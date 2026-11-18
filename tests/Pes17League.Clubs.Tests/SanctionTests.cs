using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Application;
using Pes17League.Clubs.Infrastructure;

namespace Pes17League.Clubs.Tests;

/// <summary>Thưởng / phạt của Ban tổ chức (PO duyệt 17/11).</summary>
public class SanctionTests
{
    private sealed record World(Season Season, CompetitionEdition Edition, Manager A, Club CA, Club CB, Fixture Played, Fixture F2, Fixture F3);

    private static async Task<World> SeedAsync(ClubsDbContext db, DateTimeOffset now)
    {
        var season = new Season { Name = "Mùa 1", Number = 2, CreatedAt = now, Status = SeasonStatus.Active };
        var comp = new Competition { Name = "Serie A", Type = CompetitionType.DomesticLeague, CreatedAt = now };
        var edition = new CompetitionEdition { CompetitionId = comp.Id, SeasonId = season.Id };
        var a = new Manager { Username = "an", DisplayName = "An", PasswordHash = "x" };
        Club ca = new() { Name = "CLB A" }, cb = new() { Name = "CLB B" };
        db.AddRange(season, comp, edition, a, ca, cb);
        db.SeasonClubs.AddRange(new SeasonClub { SeasonId = season.Id, ClubId = ca.Id }, new SeasonClub { SeasonId = season.Id, ClubId = cb.Id });
        db.EditionClubs.AddRange(new EditionClub { EditionId = edition.Id, ClubId = ca.Id }, new EditionClub { EditionId = edition.Id, ClubId = cb.Id });
        db.TeamAssignments.AddRange(
            new TeamAssignment { ClubId = ca.Id, ParticipantId = a.Id, ControllerType = ControllerType.Human, From = now },
            new TeamAssignment { ClubId = cb.Id, ControllerType = ControllerType.Bot, From = now });
        Fixture F(int n, FixtureStatus st)
        {
            var md = new Matchday { SeasonId = season.Id, Number = n, Status = MatchdayStatus.WindowOpen };
            var f = new Fixture { MatchdayId = md.Id, HomeClubId = ca.Id, AwayClubId = cb.Id, CompetitionEditionId = edition.Id, Stage = FixtureStage.League, Status = st };
            if (st == FixtureStatus.Played) (f.HomeGoals, f.AwayGoals) = (2, 0);
            db.AddRange(md, f);
            return f;
        }
        var played = F(1, FixtureStatus.Played);
        var f3 = F(3, FixtureStatus.Scheduled);
        var f2 = F(2, FixtureStatus.Scheduled);
        await db.SaveChangesAsync();
        return new(season, edition, a, ca, cb, played, f2, f3);
    }

    [DbFact]
    public async Task Fine_bonus_book_ledger_and_revoke_reverses_with_mail()
    {
        await using var t = await TestDb.CreateAsync();
        await using var db = t.Context();
        var w = await SeedAsync(db, t.Clock.GetUtcNow());
        var svc = new SanctionService(db, t.Clock);

        await Assert.ThrowsAsync<AdminRuleException>(() => svc.IssueAsync(SanctionKind.Fine, w.CA.Id, 100, " ", "admin"));      // thiếu lý do
        await Assert.ThrowsAsync<AdminRuleException>(() => svc.IssueAsync(SanctionKind.Fine, w.CA.Id, 0, "bỏ trận", "admin"));  // tiền 0
        var fine = await svc.IssueAsync(SanctionKind.Fine, w.CA.Id, 500_000, "Bỏ trận không báo", "admin");
        await svc.IssueAsync(SanctionKind.Bonus, w.CA.Id, 200_000, "Fair-play", "admin");
        await svc.IssueAsync(SanctionKind.Warning, w.CA.Id, 99, "Phát ngôn", "admin");
        Assert.Equal(w.A.Id, fine.ManagerId);
        Assert.Equal(-300_000, await db.Ledger.Where(l => l.ClubId == w.CA.Id).SumAsync(l => l.Amount));

        await svc.RevokeAsync(fine.Id, "Khiếu nại đúng", "admin");
        Assert.Equal(200_000, await db.Ledger.Where(l => l.ClubId == w.CA.Id).SumAsync(l => l.Amount));
        await Assert.ThrowsAsync<AdminRuleException>(() => svc.RevokeAsync(fine.Id, "", "admin"));
        Assert.Equal(4, await db.MailRecipients.CountAsync(r => r.ManagerId == w.A.Id));   // 3 quyết định + 1 huỷ
        Assert.Equal(4, await db.AdminAudit.CountAsync(a => a.EntityType == nameof(Sanction)));
        var list = await svc.ListAsync();
        Assert.Equal(3, list.Count);
        Assert.Equal(0, list.Single(x => x.S.Kind == SanctionKind.Warning).S.Amount);
        Assert.False(list.Single(x => x.S.Id == fine.Id).S.Active);
    }

    [DbFact]
    public async Task Points_deduction_applies_to_edition_and_season_standings()
    {
        await using var t = await TestDb.CreateAsync();
        await using var db = t.Context();
        var w = await SeedAsync(db, t.Clock.GetUtcNow());
        var svc = new SanctionService(db, t.Clock);
        var comp = new CompetitionService(db, t.Store, t.Clock);

        var s = await svc.IssueAsync(SanctionKind.PointsDeduction, w.CA.Id, 6, "Dàn xếp tỷ số", "admin", w.Edition.Id);
        var table = await comp.GetEditionStandingsAsync(w.Edition.Id);
        Assert.Equal(w.CB.Id, table[0].Club);                    // A thắng 3 điểm, bị trừ 6 → -3, xếp sau B
        Assert.Equal(-3, table.Single(r => r.Club == w.CA.Id).Points);
        Assert.Equal(3, (await comp.GetStandingsAsync(w.Season.Id)).Single(r => r.Club == w.CA.Id).Points);   // trừ theo giải, không trừ BXH mùa

        await svc.RevokeAsync(s.Id, "", "admin");
        Assert.Equal(3, (await comp.GetEditionStandingsAsync(w.Edition.Id)).Single(r => r.Club == w.CA.Id).Points);
    }

    [DbFact]
    public async Task Ban_hands_next_fixtures_to_assistant_until_revoked()
    {
        await using var t = await TestDb.CreateAsync();
        await using var db = t.Context();
        var w = await SeedAsync(db, t.Clock.GetUtcNow());
        var svc = new SanctionService(db, t.Clock);

        await Assert.ThrowsAsync<AdminRuleException>(() => svc.IssueAsync(SanctionKind.Ban, w.CB.Id, 1, "Gây rối", "admin"));   // CLB BOT: không có HLV
        var ban = await svc.IssueAsync(SanctionKind.Ban, w.CA.Id, 1, "Gây rối", "admin");
        await db.Entry(w.F2).ReloadAsync();
        await db.Entry(w.F3).ReloadAsync();
        Assert.True(w.F2.AssistantPlays);                       // trận chưa đá gần nhất (vòng 2), không phải vòng 3
        Assert.False(w.F3.AssistantPlays);
        Assert.False(w.F2.HomeAbsent);                          // không tính vắng
        Assert.True(await SanctionService.IsBannedAsync(db, w.F2.Id));
        Assert.False(await SanctionService.IsBannedAsync(db, w.F3.Id));

        await svc.RevokeAsync(ban.Id, "", "admin");
        await db.Entry(w.F2).ReloadAsync();
        Assert.False(w.F2.AssistantPlays);
        Assert.False(await SanctionService.IsBannedAsync(db, w.F2.Id));
    }
}
