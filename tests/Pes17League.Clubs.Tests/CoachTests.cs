using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Application;
using Pes17League.Clubs.Infrastructure;

namespace Pes17League.Clubs.Tests;

/// <summary>Hồ sơ HLV career (PO 19/11): tạo, việc làm/thương lượng/ký, hòm thư, chốt vòng (lương, hồi âm, quá hạn, sa thải).</summary>
public class CoachTests
{
    // ---------- luật ----------

    [Fact]
    public void Draw_is_deterministic_and_bounded()
    {
        for (var seed = 1; seed < 400; seed++)
        {
            var (o, skills, rep, trait) = CoachRules.Draw(seed);
            Assert.Equal(CoachRules.Draw(seed).Skills, skills);
            Assert.Equal(CoachRules.SkillCount, skills.Length);
            Assert.All(skills, v => Assert.InRange(v, 1, CoachRules.CreateCap));
            Assert.InRange(rep, 0, CoachRules.Origin(o).Reputation + 5);
            Assert.True(Enum.IsDefined(trait));
        }
        Assert.Equal(4, Enumerable.Range(1, 400).Select(s => CoachRules.Draw(s).Origin).Distinct().Count());
    }

    [Fact]
    public void Tiers_and_locks()
    {
        Assert.Equal(ClubTier.Elite, CoachRules.TierOf(0, 20));
        Assert.Equal(ClubTier.Big, CoachRules.TierOf(2, 20));
        Assert.Equal(ClubTier.Mid, CoachRules.TierOf(8, 20));
        Assert.Equal(ClubTier.Small, CoachRules.TierOf(19, 20));
        Assert.Null(CoachRules.LockReason(ClubTier.Small, 0, 0));
        Assert.Equal("Cần danh tiếng 75 và bằng A", CoachRules.LockReason(ClubTier.Elite, 10, 0));
        Assert.Equal("Cần bằng B", CoachRules.LockReason(ClubTier.Big, 60, 1));
        Assert.Null(CoachRules.LockReason(ClubTier.Big, 55, 2));
        Assert.True(CoachRules.OfferWage(ClubTier.Mid, 50) > CoachRules.OfferWage(ClubTier.Mid, 10));
    }

    [Fact]
    public void Chairman_judges_by_ratio_and_patience()
    {
        const long w0 = 35_000;
        var b0 = CoachRules.OfferBonus(w0);
        Assert.Equal(1.0, CoachRules.Ratio(w0, b0, 2, 50, w0), 6);
        var max = CoachRules.ChairmanMax(10);
        Assert.Equal(NegotiationOutcome.Accept, CoachRules.Judge(1.05, max, 1, 3));
        Assert.Equal(NegotiationOutcome.Counter, CoachRules.Judge(max + 0.1, max, 1, 3));
        Assert.Equal(NegotiationOutcome.Reject, CoachRules.Judge(max + 0.3, max, 1, 3));
        Assert.Equal(NegotiationOutcome.WalkAway, CoachRules.Judge(max + 0.1, max, 1, 1));
        var (w, b) = CoachRules.Middle(w0 * 16 / 10, b0 * 2, w0, b0, 2, 50, w0, max);
        Assert.True(CoachRules.Ratio(w, b, 2, 50, w0) <= max + 1e-9);
        Assert.InRange(w, w0, w0 * 16 / 10);
    }

    [Fact]
    public void Stress_lowers_people_skills_and_hints_show_direction_only()
    {
        int[] s = [12, 12, 12, 12, 12, 12, 12];
        Assert.Equal(12, CoachRules.Effective(s, 60, CoachSkill.People));
        Assert.Equal(11, CoachRules.Effective(s, 70, CoachSkill.People));
        Assert.Equal(12, CoachRules.Effective(s, 80, CoachSkill.Training));
        Assert.Equal(11, CoachRules.Effective(s, 85, CoachSkill.Training));
        Assert.Equal("Tiền ↓, Kinh nghiệm ↑, Căng thẳng ↑".Split(", ").Order(), CoachEvents.Hint(new CoachFx(Money: -50_000, Xp: 15, Stress: 2)).Split(", ").Order());
        Assert.DoesNotContain("50", CoachEvents.Hint(new CoachFx(Money: -50_000)));
        Assert.Equal(30_000, CoachRules.Severance(new CoachContract { Wage = 1_000, TotalRounds = 70, RoundsPaid = 10, SeverancePct = 50 }));
        Assert.Equal(97_000, CoachRules.Discounted(100_000, 3));
    }

    // ---------- luồng DB ----------

    private sealed record World(Season Season, Manager Manager);

