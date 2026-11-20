using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Infrastructure;

namespace Pes17League.Clubs.Application;

/// <summary>CLB trên thị trường việc làm HLV: bậc (theo giá trị đội trong giải), mục tiêu, lương đề nghị, lý do khoá.</summary>
public sealed record CoachClubRow(Guid ClubId, string Name, string? LogoPath, string League, string Country, int LeagueTier, double Ovr, long SquadValue,
    long Budget, IReadOnlyList<string> Cups, ClubTier Tier, int Expected, int Teams, string Objective, long Wage, string? Lock, bool Refused, bool Vacant,
    int? Confidence);

public sealed record NegotiationReply(NegotiationOutcome Outcome, string Message);

/// <summary>
/// Việc làm HLV (PO 19/11): ứng tuyển / được mời → thương lượng với chủ tịch (3 lần kiên nhẫn) → ký hợp đồng → họp báo ra mắt.
/// CLB lớn khoá theo danh tiếng + bằng (<see cref="CoachRules.LockReason"/>). Bị sa thải / hết hạn / từ chức → thất nghiệp, đi tìm việc.
/// Ký lần đầu ở mùa chờ bắt đầu = bắt đầu career (<see cref="Pes17CareerService.StartAsync"/>); giữa mùa = đổi CLB người cầm.
/// </summary>
public sealed class CoachJobService(ClubsDbContext db, TimeProvider clock, IOptionFileStore? store = null)
{
    // ---------- danh sách CLB ----------

