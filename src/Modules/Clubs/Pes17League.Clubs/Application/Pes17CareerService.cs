using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Infrastructure;

namespace Pes17League.Clubs.Application;

/// <summary>CLB chọn được khi bắt đầu sự nghiệp: giải quốc nội, OVR, giá trị đội hình, ngân sách đầu mùa dự kiến, cúp mùa này.</summary>
public sealed record CareerClubChoice(Guid ClubId, string Name, string? LogoPath, string League, string Country, int Tier, double Ovr, long SquadValue,
    long Budget, IReadOnlyList<string> Cups);

public sealed record CareerStartResult(string Season, string Club, int Slots, int Matches, long Fund, long Budget);

/// <summary>
/// Career PES17 (PO 19/11/2026: "chế độ tự chọn, gói gọn thành game"): mùa đã dựng thế giới (<see cref="Pes17WorldService"/>) nhưng chưa
/// bắt đầu → người chơi chọn CLB → một bước: kinh tế đầu mùa, ghi lịch, bắt đầu mùa, mở chợ hè, giao CLB cho người chơi.
/// Kinh tế: quỹ ngân sách = 20% giá trị đội hình các CLB dự mùa, k = 0,5, lương 2‰/khung (PO 19/11: mùa 69 khung, 4‰ quá nặng),
/// trần lương mềm = quỹ lương/khung của CLB đứng thứ 2.
/// </summary>
public sealed class Pes17CareerService(ClubsDbContext db, IOptionFileStore store, TimeProvider clock)
{
    public const int WagePermille = 2;
    public const double FundShare = 0.20, BudgetExponent = 0.5;

    /// <summary>Mùa chờ bắt đầu sự nghiệp: mùa PES17 chưa xong sớm nhất, đang Planned, đã chia CLB vào giải; không thì null.</summary>
    public async Task<Season?> PendingSeasonAsync(CancellationToken ct = default)
    {
        var s = await db.Seasons.Where(x => x.Status != SeasonStatus.Finished).OrderBy(x => x.Number).FirstOrDefaultAsync(ct);
        if (s is not { Status: SeasonStatus.Planned } || !await new Pes17RoundService(db, store, clock).IsPes17SeasonAsync(s.Id, ct)) return null;
        return await db.Editions.AnyAsync(e => e.SeasonId == s.Id, ct) ? s : null;
    }

    /// <summary>Đã có HLV người cầm CLB → sự nghiệp đã có chủ (chế độ riêng: 1 người).</summary>
    public Task<bool> HasCoachAsync(CancellationToken ct = default) =>
        db.TeamAssignments.AnyAsync(a => a.To == null && a.ControllerType == ControllerType.Human && a.ParticipantId != null, ct);

    public async Task<SeasonEconomyChange> EconomyAsync(Guid seasonId, CancellationToken ct = default)
    {
        var econ = new EconomyAdminService(db, clock);
        var pv = await econ.PreviewAsync(seasonId, new(WagePermille: WagePermille, BudgetExponent: BudgetExponent), ct);
        var fund = (long)Math.Round(pv.Clubs.Sum(c => c.SquadValue) * FundShare / 1e6) * 1_000_000;
        var pays = pv.Clubs.Select(c => c.Payroll).OrderByDescending(x => x).ToList();
        var soft = pays.Count > 1 ? pays[1] / 1000 * 1000 : 0;
        return new SeasonEconomyChange(SoftWageCap: soft, WagePermille: WagePermille, InitialBudgetFund: fund, BudgetExponent: BudgetExponent);
    }