    private static async Task<World> WorldAsync(TestDb t, ClubsDbContext db)
    {
        var svc = new CompetitionService(db, t.Store, t.Clock);
        var season = await svc.CreateSeasonAsync("Mùa 1", 1);
        var import = await new ImportPatchService(db, t.Store, t.Clock).ImportAsync("PES17 test", Env.OrigOptionFile, season.Id,
            [new LeagueClub(0, "Arsenal"), new LeagueClub(1)]);
        Assert.True((await svc.StartSeasonAsync(season.Id, import.PatchVersionId)).Ok);
        // Career: CLB nằm trong giải quốc nội (thị trường việc làm, BLĐ tính theo giải).
        var comp = new Competition { Name = "Premier League", Country = "Anh", Tier = 1, Type = CompetitionType.DomesticLeague, CreatedAt = t.Clock.GetUtcNow() };
        var edition = new CompetitionEdition { CompetitionId = comp.Id, SeasonId = season.Id };
        db.AddRange(comp, edition);
        db.EditionClubs.AddRange((await db.SeasonClubs.Where(x => x.SeasonId == season.Id).Select(x => x.ClubId).ToListAsync())
            .Select(id => new EditionClub { EditionId = edition.Id, ClubId = id }));
        var m = new Manager { Username = "hlv", DisplayName = "Người chơi", PasswordHash = "x" };
        db.Add(m);
        await db.SaveChangesAsync();
        return new(await db.Seasons.SingleAsync(s => s.Id == season.Id), m);
    }

    /// <summary>Vòng <paramref name="n"/> của mùa (giải test ít đội → vòng sau cùng là bản dựng tạm, không lưu).</summary>
    private static async Task<Matchday> RoundAsync(ClubsDbContext db, World w, int n) =>
        await db.Matchdays.SingleOrDefaultAsync(m => m.SeasonId == w.Season.Id && m.Number == n)
        ?? new Matchday { SeasonId = w.Season.Id, Number = n, Status = MatchdayStatus.Settled };

    /// <summary>Tạo hồ sơ tới thất nghiệp → ứng tuyển CLB mở khoá → chấp nhận đề nghị gốc → ký (giữa mùa) → họp báo ra mắt.</summary>
    private static async Task<(Coach Coach, Guid ClubId)> HireAsync(TestDb t, ClubsDbContext db, World w)
    {
        var coaches = new CoachService(db, t.Clock);
        var (c, err) = await coaches.DrawAsync(w.Manager.Id, "Nguyễn Văn Thử");
        Assert.Null(err);
        Assert.Null(await coaches.FinishAsync(w.Manager.Id));
        var jobs = new CoachJobService(db, t.Clock, t.Store);
        var rows = await jobs.ClubsAsync(c!, w.Season.Id);
        var row = rows.First(r => r.Lock is null && r.Vacant);
        var (app, openErr) = await jobs.OpenAsync(c!, row.ClubId);
        Assert.Null(openErr);
        var (reply, propErr) = await jobs.ProposeAsync(c!.Id, app!.Id, app.BaseWage, app.Bonus, 2, 50);
        Assert.Null(propErr);
        Assert.Equal(NegotiationOutcome.Accept, reply!.Outcome);
        Assert.Null(await jobs.SignAsync(c.Id, app.Id, "test"));
        return (c, row.ClubId);
    }

    [DbFact]
    public async Task Create_draws_origin_with_two_rerolls_and_points_within_pool()
    {
        await using var t = await TestDb.CreateAsync();
        await using var db = t.Context();
        var w = await WorldAsync(t, db);
        var svc = new CoachService(db, t.Clock);

        var (c, err) = await svc.DrawAsync(w.Manager.Id, "Trần Thử");
        Assert.Null(err);
        Assert.Equal(CoachStatus.Creating, c!.Status);
        Assert.Null((await svc.DrawAsync(w.Manager.Id)).Error);
        Assert.Null((await svc.DrawAsync(w.Manager.Id)).Error);
        Assert.Equal("Đã hết lượt bốc lại.", (await svc.DrawAsync(w.Manager.Id)).Error);

        var pool = CoachRules.Origin(c.Origin).FreePoints;
        var over = c.BaseSkills.ToArray();
        over[0] = Math.Min(CoachRules.CreateCap, over[0] + pool);
        over[1] = Math.Min(CoachRules.CreateCap, over[1] + pool);
        if (over.Sum() - c.BaseSkills.Sum() > pool) Assert.NotNull(await svc.SetSkillsAsync(w.Manager.Id, over));
        var ok = c.BaseSkills.ToArray();
        ok[(int)CoachSkill.Negotiation] = Math.Min(CoachRules.CreateCap, ok[(int)CoachSkill.Negotiation] + 1);
        Assert.Null(await svc.SetSkillsAsync(w.Manager.Id, ok));
        Assert.Null(await svc.SetLookAsync(w.Manager.Id, new CoachLook("quiff", "#2b1d12", "suit", "#c99a76")));
        Assert.NotNull(await svc.SetLookAsync(w.Manager.Id, new CoachLook("mohawk", "#2b1d12", "suit", "#c99a76")));
        Assert.NotNull(await svc.SetFaceAsync(w.Manager.Id, [1, 2, 3, 4, 5, 6], null));   // không phải ảnh

        Assert.Null(await svc.FinishAsync(w.Manager.Id));
        c = await svc.GetAsync(w.Manager.Id);
        Assert.Equal(CoachStatus.Unemployed, c!.Status);
        Assert.NotNull(await svc.SetSkillsAsync(w.Manager.Id, ok));                       // hết bước tạo
        Assert.Single(await db.CoachLetters.Where(l => l.CoachId == c.Id).ToListAsync());  // thư chào mừng
        Assert.Empty(await db.CoachContracts.Where(k => k.CoachId == c.Id).ToListAsync());
    }