    /// <summary>CLB dự giải quốc nội của mùa <paramref name="seasonId"/> kèm bậc/khoá. <paramref name="choices"/> (wizard) dùng lại số liệu đã tính.</summary>
    public async Task<IReadOnlyList<CoachClubRow>> ClubsAsync(Coach c, Guid seasonId, IReadOnlyList<CareerClubChoice>? choices = null, CancellationToken ct = default)
    {
        var season = await db.Seasons.AsNoTracking().SingleAsync(s => s.Id == seasonId, ct);
        List<CareerClubChoice> list;
        if (choices is not null) list = [.. choices];
        else
        {
            var rows = await (from x in db.EditionClubs join e in db.Editions on x.EditionId equals e.Id join comp in db.Competitions on e.CompetitionId equals comp.Id
                              where e.SeasonId == seasonId && comp.Type == CompetitionType.DomesticLeague
                              select new { x.ClubId, comp.Name, comp.Country, comp.Tier }).ToListAsync(ct);
            var value = (await new EconomyAdminService(db, clock).PreviewAsync(seasonId, null, ct)).Clubs.ToDictionary(x => x.ClubId, x => x.SquadValue);
            var ovr = season.PatchVersionId is { } patch
                ? (await new DivisionService(db, clock).StrengthsAsync(seasonId, patch, ct)).ToDictionary(x => x.ClubId, x => x.Ovr) : [];
            var ids = rows.Select(r => r.ClubId).ToList();
            var money = await db.Ledger.Where(l => ids.Contains(l.ClubId)).GroupBy(l => l.ClubId).Select(g => new { g.Key, S = g.Sum(l => l.Amount) }).ToDictionaryAsync(x => x.Key, x => x.S, ct);
            var clubs = await db.Clubs.Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => new { x.Name, x.LogoPath }, ct);
            list = [.. rows.Where(r => clubs.ContainsKey(r.ClubId)).Select(r => new CareerClubChoice(r.ClubId, clubs[r.ClubId].Name, clubs[r.ClubId].LogoPath, r.Name,
                r.Country ?? "", r.Tier ?? 1, Math.Round(ovr.GetValueOrDefault(r.ClubId), 1), value.GetValueOrDefault(r.ClubId), money.GetValueOrDefault(r.ClubId), []))];
        }
        var refused = (await db.CoachApplications.Where(a => a.CoachId == c.Id && a.SeasonId == seasonId && a.Status == CoachApplicationStatus.Refused)
            .Select(a => a.ClubId).ToListAsync(ct)).ToHashSet();
        var humans = (await db.TeamAssignments.Where(a => a.To == null && a.ControllerType == ControllerType.Human && a.ParticipantId != null)
            .Select(a => new { a.ClubId, a.ParticipantId }).ToListAsync(ct));
        var early = season.Status == SeasonStatus.Planned || !await db.Matchdays.AnyAsync(m => m.SeasonId == seasonId && m.Status == MatchdayStatus.Settled, ct);
        var conf = await db.BoardConfidences.Where(b => b.SeasonId == seasonId).ToDictionaryAsync(b => b.ClubId, b => b.Confidence, ct);
        var result = new List<CoachClubRow>();
        foreach (var league in list.GroupBy(x => x.League))
        {
            var ranked = league.OrderByDescending(x => x.SquadValue).ThenBy(x => x.ClubId).ToList();
            for (var i = 0; i < ranked.Count; i++)
            {
                var x = ranked[i];
                var tier = CoachRules.TierOf(i, ranked.Count);
                var human = humans.FirstOrDefault(h => h.ClubId == x.ClubId);
                int? confidence = conf.TryGetValue(x.ClubId, out var cf) ? cf : null;
                var vacant = human is null && (early || (confidence ?? BoardRules.StartConfidence) < CoachRules.VacancyBelow);
                result.Add(new(x.ClubId, x.Name, x.LogoPath, x.League, x.Country, x.Tier, x.Ovr, x.SquadValue, x.Budget, x.Cups, tier, i + 1, ranked.Count,
                    BoardRules.Objective(i + 1, ranked.Count), CoachRules.OfferWage(tier, c.Reputation), CoachRules.LockReason(tier, c.Reputation, c.License),
                    refused.Contains(x.ClubId), vacant, confidence));
            }
        }
        return result;
    }

    /// <summary>Mùa để tìm việc: mùa chưa xong sớm nhất (Planned / Active).</summary>
    public Task<Season?> JobSeasonAsync(CancellationToken ct = default) =>
        db.Seasons.AsNoTracking().Where(s => s.Status != SeasonStatus.Finished).OrderBy(s => s.Number).FirstOrDefaultAsync(ct);

    // ---------- ứng tuyển, thương lượng ----------

    /// <summary>Ứng tuyển (hoặc nhận lời mời) → mở bàn thương lượng với đề nghị gốc của chủ tịch.</summary>
    public async Task<(CoachApplication? App, string? Error)> OpenAsync(Coach c, Guid clubId, bool invited = false, CancellationToken ct = default)
    {
        if (c.Status == CoachStatus.Creating) return (null, "Hoàn tất hồ sơ HLV trước.");
        if (await JobSeasonAsync(ct) is not { } season) return (null, "Chưa có mùa giải để tìm việc. Sang mùa mới trước.");
        var current = await db.CoachContracts.SingleOrDefaultAsync(k => k.CoachId == c.Id && k.Status == CoachContractStatus.Active, ct);
        if (current?.ClubId == clubId) return (null, "Ông đang dẫn dắt CLB này.");
        var open = await db.CoachApplications.FirstOrDefaultAsync(a => a.CoachId == c.Id && a.SeasonId == season.Id && a.ClubId == clubId
            && (a.Status == CoachApplicationStatus.Open || a.Status == CoachApplicationStatus.Agreed), ct);
        if (open is not null) return (open, null);
        var row = (await ClubsAsync(c, season.Id, null, ct)).FirstOrDefault(r => r.ClubId == clubId);
        if (row is null) return (null, "CLB không dự giải quốc nội mùa này.");
        if (row.Refused) return (null, $"{row.Name} đã từ chối ông mùa này.");
        if (row.Lock is { } why) return (null, $"{row.Name}: {why}.");
        if (!row.Vacant && !invited) return (null, $"{row.Name} chưa có ý định thay HLV.");
        return (await CreateAppAsync(c, season, row, invited, ct), null);
    }

    /// <summary>Wizard career: ứng tuyển CLB đã chọn ở mùa chờ bắt đầu (số liệu từ <paramref name="row"/>).</summary>
    public async Task<(CoachApplication? App, string? Error)> OpenAsync(Coach c, Season season, CoachClubRow row, CancellationToken ct = default)
    {
        if (c.Status == CoachStatus.Creating) return (null, "Hoàn tất hồ sơ HLV trước.");
        if (row.Refused) return (null, $"{row.Name} đã từ chối ông mùa này.");
        if (row.Lock is { } why) return (null, $"{row.Name}: {why}.");
        var open = await db.CoachApplications.FirstOrDefaultAsync(a => a.CoachId == c.Id && a.SeasonId == season.Id && a.ClubId == row.ClubId
            && (a.Status == CoachApplicationStatus.Open || a.Status == CoachApplicationStatus.Agreed), ct);
        return (open ?? await CreateAppAsync(c, season, row, false, ct), null);
    }

    async Task<CoachApplication> CreateAppAsync(Coach c, Season season, CoachClubRow row, bool invited, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var tick = await CoachService.NowTickAsync(db, ct);
        var w0 = row.Wage;
        var greet = $"Chào ông {LastName(c.Name)}. {row.Name} muốn mời ông dẫn dắt đội, mục tiêu mùa này: {row.Objective.ToLowerInvariant()}. Đây là đề nghị của chúng tôi.";
        var app = new CoachApplication
        {
            CoachId = c.Id, ClubId = row.ClubId, SeasonId = season.Id, Status = CoachApplicationStatus.Open, Invited = invited, BaseWage = w0, Wage = w0,
            Bonus = CoachRules.OfferBonus(w0), Seasons = CoachRules.DefaultSeasons, SeverancePct = CoachRules.DefaultSeverance, Log = "C|" + greet, Tick = tick, At = now,
        };
        db.CoachApplications.Add(app);
        var chairman = CoachRules.Chairman(row.ClubId);
        if (!invited)
        {
            var sent = CoachLetterService.Write(db, c, LetterKind.Request, LetterFrom.Board, $"Chủ tịch {row.Name}", $"Đơn ứng tuyển HLV trưởng {row.Name}",
                $"Kính gửi {chairman},\n\nTôi là {c.Name}, {CoachRules.Origin(c.Origin).Name.ToLowerInvariant()}, bằng {CoachRules.Licenses[c.License]}, " +
                $"danh tiếng {c.Reputation}. Tôi mong được dẫn dắt {row.Name} và tự tin hoàn thành mục tiêu {row.Objective.ToLowerInvariant()}.\n\n{c.Name}",
                tick, now, row.ClubId, key: "ung-tuyen");
            sent.Outgoing = true;
            sent.Read = true;
            sent.AnsweredTick = tick;
        }
        CoachLetterService.Write(db, c, LetterKind.Official, LetterFrom.Board, $"{chairman}, chủ tịch {row.Name}", $"Mời thương lượng hợp đồng HLV trưởng",
            $"{greet}\n\nLương {MoneyText.Format(w0)}/vòng, thưởng {MoneyText.Format(app.Bonus)} nếu đạt mục tiêu, {app.Seasons} mùa, bồi thường {app.SeverancePct}% lương còn lại nếu bị sa thải.",
            tick, now, row.ClubId, key: "thuong-luong", link: "hlv/viec-lam");
        await db.SaveChangesAsync(ct);
        return app;
    }

    static string LastName(string name) => name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? name;

    public Task<CoachApplication?> ApplicationAsync(Guid coachId, Guid appId, CancellationToken ct = default) =>
        db.CoachApplications.SingleOrDefaultAsync(a => a.Id == appId && a.CoachId == coachId, ct);

    public async Task<IReadOnlyList<CoachApplication>> OpenApplicationsAsync(Guid coachId, CancellationToken ct = default) =>
        await db.CoachApplications.AsNoTracking().Where(a => a.CoachId == coachId && (a.Status == CoachApplicationStatus.Open || a.Status == CoachApplicationStatus.Agreed))
            .OrderByDescending(a => a.At).ToListAsync(ct);

    public static double MaxFor(Coach c) => CoachRules.ChairmanMax(CoachRules.Effective(c, CoachSkill.Negotiation));

    /// <summary>Đưa đề nghị cho chủ tịch. Lương 0,8–1,6× gốc, thưởng 0,5–2× gốc, 1–3 mùa, bồi thường 25/50/100%.</summary>
    public async Task<(NegotiationReply? Reply, string? Error)> ProposeAsync(Guid coachId, Guid appId, long wage, long bonus, int seasons, int severance,
        CancellationToken ct = default)
    {
        if (await ApplicationAsync(coachId, appId, ct) is not { Status: CoachApplicationStatus.Open } a) return (null, "Bàn thương lượng đã đóng.");
        var c = await db.Coaches.SingleAsync(x => x.Id == coachId, ct);
        var b0 = CoachRules.OfferBonus(a.BaseWage);
        if (wage < a.BaseWage * 8 / 10 || wage > a.BaseWage * 16 / 10 || bonus < b0 / 2 || bonus > b0 * 2 || seasons is < 1 or > 3 || severance is not (25 or 50 or 100))
            return (null, "Điều khoản ngoài khung thương lượng.");
        var max = MaxFor(c);
        var ratio = CoachRules.Ratio(wage, bonus, seasons, severance, a.BaseWage);
        var current = CoachRules.Ratio(a.Wage, a.Bonus, a.Seasons, a.SeverancePct, a.BaseWage);
        var outcome = CoachRules.Judge(ratio, max, current, a.Patience);
        var club = await db.Clubs.Where(x => x.Id == a.ClubId).Select(x => x.Name).SingleAsync(ct);
        string msg;
        switch (outcome)
        {
            case NegotiationOutcome.Accept:
                (a.Wage, a.Bonus, a.Seasons, a.SeverancePct) = (wage, bonus, seasons, severance);
                a.Status = CoachApplicationStatus.Agreed;
                msg = "Được, chúng tôi đồng ý. Mời ông sang ký hợp đồng.";
                break;
            case NegotiationOutcome.Counter:
                a.Patience--;
                var (w, bb) = CoachRules.Middle(wage, bonus, a.Wage, a.Bonus, seasons, severance, a.BaseWage, max);
                (a.Wage, a.Bonus, a.Seasons, a.SeverancePct) = (w, bb, seasons, severance);
                msg = $"Hơi cao. Tôi gặp ông ở giữa: lương {MoneyText.Format(w)}/vòng, thưởng {MoneyText.Format(bb)}. Ông xem thế nào?";
                break;
            case NegotiationOutcome.Reject:
                a.Patience--;
                msg = "Mức đó vượt xa khả năng của CLB. Ông cân nhắc lại.";
                break;
            default:
                a.Patience = 0;
                a.Status = CoachApplicationStatus.Refused;
                msg = $"Chúng tôi không đáp ứng được. {club} sẽ tìm người khác.";
                CoachLetterService.Write(db, c, LetterKind.Official, LetterFrom.Board, $"Chủ tịch {club}", "Kết thúc thương lượng",
                    $"{club} rất tiếc không thể đi đến thoả thuận với ông. CLB sẽ không tiếp tục đàm phán trong mùa này.", await CoachService.NowTickAsync(db, ct),
                    clock.GetUtcNow(), a.ClubId, key: "thuong-luong");
                break;
        }
        a.Log = $"H|Anh đề nghị lương {MoneyText.Format(wage)}/vòng, thưởng {MoneyText.Format(bonus)}, {seasons} mùa, bồi thường {severance}%.\nC|{msg}\n{a.Log}";
        await db.SaveChangesAsync(ct);
        return (new(outcome, msg), null);
    }

    /// <summary>Chấp nhận đề nghị hiện tại của chủ tịch.</summary>
    public async Task<string?> AcceptAsync(Guid coachId, Guid appId, CancellationToken ct = default)
    {
        if (await ApplicationAsync(coachId, appId, ct) is not { Status: CoachApplicationStatus.Open } a) return "Bàn thương lượng đã đóng.";
        a.Status = CoachApplicationStatus.Agreed;
        a.Log = $"H|Tôi đồng ý với đề nghị của ông.\nC|Tuyệt. Mời ông sang ký hợp đồng.\n{a.Log}";
        await db.SaveChangesAsync(ct);
        return null;
    }

    public async Task WithdrawAsync(Guid coachId, Guid appId, CancellationToken ct = default)
    {
        if (await ApplicationAsync(coachId, appId, ct) is { Status: CoachApplicationStatus.Open or CoachApplicationStatus.Agreed } a)
        {
            a.Status = CoachApplicationStatus.Withdrawn;
            await db.SaveChangesAsync(ct);
        }
    }

    // ---------- ký ----------

    /// <summary>
    /// Ký hợp đồng đã thoả thuận. Chưa có ai cầm CLB ở mùa chờ bắt đầu → bắt đầu career với CLB này; không thì đổi CLB người chơi cầm
    /// (đang dẫn CLB khác → hợp đồng cũ chấm dứt, không bồi thường).
    /// </summary>
    public async Task<string?> SignAsync(Guid coachId, Guid appId, string actor, CancellationToken ct = default)
    {
        if (await ApplicationAsync(coachId, appId, ct) is not { Status: CoachApplicationStatus.Agreed } a) return "Chưa thoả thuận xong điều khoản.";
        if (store is null) throw new InvalidOperationException("Cần IOptionFileStore để ký hợp đồng");
        var c = await db.Coaches.SingleAsync(x => x.Id == coachId, ct);
        var career = new Pes17CareerService(db, store, clock);
        try
        {
            if (await career.PendingSeasonAsync(ct) is { } pending && pending.Id == a.SeasonId && !await career.HasCoachAsync(ct))
                await career.StartAsync(a.ClubId, c.ManagerId, Random.Shared.Next(1, 10_000), actor, ct);
            else
                await new ClubAdminService(db, clock, store).SetHumanClubAsync(a.ClubId, actor, c.ManagerId, ct);
        }
        catch (AdminRuleException e) { return e.Message; }
        var tick = await CoachService.NowTickAsync(db, ct);
        var now = clock.GetUtcNow();
        foreach (var old in await db.CoachContracts.Where(k => k.CoachId == c.Id && k.Status == CoachContractStatus.Active).ToListAsync(ct))
        {
            old.Status = CoachContractStatus.Resigned;
            old.EndedTick = tick;
        }
        await CreateContractAsync(c, a.ClubId, a.Wage, a.Bonus, a.Seasons, a.SeverancePct, launched: false, tick, now, ct);
        a.Status = CoachApplicationStatus.Signed;
        foreach (var other in await db.CoachApplications.Where(x => x.CoachId == c.Id && x.Id != a.Id
                     && (x.Status == CoachApplicationStatus.Open || x.Status == CoachApplicationStatus.Agreed)).ToListAsync(ct))
            other.Status = CoachApplicationStatus.Withdrawn;
        await db.SaveChangesAsync(ct);
        return null;
    }

    /// <summary>Hợp đồng mặc định cho career đang dở (PO 19/11): bậc CLB, lương đề nghị gốc, 2 mùa, bồi thường 50%, coi như đã ra mắt.</summary>
    public async Task DefaultContractAsync(Coach c, Guid clubId, CancellationToken ct = default)
    {
        if (await JobSeasonAsync(ct) is not { } season) return;
        var row = (await ClubsAsync(c, season.Id, null, ct)).FirstOrDefault(r => r.ClubId == clubId);
        var wage = row?.Wage ?? CoachRules.OfferWage(ClubTier.Small, c.Reputation);
        await CreateContractAsync(c, clubId, wage, CoachRules.OfferBonus(wage), CoachRules.DefaultSeasons, CoachRules.DefaultSeverance, launched: true,
            await CoachService.NowTickAsync(db, ct), clock.GetUtcNow(), ct);
        await db.SaveChangesAsync(ct);
    }

    async Task CreateContractAsync(Coach c, Guid clubId, long wage, long bonus, int seasons, int severance, bool launched, int tick, DateTimeOffset now,
        CancellationToken ct)
    {
        var season = await JobSeasonAsync(ct) ?? throw new AdminRuleException("Chưa có mùa giải");
        var rounds = await db.Matchdays.CountAsync(m => m.SeasonId == season.Id, ct);
        var settled = await db.Matchdays.CountAsync(m => m.SeasonId == season.Id && m.Status == MatchdayStatus.Settled, ct);
        var status = await new BoardService(db, clock).StatusAsync(season.Id, clubId, ct);
        var k = new CoachContract
        {
            CoachId = c.Id, ClubId = clubId, SeasonId = season.Id, Seasons = seasons, EndSeasonNumber = season.Number + seasons - 1, Wage = wage, Bonus = bonus,
            SeverancePct = severance, ExpectedPosition = status?.ExpectedPosition ?? 0, Objective = status?.Objective ?? "Trụ hạng",
            Status = CoachContractStatus.Active, TotalRounds = Math.Max(1, rounds - settled + (seasons - 1) * rounds), SignedTick = tick, SignedAt = now, Launched = launched,
        };
        db.CoachContracts.Add(k);
        // HLV mới: BLĐ cho cơ hội làm lại (tin cậy về mức khởi điểm).
        if (await new CoachEffects(db, clock).BoardAsync(season.Id, clubId, ct) is { } b)
        {
            b.Confidence = BoardRules.StartConfidence;
            b.UpdatedAt = now;
        }
        c.Status = CoachStatus.Employed;
        c.WarnedTick = 0;
        c.UpdatedAt = now;
        var club = await db.Clubs.Where(x => x.Id == clubId).Select(x => x.Name).SingleAsync(ct);
        var chairman = CoachRules.Chairman(clubId);
        CoachLetterService.Write(db, c, LetterKind.Official, LetterFrom.Board, $"{chairman}, chủ tịch {club}", $"Hợp đồng HLV trưởng {club} có hiệu lực",
            $"Chức danh: HLV trưởng {club}.\nThời hạn: {seasons} mùa (hết mùa {k.EndSeasonNumber}).\nLương: {MoneyText.Format(wage)}/vòng, trả vào ví HLV.\n" +
            $"Thưởng đạt mục tiêu: {MoneyText.Format(bonus)}.\nMục tiêu mùa: {k.Objective}" + (k.ExpectedPosition > 0 ? $" (hạng {k.ExpectedPosition})" : "") +
            $".\nBồi thường nếu bị sa thải: {severance}% lương còn lại.", tick, now, clubId, key: "hop-dong", link: "hlv");
        CoachLetterService.Write(db, c, LetterKind.Info, LetterFrom.Board, $"{chairman}, chủ tịch {club}", "Thư chào mừng của chủ tịch",
            $"Chào mừng ông tới {club}. Ban lãnh đạo kỳ vọng ông hoàn thành mục tiêu {k.Objective.ToLowerInvariant()}. Cửa phòng tôi luôn mở: " +
            "ông có thể gửi thư cho ban lãnh đạo khi cần ngân sách, nhân sự hay điều chỉnh mục tiêu.", tick, now, clubId, key: "chao-mung");
    }

    // ---------- họp báo ra mắt ----------

    /// <summary>Họp báo ra mắt (3 câu, chọn 1 đáp án mỗi câu): CĐV (‰ khán giả), tin cậy BLĐ, danh tiếng. Trả về tổng tác động.</summary>
    public async Task<(int Fans, int Board, int Rep, string? Error)> LaunchAsync(Guid coachId, int[] answers, CancellationToken ct = default)
    {
        var c = await db.Coaches.SingleAsync(x => x.Id == coachId, ct);
        var k = await db.CoachContracts.SingleOrDefaultAsync(x => x.CoachId == coachId && x.Status == CoachContractStatus.Active, ct);
        if (k is null) return (0, 0, 0, "Chưa có hợp đồng.");
        if (k.Launched) return (0, 0, 0, "Đã họp báo ra mắt.");
        if (answers.Length != CoachRules.Launch.Length || answers.Where((a, i) => a < 0 || a >= CoachRules.Launch[i].Answers.Length).Any()) return (0, 0, 0, "Thiếu câu trả lời.");
        var media = CoachRules.MediaFactor(CoachRules.Effective(c, CoachSkill.Media), c.Trait);
        int fans = 0, board = 0, rep = 0;
        for (var i = 0; i < answers.Length; i++)
        {
            var (f, b, r) = CoachRules.LaunchEffect(CoachRules.Launch[i].Answers[answers[i]], media);
            fans += f; board += b; rep += r;
        }
        var tick = await CoachService.NowTickAsync(db, ct);
        await new CoachEffects(db, clock).ApplyAsync(c, new CoachFx(Rep: rep, Board: board, Fans: fans), k.ClubId, null, tick, "Họp báo ra mắt", ct);
        k.Launched = true;
        var club = await db.Clubs.Where(x => x.Id == k.ClubId).Select(x => x.Name).SingleAsync(ct);
        CoachLetterService.Write(db, c, LetterKind.Info, LetterFrom.Press, "Báo Bóng Đá", $"{c.Name} ra mắt {club}",
            $"Trong buổi họp báo ra mắt, tân HLV {c.Name} trả lời ba câu hỏi của báo giới. " +
            (fans > 0 ? "Cổ động viên hào hứng với tuyên bố của ông. " : fans < 0 ? "Một bộ phận CĐV chưa hài lòng. " : "") +
            (board > 0 ? "Ban lãnh đạo đánh giá cao sự chín chắn." : board < 0 ? "Ban lãnh đạo e ngại những đòi hỏi của ông." : ""), tick, clock.GetUtcNow(), k.ClubId, key: "ra-mat");
        await db.SaveChangesAsync(ct);
        return (fans, board, rep, null);
    }

    // ---------- rời CLB ----------

    /// <summary>Đơn từ chức (hồi âm vòng sau): nửa khoản bồi thường, danh tiếng −3, về thất nghiệp. Không lưu.</summary>
    internal async Task<(string Title, string Text)> ResignAsync(Coach c, CoachContract k, int tick, CancellationToken ct)
    {
        var pay = CoachRules.Severance(k) / 2;
        await LeaveAsync(c, k, CoachContractStatus.Resigned, pay, CoachRules.ResignRep, tick, ct);
        return ("Chấp thuận", $"Ban lãnh đạo chấp nhận đơn từ chức của ông. Theo thoả thuận, CLB trả {MoneyText.Format(pay)} vào ví HLV. Chúc ông thành công ở chặng đường mới.");
    }

    /// <summary>Sa thải: bồi thường theo hợp đồng, danh tiếng −8. Không lưu (trừ bước trả CLB về BOT).</summary>
    internal async Task<long> SackAsync(Coach c, CoachContract k, int tick, CancellationToken ct)
    {
        var pay = CoachRules.Severance(k);
        await LeaveAsync(c, k, CoachContractStatus.Sacked, pay, CoachRules.SackedRep, tick, ct);
        return pay;
    }

    internal async Task LeaveAsync(Coach c, CoachContract k, CoachContractStatus status, long pay, int rep, int tick, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        k.Status = status;
        k.EndedTick = tick;
        CoachService.Money(db, c, pay, CoachMoney.Severance, status == CoachContractStatus.Sacked ? "Bồi thường sa thải" : "Thanh lý hợp đồng", tick, now);
        c.Reputation = Math.Clamp(c.Reputation + rep, 0, 100);
        c.Status = CoachStatus.Unemployed;
        c.UpdatedAt = now;
        await new ClubAdminService(db, clock, store).SetHumanClubAsync(null, "career", c.ManagerId, ct);
    }
}