    /// <summary>Mọi CLB dự giải quốc nội của mùa chờ bắt đầu, mạnh trước.</summary>
    public async Task<IReadOnlyList<CareerClubChoice>> ChoicesAsync(Guid seasonId, CancellationToken ct = default)
    {
        var season = await db.Seasons.SingleAsync(s => s.Id == seasonId, ct);
        var rows = await (from x in db.EditionClubs
                          join e in db.Editions on x.EditionId equals e.Id
                          join c in db.Competitions on e.CompetitionId equals c.Id
                          where e.SeasonId == seasonId
                          select new { x.ClubId, c.Name, c.Type, c.Country, c.Tier }).ToListAsync(ct);
        var leagues = rows.Where(r => r.Type == CompetitionType.DomesticLeague).GroupBy(r => r.ClubId).ToDictionary(g => g.Key, g => g.First());
        var cups = rows.Where(r => r.Type != CompetitionType.DomesticLeague).GroupBy(r => r.ClubId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)[.. g.OrderByDescending(r => r.Type).Select(r => r.Name)]);
        var ovr = (await new DivisionService(db, clock).StrengthsAsync(seasonId, season.PatchVersionId!.Value, ct)).ToDictionary(c => c.ClubId, c => c.Ovr);
        var econ = await new EconomyAdminService(db, clock).PreviewAsync(seasonId, await EconomyAsync(seasonId, ct), ct);
        var money = econ.Clubs.ToDictionary(c => c.ClubId);
        var clubs = await db.Clubs.Where(c => leagues.Keys.Contains(c.Id)).Select(c => new { c.Id, c.Name, c.LogoPath }).ToListAsync(ct);
        return [.. clubs.Select(c =>
            {
                var l = leagues[c.Id];
                var m = money.GetValueOrDefault(c.Id);
                return new CareerClubChoice(c.Id, c.Name, c.LogoPath, l.Name, l.Country!, l.Tier ?? 1, Math.Round(ovr.GetValueOrDefault(c.Id), 1),
                    m?.SquadValue ?? 0, m?.After ?? 0, cups.GetValueOrDefault(c.Id, []));
            }).OrderByDescending(c => c.Ovr).ThenBy(c => c.Name, StringComparer.Ordinal)];
    }

    /// <summary>Bắt đầu sự nghiệp với CLB <paramref name="clubId"/> (null = chỉ bắt đầu mùa, mọi CLB BOT) cho tài khoản <paramref name="managerId"/>.</summary>
    public async Task<CareerStartResult> StartAsync(Guid? clubId, Guid? managerId, int seed, string actor, CancellationToken ct = default)
    {
        var season = await PendingSeasonAsync(ct) ?? throw new AdminRuleException("Không có mùa nào chờ bắt đầu sự nghiệp");
        if (clubId is not null && managerId is null) throw new AdminRuleException("Cần tài khoản người chơi để cầm CLB");
        if (clubId is not null && await HasCoachAsync(ct)) throw new AdminRuleException("Sự nghiệp đã có người cầm CLB");
        string? clubName = null;
        if (clubId is { } id)
        {
            var leagueClub = await (from x in db.EditionClubs join e in db.Editions on x.EditionId equals e.Id join c in db.Competitions on e.CompetitionId equals c.Id
                                    where e.SeasonId == season.Id && x.ClubId == id && c.Type == CompetitionType.DomesticLeague select x.ClubId).AnyAsync(ct);
            if (!leagueClub) throw new AdminRuleException("CLB không đá giải quốc nội mùa này");
            clubName = await db.Clubs.Where(c => c.Id == id).Select(c => c.Name).SingleAsync(ct);
        }

        var change = await EconomyAsync(season.Id, ct);
        await new EconomyAdminService(db, clock).SetAsync(season.Id, change, actor, ct);
        var schedule = await new SeasonScheduleService(db, clock).ApplyAsync(season.Id, seed, actor, ct);
        var start = await new CompetitionService(db, store, clock).StartScheduledSeasonAsync(season.Id, actor, ct);
        if (start.Error is { } err) throw new AdminRuleException($"Bắt đầu mùa lỗi: {err}");
        await new FreeMarketService(db, clock).OpenAsync(season.Id, actor, ct);
        if (clubId is { } human) await new ClubAdminService(db, clock, store).SetHumanClubAsync(human, actor, managerId, ct);
        var budget = clubId is { } b ? await db.Ledger.Where(e => e.ClubId == b).SumAsync(e => e.Amount, ct) : 0;
        return new CareerStartResult(season.Name, clubName ?? "(BOT)", schedule.Slots.Count, schedule.Slots.Sum(s => s.Matches.Count),
            change.InitialBudgetFund ?? 0, budget);
    }

    /// <summary>
    /// "Tiếp tục" (PO 19/11: trận không có PO thì mô phỏng): mô phỏng lần lượt từng khung đang mở (chốt khung → khung sau tự mở) tới khi gặp
    /// trận của người chơi chưa đá (dừng, trả trận đó), hết mùa, hoặc khung không tiến được (trận đang đá dở / lỗi ghi kết quả).
    /// <paramref name="includeMine"/> = mô phỏng luôn trận của người chơi (người chơi chọn "mô phỏng trận của tôi" / chạy thử cả mùa).
    /// </summary>
    public async Task<CareerContinueResult> ContinueAsync(int maxSlots = 500, Action<string>? progress = null, bool includeMine = false, CancellationToken ct = default)
    {
        var sim = new MatchSimService(db, store, clock);
        var simulated = 0;
        var slots = new List<int>();
        var errors = new List<string>();
        for (var i = 0; i < maxSlots; i++)
        {
            var season = await db.Seasons.AsNoTracking().Where(s => s.Status == SeasonStatus.Active).OrderBy(s => s.Number).FirstOrDefaultAsync(ct);
            if (season is null) return new(simulated, slots, null, true, errors);
            var md = await OpenMatchdayAsync(season.Id, ct);
            if (md is null) return new(simulated, slots, null, true, errors);
            var r = await sim.SimulateMatchdayAsync(md.Id, ct: ct);
            simulated += r.Simulated;
            errors.AddRange(r.Errors);
            if (r.Simulated > 0) slots.Add(md.Number);
            if (r.Simulated > 0) progress?.Invoke($"Khung {md.Number}: mô phỏng {r.Simulated} trận{(r.Settled ? ", đã chốt khung" : "")}");
            if (r.SeasonFinished) return new(simulated, slots, null, true, errors);
            var mine = await MyPendingFixtureAsync(md.Id, ct);
            if (mine is { } myFixture && includeMine)
            {
                var m = await sim.SimulateMatchdayAsync(md.Id, myFixture, ct);
                simulated += m.Simulated;
                errors.AddRange(m.Errors);
                if (m.Simulated > 0) progress?.Invoke($"Khung {md.Number}: trận của bạn {await ScoreLineAsync(myFixture, ct)}{(m.Settled ? ", đã chốt khung" : "")}");
                if (m.SeasonFinished) return new(simulated, slots, null, true, errors);
                if (m.Simulated == 0) return new(simulated, slots, myFixture, false, errors);
                continue;
            }
            if (mine is not null) return new(simulated, slots, mine, false, errors);
            if (!r.Settled) return new(simulated, slots, null, false, [.. errors, $"Khung {md.Number} chưa chốt được (còn trận đang đá dở hoặc lỗi ghi kết quả)"]);
        }
        return new(simulated, slots, null, false, errors);
    }

    /// <summary>"Chủ 2–1 Khách" (kèm luân lưu nếu có).</summary>
    public async Task<string> ScoreLineAsync(Guid fixtureId, CancellationToken ct = default)
    {
        var x = await (from f in db.Fixtures.AsNoTracking() join h in db.Clubs on f.HomeClubId equals h.Id join a in db.Clubs on f.AwayClubId equals a.Id
                       where f.Id == fixtureId select new { Home = h.Name, Away = a.Name, f.HomeGoals, f.AwayGoals, f.HomePenalties, f.AwayPenalties }).SingleAsync(ct);
        var pens = x.HomePenalties is { } hp && x.AwayPenalties is { } ap ? $" (pen {hp}–{ap})" : "";
        return x.HomeGoals is { } hg && x.AwayGoals is { } ag ? $"{x.Home} {hg}–{ag} {x.Away}{pens}" : $"{x.Home} – {x.Away}";
    }

    /// <summary>Khung đang mở sớm nhất của mùa (đá được: WindowOpen / Locked / Playing).</summary>
    public Task<Matchday?> OpenMatchdayAsync(Guid seasonId, CancellationToken ct = default) =>
        db.Matchdays.AsNoTracking().Where(m => m.SeasonId == seasonId
                && (m.Status == MatchdayStatus.WindowOpen || m.Status == MatchdayStatus.Locked || m.Status == MatchdayStatus.Playing))
            .OrderBy(m => m.Number).FirstOrDefaultAsync(ct);

    /// <summary>Trận chưa đá của CLB người chơi trong khung (chưa giao trợ lý).</summary>
    public async Task<Guid?> MyPendingFixtureAsync(Guid matchdayId, CancellationToken ct = default)
    {
        var fixtures = await db.Fixtures.AsNoTracking().Where(f => f.MatchdayId == matchdayId && f.Status != FixtureStatus.Played).ToListAsync(ct);
        var humans = await MatchOrchestrator.HumanClubsAsync(db, fixtures.SelectMany(f => new[] { f.HomeClubId, f.AwayClubId }), ct);
        return fixtures.FirstOrDefault(f => MatchOrchestrator.NeedsHuman(f, humans))?.Id;
    }
}

/// <param name="Slots">Số khung đã mô phỏng (có ít nhất 1 trận).</param>
/// <param name="MyFixtureId">Dừng ở trận này của người chơi (chưa đá); null = không còn trận chờ trong khung đang mở.</param>
public sealed record CareerContinueResult(int Simulated, IReadOnlyList<int> Slots, Guid? MyFixtureId, bool SeasonFinished, IReadOnlyList<string> Errors);