    [DbFact]
    public async Task Career_in_progress_gets_default_contract()
    {
        await using var t = await TestDb.CreateAsync();
        await using var db = t.Context();
        var w = await WorldAsync(t, db);
        var club = await db.EditionClubs.Select(x => x.ClubId).FirstAsync();
        await new ClubAdminService(db, t.Clock, t.Store).SetHumanClubAsync(club, "test", w.Manager.Id);

        var svc = new CoachService(db, t.Clock);
        await svc.DrawAsync(w.Manager.Id, "Lê Thử");
        Assert.Null(await svc.FinishAsync(w.Manager.Id));
        var p = await svc.ProfileAsync(w.Manager.Id);
        Assert.Equal(CoachStatus.Employed, p!.Coach.Status);
        Assert.Equal(club, p.Contract!.ClubId);
        Assert.True(p.Contract.Launched);
        Assert.Equal(CoachRules.DefaultSeasons, p.Contract.Seasons);
    }

    [DbFact]
    public async Task Apply_negotiate_sign_launch_and_request_answered_next_round()
    {
        await using var t = await TestDb.CreateAsync();
        await using var db = t.Context();
        var w = await WorldAsync(t, db);
        var (c, clubId) = await HireAsync(t, db, w);

        var human = await db.TeamAssignments.SingleAsync(a => a.To == null && a.ControllerType == ControllerType.Human && a.ParticipantId == w.Manager.Id);
        Assert.Equal(clubId, human.ClubId);
        var k = await db.CoachContracts.SingleAsync(x => x.CoachId == c.Id && x.Status == CoachContractStatus.Active);
        Assert.False(k.Launched);
        var jobs = new CoachJobService(db, t.Clock, t.Store);
        Assert.Null((await jobs.LaunchAsync(c.Id, [0, 1, 2])).Error);
        Assert.Equal("Đã họp báo ra mắt.", (await jobs.LaunchAsync(c.Id, [0, 0, 0])).Error);

        // Yêu cầu BLĐ: 1 lần / 6 vòng, hồi âm vòng sau.
        var letters = new CoachLetterService(db, t.Clock);
        Assert.Null(await letters.SendAsync(c.Id, "tang-ngan-sach", null, null));
        Assert.NotNull(await letters.SendAsync(c.Id, "tang-ngan-sach", null, null));
        Assert.NotNull(await letters.SendAsync(c.Id, "khen", Guid.NewGuid(), null));      // không phải cầu thủ của CLB
        var req = await db.CoachLetters.SingleAsync(l => l.CoachId == c.Id && l.Outgoing && l.Key == "tang-ngan-sach");
        Assert.Null(req.AnsweredTick);

        var md1 = await RoundAsync(db, w, 1);
        await new CoachRoundService(db, t.Clock, t.Store).ApplyRoundAsync(md1, false);
        await db.Entry(req).ReloadAsync();
        Assert.NotNull(req.AnsweredTick);
        Assert.True(await db.CoachLetters.AnyAsync(l => l.CoachId == c.Id && l.ReplyToId == req.Id && l.Kind == LetterKind.Reply));
        var wallet = await CoachService.BalanceAsync(db, c.Id);
        Assert.Contains(await db.CoachLedger.Where(e => e.CoachId == c.Id).ToListAsync(), e => e.Kind == CoachMoney.Wage && e.Amount == k.Wage);

        // Chốt lại cùng vòng: không trả lương 2 lần.
        await new CoachRoundService(db, t.Clock, t.Store).ApplyRoundAsync(md1, false);
        Assert.Equal(wallet, await CoachService.BalanceAsync(db, c.Id));
    }

    [DbFact]
    public async Task Overdue_decision_takes_default_option()
    {
        await using var t = await TestDb.CreateAsync();
        await using var db = t.Context();
        var w = await WorldAsync(t, db);
        var svc = new CoachService(db, t.Clock);
        var (c, _) = await svc.DrawAsync(w.Manager.Id, "Phạm Thử");
        await svc.FinishAsync(w.Manager.Id);
        var e = CoachEvents.Find("hoi-thao")!;
        var l = new CoachLetter
        {
            CoachId = c!.Id, Kind = LetterKind.Decision, From = e.From, Sender = e.Sender, Subject = e.Subject, Body = e.Body, Key = e.Key,
            Options = string.Join('\n', e.Options.Select(o => o.Label)), DefaultOption = e.Default, DeadlineTick = CoachRules.Tick(1, 2),
            Tick = CoachRules.Tick(1, 0), At = t.Clock.GetUtcNow(),
        };
        db.CoachLetters.Add(l);
        await db.SaveChangesAsync();

        var rounds = new CoachRoundService(db, t.Clock, t.Store);
        await rounds.ApplyRoundAsync(await RoundAsync(db, w, 1), false);
        await db.Entry(l).ReloadAsync();
        Assert.Null(l.Chosen);
        await rounds.ApplyRoundAsync(await RoundAsync(db, w, 2), false);
        await db.Entry(l).ReloadAsync();
        Assert.Equal(e.Default, l.Chosen);
        Assert.Contains("Hết hạn", l.Body);
        Assert.Equal("Đã quyết định thư này.", (await new CoachLetterService(db, t.Clock).ChooseAsync(c.Id, l.Id, 0)).Error);
    }

    [DbFact]
    public async Task Low_confidence_after_round_8_sacks_with_severance()
    {
        await using var t = await TestDb.CreateAsync();
        await using var db = t.Context();
        var w = await WorldAsync(t, db);
        var (c, clubId) = await HireAsync(t, db, w);
        var board = await db.BoardConfidences.SingleAsync(b => b.ClubId == clubId && b.SeasonId == w.Season.Id);
        board.Confidence = CoachRules.SackBelow - 5;
        await db.SaveChangesAsync();

        await new CoachRoundService(db, t.Clock, t.Store).ApplyRoundAsync(await RoundAsync(db, w, CoachRules.SackAfterRound + 1), false);

        var k = await db.CoachContracts.AsNoTracking().SingleAsync(x => x.CoachId == c.Id);
        Assert.Equal(CoachContractStatus.Sacked, k.Status);
        var coach = await db.Coaches.AsNoTracking().SingleAsync(x => x.Id == c.Id);
        Assert.Equal(CoachStatus.Unemployed, coach.Status);
        Assert.False(await db.TeamAssignments.AnyAsync(a => a.To == null && a.ControllerType == ControllerType.Human && a.ParticipantId == w.Manager.Id));
        Assert.True(await db.CoachLedger.AnyAsync(e => e.CoachId == c.Id && e.Kind == CoachMoney.Severance && e.Amount > 0));
        Assert.True(await db.CoachLetters.AnyAsync(l => l.CoachId == c.Id && l.Key == "sa-thai"));
    }

    [DbFact]
    public async Task System_mail_goes_to_coach_inbox_and_no_coach_round_is_noop()
    {
        await using var t = await TestDb.CreateAsync();
        await using var db = t.Context();
        var w = await WorldAsync(t, db);

        await new CoachRoundService(db, t.Clock, t.Store).ApplyRoundAsync(await RoundAsync(db, w, 1), false);   // giải online: không hồ sơ HLV
        Assert.False(await db.CoachLedger.AnyAsync());

        var mail = new MailService(db, t.Clock);
        await mail.SystemAsync(w.Manager.Id, "Trước khi có HLV", "…");
        var svc = new CoachService(db, t.Clock);
        var (c, _) = await svc.DrawAsync(w.Manager.Id, "Võ Thử");
        await mail.SystemAsync(w.Manager.Id, "Đang tạo HLV", "…");                      // còn bước tạo → hòm thư thường
        await svc.FinishAsync(w.Manager.Id);
        await mail.SystemAsync(w.Manager.Id, "Có đề nghị mới", "…");
        Assert.True(await db.CoachLetters.AnyAsync(l => l.CoachId == c!.Id && l.Subject == "Có đề nghị mới" && l.Sender == MailService.SystemName));
        Assert.False(await db.CoachLetters.AnyAsync(l => l.Subject == "Đang tạo HLV"));
        var inbox = await mail.ListAsync(w.Manager.Id, MailFolder.Inbox);
        Assert.Equal(2, inbox.Count(x => x.System));
    }
}
