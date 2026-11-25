using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Infrastructure;
using Pes17League.OptionFile;

namespace Pes17League.Clubs.Application;

public enum CompetitionError
{
    SeasonNotFound,
    SeasonNotPlanned,
    SeasonNotActive,
    OddNumberOfClubs,
    ClubNotInSeason,
    MatchdayNotFound,
    WrongMatchdayStatus,
    FixtureNotFound,
    WrongFixtureStatus,
    AttemptNotFound,
    GoalsMismatch,
    OptionFileInvalid,
    PenaltiesInvalid,
    /// <summary>Mùa liên đoàn chưa ghi lịch (hoặc còn mùa giải chưa Scheduled).</summary>
    SeasonNotScheduled,
    /// <summary>Đang có mùa khác Active (web chạy 1 mùa một lúc).</summary>
    AnotherSeasonActive,
}

public sealed record SeasonStartResult(CompetitionError? Error, int Matchdays = 0, IReadOnlyDictionary<Guid, long>? Budgets = null)
{
    public bool Ok => Error is null;
}

public sealed record CloseWindowResult(CompetitionError? Error, Guid? OptionFileBuildId = null, IReadOnlyList<ValidationIssue>? Issues = null)
{
    public bool Ok => Error is null;
}

public sealed record StartFixtureResult(CompetitionError? Error, Guid? AttemptId = null)
{
    public bool Ok => Error is null;
}

/// <summary>Bàn thắng báo về từ bộ đọc kết quả (hoặc admin).</summary>
public sealed record GoalInput(Guid ClubId, int Half, int Minute, double? VideoOffsetSec = null, Guid? ScorerPlayerId = null);
public sealed record RatingInput(Guid ClubId, Guid PlayerId, int Slot, decimal? Rating);
/// <summary>Thống kê trận đã giải mã (B3): Teams/Events chưa gán FixtureId (gán khi ghi); Players gộp vào dòng MatchRating cùng cầu thủ
/// (chưa có điểm → thêm dòng Rating null).</summary>
public sealed record MatchStatsInput(IReadOnlyList<MatchTeamStats> Teams, IReadOnlyList<MatchEvent> Events, IReadOnlyList<PlayerStatsInput> Players);
public sealed record PlayerStatsInput(Guid ClubId, Guid PlayerId, int Slot, int Goals, int Shots, int ShotsOnTarget, int Fouls, int Offsides, int Corners,
    int? InMinute, int? OutMinute);

/// <param name="Settlement">Khác null khi trận này là trận cuối của vòng → đã tự chốt vòng.</param>
public sealed record RecordResult(CompetitionError? Error, RoundSettlementResult? Settlement = null, bool SeasonFinished = false)
{
    public bool Ok => Error is null;
}

/// <summary>
/// M2.2 mùa/lịch/chốt vòng (docs/domain-competition-design.md, PO duyệt 01/11). Bản alpha solo:
/// PO bấm "Đóng chuyển nhượng" (<see cref="CloseTransferWindowAsync"/>) → sinh OF vòng → điều phối chạy trận
/// (<see cref="StartFixtureAsync"/> / <see cref="EndAttemptAsync"/> / <see cref="RecordResultAsync"/>);
/// trận cuối của vòng có kết quả → tự chốt vòng (lương, thuế, hết hợp đồng, phong độ theo kết quả đội) + mở kỳ CN vòng sau.
/// Mỗi lệnh chạy trong 1 transaction DB.
/// </summary>
/// <param name="rng">Bốc chủ nhà chung kết cúp khi tự sinh vòng loại trực tiếp (<see cref="SeasonScheduleService.AdvanceAsync"/>).</param>
public sealed class CompetitionService(ClubsDbContext db, IOptionFileStore store, TimeProvider clock,
    SquadPolicyOptions? squadOptions = null, PriceModelOptions? priceOptions = null, Random? rng = null)
{
    private readonly SquadPolicyOptions _squad = squadOptions ?? new();

    public async Task<Season> CreateSeasonAsync(string name, int number, CancellationToken ct = default)
    {
        var s = new Season { Name = name, Number = number, CreatedAt = clock.GetUtcNow() };
        db.Seasons.Add(s);
        await db.SaveChangesAsync(ct);
        return s;
    }

    /// <summary>
    /// Bắt đầu mùa sau khi đã nhập patch với cùng seasonId: CLB tham gia = CLB của patch; lịch 2 lượt; giá → lương hợp đồng nhập → ngân sách
    /// (tham số SeasonEconomy đặt trước); CLB của PO chuyển sang Human; mở kỳ CN vòng 1.
    /// </summary>
    public Task<SeasonStartResult> StartSeasonAsync(Guid seasonId, Guid patchVersionId, Guid? humanClubId = null, CancellationToken ct = default) =>
        InTransactionAsync(async () =>
        {
            var season = await db.Seasons.FindAsync([seasonId], ct);
            if (season is null) return new SeasonStartResult(CompetitionError.SeasonNotFound);
            if (season.Status != SeasonStatus.Planned) return new(CompetitionError.SeasonNotPlanned);
            var clubs = await (from m in db.ClubPesMappings
                               join c in db.Clubs on m.ClubId equals c.Id
                               where m.PatchVersionId == patchVersionId && c.Status == ClubStatus.Active
                               orderby m.PesClubIndex
                               select c.Id).ToListAsync(ct);
            if (clubs.Count < 2 || clubs.Count % 2 != 0) return new(CompetitionError.OddNumberOfClubs);
            if (humanClubId is { } h && !clubs.Contains(h)) return new(CompetitionError.ClubNotInSeason);

            var now = clock.GetUtcNow();
            season.PatchVersionId = patchVersionId;
            season.Status = SeasonStatus.Active;
            db.SeasonClubs.AddRange(clubs.Select(c => new SeasonClub { SeasonId = seasonId, ClubId = c }));
            if (humanClubId is { } human)
            {
                foreach (var a in await db.TeamAssignments.Where(a => a.ClubId == human && a.To == null).ToListAsync(ct)) a.To = now;
                db.TeamAssignments.Add(new TeamAssignment { ClubId = human, ControllerType = ControllerType.Human, From = now });
            }
            var rounds = RoundRobin.DoubleRoundRobin(clubs);
            Matchday? first = null;
            for (var i = 0; i < rounds.Count; i++)
            {
                var md = new Matchday { SeasonId = seasonId, Number = i + 1 };
                first ??= md;
                db.Matchdays.Add(md);
                db.Fixtures.AddRange(rounds[i].Select(f => new Fixture { MatchdayId = md.Id, HomeClubId = f.Home, AwayClubId = f.Away }));
            }
            OpenWindow(seasonId, first!, now, await IsPes17PatchAsync(patchVersionId, ct));
            await db.SaveChangesAsync(ct);

            var eco = new EconomyService(db, clock, priceOptions, squadOptions);
            await eco.RecalculatePricesAsync(seasonId, ct);
            await eco.SetImportedWagesAsync(seasonId, ct);
            var budgets = await eco.AllocateInitialBudgetsAsync(seasonId, ct);
            return new SeasonStartResult(null, rounds.Count, budgets);
        }, r => r.Ok, ct);

    /// <summary>
    /// "Bắt đầu mùa" liên đoàn (PO chọn 06/11, bước 6) sau "Ghi lịch mùa" (<see cref="SeasonScheduleService.ApplyAsync"/>): mọi mùa giải có CLB
    /// đã Scheduled → Active; CLB tham gia mùa = CLB đang hoạt động của patch (mọi CLB nhận lương, mua bán) ∪ CLB các giải;
    /// giá → lương hợp đồng nhập → ngân sách như <see cref="StartSeasonAsync"/>; mở kỳ CN khung ngày 1.
    /// </summary>
    public Task<SeasonStartResult> StartScheduledSeasonAsync(Guid seasonId, string actor, CancellationToken ct = default) =>
        InTransactionAsync(async () =>
        {
            var season = await db.Seasons.FindAsync([seasonId], ct);
            if (season is null) return new SeasonStartResult(CompetitionError.SeasonNotFound);
            if (season.Status != SeasonStatus.Planned) return new(CompetitionError.SeasonNotPlanned);
            var matchdays = await db.Matchdays.Where(m => m.SeasonId == seasonId).OrderBy(m => m.Number).ToListAsync(ct);
            var editions = await db.Editions.Where(e => e.SeasonId == seasonId && db.EditionClubs.Any(x => x.EditionId == e.Id)).ToListAsync(ct);
            if (season.PatchVersionId is not { } patch || matchdays.Count == 0 || editions.Count == 0 || editions.Any(e => e.Status != EditionStatus.Scheduled))
                return new(CompetitionError.SeasonNotScheduled);
            if (await db.Seasons.AnyAsync(s => s.Id != seasonId && s.Status == SeasonStatus.Active, ct)) return new(CompetitionError.AnotherSeasonActive);

            var ids = editions.Select(e => e.Id).ToList();
            var clubs = (await (from m in db.ClubPesMappings join c in db.Clubs on m.ClubId equals c.Id
                                where m.PatchVersionId == patch && c.Status == ClubStatus.Active select c.Id).ToListAsync(ct))
                .Union(await db.EditionClubs.Where(x => ids.Contains(x.EditionId)).Select(x => x.ClubId).ToListAsync(ct)).ToList();
            var now = clock.GetUtcNow();
            season.Status = SeasonStatus.Active;
            db.SeasonClubs.AddRange(clubs.Select(c => new SeasonClub { SeasonId = seasonId, ClubId = c }));
            foreach (var e in editions) e.Status = EditionStatus.Active;
            OpenWindow(seasonId, matchdays[0], now, await IsPes17PatchAsync(patch, ct));
            db.AdminAudit.Add(new AdminAuditEntry
            {
                At = now, Actor = actor, EntityType = nameof(Season), EntityId = seasonId, Action = "StartSeason",
                After = JsonSerializer.Serialize(new { Clubs = clubs.Count, Editions = editions.Count, Matchdays = matchdays.Count }),
            });
            await db.SaveChangesAsync(ct);

            var eco = new EconomyService(db, clock, priceOptions, squadOptions);
            await eco.RecalculatePricesAsync(seasonId, ct);
            await eco.SetImportedWagesAsync(seasonId, ct);
            var budgets = await eco.AllocateInitialBudgetsAsync(seasonId, ct);
            return new SeasonStartResult(null, matchdays.Count, budgets);
        }, r => r.Ok, ct);

    /// <summary>Đóng kỳ CN và sinh OF vòng. OF không hợp lệ → giữ kỳ CN mở, trả lỗi để admin/PO sửa squad.</summary>
    public Task<CloseWindowResult> CloseTransferWindowAsync(Guid matchdayId, CancellationToken ct = default) =>
        InTransactionAsync(async () =>
        {
            var md = await LockMatchdayAsync(matchdayId, ct);
            if (md is null) return new CloseWindowResult(CompetitionError.MatchdayNotFound);
            if (md.Status != MatchdayStatus.WindowOpen) return new(CompetitionError.WrongMatchdayStatus);
            var season = (await db.Seasons.FindAsync([md.SeasonId], ct))!;
            // HLV BOT v1: xếp sơ đồ/XI/thiết lập cho CLB BOT trước khi sinh OF (hlv-bot-v1.md).
            await new BotCoachService(db, clock).ApplyAsync(md.Id, ct);
            if (await IsPes17PatchAsync(season.PatchVersionId!.Value, ct))
            {   // PES17: không khoá chuyển nhượng, không dựng OF — chụp đội hình (game nhận qua tools/pes17db/sync.sh)
                var snap = await new Pes17RoundService(db, store, clock).SnapshotAsync(season.PatchVersionId!.Value, md.Id, ct);
                md.Status = MatchdayStatus.Locked;
                md.OptionFileBuildId = snap.Id;
                await db.SaveChangesAsync(ct);
                return new(null, snap.Id);
            }
            var build = await new RoundOptionFileService(db, store, clock,
                new SquadRulesOptions { MinSquadSize = _squad.MinSquadSize, MinGoalkeepers = _squad.MinGoalkeepers })
                .BuildAsync(season.PatchVersionId!.Value, md.Id, ct);
            if (!build.Ok) return new(CompetitionError.OptionFileInvalid, build.BuildId, build.Issues);   // bản Failed vẫn được lưu
            if (md.TransferWindowId is { } wid && await db.TransferWindows.FindAsync([wid], ct) is { } w)
            {
                w.Status = TransferWindowStatus.Closed;
                w.ClosesAt = clock.GetUtcNow();
            }
            md.Status = MatchdayStatus.Locked;
            md.OptionFileBuildId = build.BuildId;
            await db.SaveChangesAsync(ct);
            return new(null, build.BuildId);
        }, _ => true, ct);

    /// <summary>Bắt đầu 1 lần chạy container cho trận (bước điều phối gọi).</summary>
    public async Task<StartFixtureResult> StartFixtureAsync(Guid fixtureId, string? containerName, CancellationToken ct = default)
    {
        var (f, md) = await LoadFixtureAsync(fixtureId, ct);
        if (f is null) return new(CompetitionError.FixtureNotFound);
        if (md!.Status is not (MatchdayStatus.WindowOpen or MatchdayStatus.Locked or MatchdayStatus.Playing)) return new(CompetitionError.WrongMatchdayStatus);   // PO 17/11: bỏ khoá vòng
        if (f.Status != FixtureStatus.Scheduled) return new(CompetitionError.WrongFixtureStatus);
        var attempt = new MatchAttempt { FixtureId = f.Id, ContainerName = containerName, StartedAt = clock.GetUtcNow() };
        db.MatchAttempts.Add(attempt);
        f.Status = FixtureStatus.Playing;
        md.Status = MatchdayStatus.Playing;
        await db.SaveChangesAsync(ct);
        return new(null, attempt.Id);
    }

    /// <summary>Lần chạy hỏng (crash/huỷ) → trận về Scheduled để chạy lại.</summary>
    public async Task<CompetitionError?> EndAttemptAsync(Guid attemptId, MatchAttemptOutcome outcome, string? recordingPath = null, CancellationToken ct = default)
    {
        if (outcome is not (MatchAttemptOutcome.Crashed or MatchAttemptOutcome.Aborted)) throw new ArgumentOutOfRangeException(nameof(outcome));
        var a = await db.MatchAttempts.FindAsync([attemptId], ct);
        if (a is not { Outcome: MatchAttemptOutcome.Running }) return CompetitionError.AttemptNotFound;
        a.Outcome = outcome;
        a.EndedAt = clock.GetUtcNow();
        a.RecordingPath = recordingPath;
        var f = (await db.Fixtures.FindAsync([a.FixtureId], ct))!;
        f.Status = FixtureStatus.Scheduled;
        await db.SaveChangesAsync(ct);
        return null;
    }

    /// <summary>
    /// Ghi kết quả. Nguồn Memory phải kèm lần chạy đang Running và danh sách bàn thắng khớp tỷ số; Admin nhập tay được kể cả khi chưa chạy,
    /// bàn thắng tuỳ chọn. Trận cuối của vòng → tự chốt vòng.
    /// <paramref name="penalties"/>: loạt penalty, chỉ chung kết hoà sau 120' (<see cref="Fixture.PenaltiesValid"/>).
    /// </summary>
    public Task<RecordResult> RecordResultAsync(Guid fixtureId, int homeGoals, int awayGoals, IReadOnlyList<GoalInput> goals,
        ResultSource source, Guid? attemptId = null, string? recordingPath = null, CancellationToken ct = default,
        IReadOnlyList<RatingInput>? ratings = null, (int Home, int Away)? penalties = null, MatchStatsInput? stats = null) =>
        InTransactionAsync(async () =>
        {
            ArgumentOutOfRangeException.ThrowIfNegative(homeGoals);
            ArgumentOutOfRangeException.ThrowIfNegative(awayGoals);
            var (f, md) = await LoadFixtureAsync(fixtureId, ct, lockMatchday: true);
            if (f is null) return new RecordResult(CompetitionError.FixtureNotFound);
            if (md!.Status is not (MatchdayStatus.WindowOpen or MatchdayStatus.Locked or MatchdayStatus.Playing)) return new(CompetitionError.WrongMatchdayStatus);   // PO 17/11: bỏ khoá vòng
            if (f.Status == FixtureStatus.Played) return new(CompetitionError.WrongFixtureStatus);
            // Penalty cho trận knockout 1 lượt (chung kết châu lục / mọi vòng cúp quốc gia) khi hoà.
            if (penalties is { } p && (f.Leg is not null || f.Stage <= FixtureStage.LeaguePhase || !Fixture.PenaltiesValid(homeGoals, awayGoals, p.Home, p.Away)))
                return new(CompetitionError.PenaltiesInvalid);
            if (goals.Any(g => g.ClubId != f.HomeClubId && g.ClubId != f.AwayClubId)) return new(CompetitionError.GoalsMismatch);
            var goalsMatch = goals.Count(g => g.ClubId == f.HomeClubId) == homeGoals && goals.Count(g => g.ClubId == f.AwayClubId) == awayGoals;
            if (!goalsMatch && (source == ResultSource.Memory || goals.Count > 0)) return new(CompetitionError.GoalsMismatch);

            var now = clock.GetUtcNow();
            if (source == ResultSource.Memory)
            {
                var a = attemptId is { } aid ? await db.MatchAttempts.FindAsync([aid], ct) : null;
                if (a is not { Outcome: MatchAttemptOutcome.Running } || a.FixtureId != f.Id) return new(CompetitionError.AttemptNotFound);
                a.Outcome = MatchAttemptOutcome.Finished;
                a.EndedAt = now;
                a.RecordingPath = recordingPath;
            }
            else
            {
                foreach (var a in await db.MatchAttempts.Where(x => x.FixtureId == f.Id && x.Outcome == MatchAttemptOutcome.Running).ToListAsync(ct))
                {
                    a.Outcome = MatchAttemptOutcome.Aborted;
                    a.EndedAt = now;
                }
            }
            f.HomeGoals = homeGoals;
            f.AwayGoals = awayGoals;
            (f.HomePenalties, f.AwayPenalties) = (penalties?.Home, penalties?.Away);
            f.ResultSource = source;
            f.PlayedAt = now;
            f.Status = FixtureStatus.Played;
            db.Goals.AddRange(goals.Select(g => new Goal { FixtureId = f.Id, ClubId = g.ClubId, Half = g.Half, Minute = g.Minute, VideoOffsetSec = g.VideoOffsetSec, ScorerPlayerId = g.ScorerPlayerId }));
            var rows = (ratings ?? []).Where(r => r.ClubId == f.HomeClubId || r.ClubId == f.AwayClubId).DistinctBy(r => r.PlayerId)
                .Select(r => new MatchRating { FixtureId = f.Id, ClubId = r.ClubId, PlayerId = r.PlayerId, Slot = r.Slot, Rating = r.Rating }).ToList();
            if (stats is not null) AddStats(f, stats, rows);
            db.MatchRatings.AddRange(rows);
            await new AbsenceService(db, clock).ResetAsync(f, ct);   // có mặt (tự đá / giao trợ lý / Sẵn sàng) → chuỗi vắng về 0
            md.Status = MatchdayStatus.Playing;
            await db.SaveChangesAsync(ct);

            if (await db.Fixtures.AnyAsync(x => x.MatchdayId == md.Id && x.Status != FixtureStatus.Played, ct)) return new(null);
            md.Status = MatchdayStatus.Completed;
            var (settlement, finished) = await SettleAsync(md, ct);
            return new(null, settlement, finished);
        }, r => r.Ok, ct);

    /// <summary>Thống kê trận (B3) cùng transaction với kết quả; dòng không thuộc 2 đội của trận bị bỏ.</summary>
    private void AddStats(Fixture f, MatchStatsInput stats, List<MatchRating> rows)
    {
        bool Ours(Guid club) => club == f.HomeClubId || club == f.AwayClubId;
        foreach (var t in stats.Teams.Where(t => Ours(t.ClubId)).DistinctBy(t => t.ClubId))
        {
            t.FixtureId = f.Id;
            t.IsHome = t.ClubId == f.HomeClubId;
            db.MatchTeamStats.Add(t);
        }
        foreach (var e in stats.Events.Where(e => Ours(e.ClubId)))
        {
            e.FixtureId = f.Id;
            db.MatchEvents.Add(e);
        }
        foreach (var p in stats.Players.Where(p => Ours(p.ClubId)).DistinctBy(p => p.PlayerId))
        {
            var r = rows.FirstOrDefault(r => r.PlayerId == p.PlayerId);
            if (r is null) rows.Add(r = new MatchRating { FixtureId = f.Id, ClubId = p.ClubId, PlayerId = p.PlayerId, Slot = p.Slot });
            (r.Goals, r.Shots, r.ShotsOnTarget, r.Fouls, r.Offsides, r.Corners) = (p.Goals, p.Shots, p.ShotsOnTarget, p.Fouls, p.Offsides, p.Corners);
            (r.InMinute, r.OutMinute) = (p.InMinute, p.OutMinute);
        }
    }

    /// <summary>Bảng xếp hạng mùa (giải đơn, bản alpha), tính từ các trận vòng tròn đã đá; không gồm trận cúp.</summary>
    public async Task<IReadOnlyList<StandingRow<Guid>>> GetStandingsAsync(Guid seasonId, CancellationToken ct = default)
    {
        var clubs = await (from sc in db.SeasonClubs join c in db.Clubs on sc.ClubId equals c.Id where sc.SeasonId == seasonId select c)
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var played = await (from f in db.Fixtures
                            join m in db.Matchdays on f.MatchdayId equals m.Id
                            where m.SeasonId == seasonId && f.Status == FixtureStatus.Played && f.Stage == FixtureStage.League
                            select new PlayedFixture<Guid>(f.HomeClubId, f.AwayClubId, f.HomeGoals!.Value, f.AwayGoals!.Value)).ToListAsync(ct);
        return Standings.Compute(clubs.Keys, played, id => clubs[id], SanctionService.For(await SanctionService.DeductionsAsync(db, ct), null));
    }

    /// <summary>BXH một mùa giải (PO chọn 06/11, bước 6): giải quốc nội tính trận vòng tròn, cúp tính league phase; CLB = CLB của mùa giải.</summary>
    public async Task<IReadOnlyList<StandingRow<Guid>>> GetEditionStandingsAsync(Guid editionId, CancellationToken ct = default) =>
        (await EditionStandingsAsync(db.Editions.Where(e => e.Id == editionId), ct)).Single().Value;

    /// <summary>BXH mọi mùa giải của mùa (bước 7: trang Các giải, cột phải trang chủ), cùng cách tính với <see cref="GetEditionStandingsAsync"/>.</summary>
    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<StandingRow<Guid>>>> GetSeasonEditionStandingsAsync(Guid seasonId, CancellationToken ct = default) =>
        await EditionStandingsAsync(db.Editions.Where(e => e.SeasonId == seasonId), ct);

    private async Task<Dictionary<Guid, IReadOnlyList<StandingRow<Guid>>>> EditionStandingsAsync(IQueryable<CompetitionEdition> editions, CancellationToken ct)
    {
        var types = await (from e in editions join c in db.Competitions on e.CompetitionId equals c.Id select new { e.Id, c.Type }).ToDictionaryAsync(x => x.Id, x => x.Type, ct);
        var ids = types.Keys.ToList();
        var clubs = (await (from x in db.EditionClubs join c in db.Clubs on x.ClubId equals c.Id where ids.Contains(x.EditionId) select new { x.EditionId, c.Id, c.Name })
            .ToListAsync(ct)).ToLookup(x => x.EditionId);
        var played = (await db.Fixtures.Where(f => f.CompetitionEditionId != null && ids.Contains(f.CompetitionEditionId.Value) && f.Status == FixtureStatus.Played
                && (f.Stage == FixtureStage.League || f.Stage == FixtureStage.LeaguePhase))
            .Select(f => new { Edition = f.CompetitionEditionId!.Value, f.Stage, f.HomeClubId, f.AwayClubId, Home = f.HomeGoals!.Value, Away = f.AwayGoals!.Value })
            .ToListAsync(ct)).ToLookup(f => f.Edition);
        var deducted = await SanctionService.DeductionsAsync(db, ct);   // trừ điểm kỷ luật (PO 17/11)
        return types.ToDictionary(t => t.Key, IReadOnlyList<StandingRow<Guid>> (t) =>
        {
            var stage = t.Value == CompetitionType.ContinentalCup ? FixtureStage.LeaguePhase : FixtureStage.League;
            var names = clubs[t.Key].ToDictionary(x => x.Id, x => x.Name);
            var own = played[t.Key].Where(f => f.Stage == stage).Select(f => new PlayedFixture<Guid>(f.HomeClubId, f.AwayClubId, f.Home, f.Away)).ToList();
            return Standings.Compute(names.Keys, own, id => names[id], SanctionService.For(deducted, t.Key));
        });
    }

    /// <summary>Sơ đồ nhánh của mùa giải cúp (châu lục: nhánh cố định; quốc gia: knockout 1 lượt); null với giải quốc nội.</summary>
    public async Task<IReadOnlyList<BracketRound>?> GetBracketAsync(Guid editionId, CancellationToken ct = default)
    {
        var e = await db.Editions.AsNoTracking().SingleAsync(x => x.Id == editionId, ct);
        var comp = await db.Competitions.AsNoTracking().SingleAsync(c => c.Id == e.CompetitionId, ct);
        var clubs = await (from x in db.EditionClubs join c in db.Clubs on x.ClubId equals c.Id where x.EditionId == editionId select c)
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var fixtures = await db.Fixtures.AsNoTracking().Where(f => f.CompetitionEditionId == editionId).ToListAsync(ct);
        if (comp.Type == CompetitionType.DomesticCup)
            return CupBracket.BuildSingleLeg(clubs.Count, fixtures);
        if (comp.Type != CompetitionType.ContinentalCup || (e.RulesSnapshot ?? comp.Rules) is not { DirectKnockoutPlaces: { } direct, PlayoffPlaces: { } playoff }) return null;
        return CupBracket.Build(clubs.Keys, id => clubs[id], fixtures, direct, playoff);
    }

    /// <summary>
    /// Chốt vòng: EconomyService.SettleRound → tiến hoá chỉ số + giá gốc theo điểm trận → áp phong độ theo điểm trận
    /// → vòng Settled → giải đá xong / sinh vòng loại trực tiếp (<see cref="SeasonScheduleService.AdvanceAsync"/>) → mở kỳ CN vòng sau (hoặc kết thúc mùa).
    /// </summary>
    private async Task<(RoundSettlementResult, bool Finished)> SettleAsync(Matchday md, CancellationToken ct)
    {
        // Phong độ + chỉ số theo điểm cá nhân trận (PO chọn 04/11); cầu thủ không có điểm → không đổi.
        var ratings = await (from r in db.MatchRatings join f in db.Fixtures on r.FixtureId equals f.Id
                             where f.MatchdayId == md.Id select r).ToListAsync(ct);
        var form = ratings.Where(r => r.Rating is not null).DistinctBy(r => r.PlayerId).ToDictionary(r => r.PlayerId, r => RatingForm.For(r.Rating!.Value));
        var eco = new EconomyService(db, clock, priceOptions, squadOptions);
        var settlement = await eco.SettleRoundAsync(md.SeasonId, md.Id, ct);
        await eco.EvolveAttributesAsync(md.SeasonId, ratings, ct);
        await new TrainingService(db, clock, priceOptions).ApplyAsync(md.SeasonId, md.Number, ct);   // FM đợt 4: mỗi 5 vòng
        await new FinanceService(db, clock).ApplyRoundAsync(md.SeasonId, md.Id, ct);   // FM đợt 6: vé, tài trợ, TV, thưởng, bảo trì
        await new ConditionService(db, clock).ApplyRoundAsync(md.Id, ct);   // FM đợt 5: thể lực, chấn thương, tinh thần
        await new BoardService(db, clock).ApplyRoundAsync(md.SeasonId, md.Id, ct);   // FM đợt 7: độ tin cậy ban lãnh đạo
        await new YouthService(db, clock).ApplyRoundAsync(md.Id, ct);   // FM đợt 8: báo cáo trinh sát
        await new YouthLeagueService(db, clock, rng).ApplyRoundAsync(md, ct);   // Giải U19 mô phỏng (PO 25/11)
        await eco.ApplyFormAsync(md.SeasonId, form, ct);

        md.Status = MatchdayStatus.Settled;
        await new SeasonScheduleService(db, clock, rng).AdvanceAsync(md.SeasonId, ct);
        var next = await db.Matchdays.SingleOrDefaultAsync(x => x.SeasonId == md.SeasonId && x.Number == md.Number + 1, ct);
        var pes17 = await new Pes17RoundService(db, store, clock).IsPes17SeasonAsync(md.SeasonId, ct);
        if (next is not null) OpenWindow(md.SeasonId, next, clock.GetUtcNow(), pes17);
        if (next is not null && pes17) await SyncPes17WindowAsync(md.SeasonId, next.Number, ct);
        else if (pes17) await SyncPes17WindowAsync(md.SeasonId, int.MaxValue, ct);   // hết mùa → đóng chợ
        if (next is null) (await db.Seasons.FindAsync([md.SeasonId], ct))!.Status = SeasonStatus.Finished;
        await new CoachRoundService(db, clock, store).ApplyRoundAsync(md, next is null, ct);   // HLV career (PO 19/11); giải online không có hồ sơ HLV → bỏ qua
        await db.SaveChangesAsync(ct);
        return (settlement, next is null);
    }

    /// <summary>Vòng mở là đá được ngay (PO 17/11 bỏ khoá vòng). Chuyển nhượng tự do cả mùa: 1 kỳ CN mở suốt mùa, vòng nào cũng gắn vào kỳ đó.
    /// PES17 (<paramref name="pes17"/>): chợ tự do chung cả mùa không gắn vòng (<see cref="FreeMarketService"/>) → không tạo kỳ CN ở đây.</summary>
    private void OpenWindow(Guid seasonId, Matchday md, DateTimeOffset now, bool pes17 = false)
    {
        md.Status = MatchdayStatus.WindowOpen;
        if (pes17) return;
        var w = db.TransferWindows.Local.FirstOrDefault(x => x.SeasonId == seasonId && x.Status == TransferWindowStatus.Open)
            ?? db.TransferWindows.FirstOrDefault(x => x.SeasonId == seasonId && x.Status == TransferWindowStatus.Open && x.ClosesAt > now);
        if (w is null)
        {
            w = new TransferWindow { SeasonId = seasonId, MatchdayId = md.Id, OpensAt = now, ClosesAt = now.AddYears(5), Status = TransferWindowStatus.Open };
            db.TransferWindows.Add(w);
        }
        md.TransferWindowId = w.Id;
    }

    /// <summary>
    /// PES17 2 kỳ chuyển nhượng (<see cref="Pes17TransferWindows"/>, PO 19/11): khung <paramref name="number"/> sắp đá nằm trong kỳ → mở chợ tự do
    /// (chỉ tạo kỳ CN, không tính lại giá/lương/ngân sách như lúc mở chợ đầu mùa); ngoài kỳ → đóng chợ đang mở.
    /// </summary>
    private async Task SyncPes17WindowAsync(Guid seasonId, int number, CancellationToken ct)
    {
        var total = await db.Matchdays.CountAsync(m => m.SeasonId == seasonId, ct);
        var now = clock.GetUtcNow();
        var open = await db.TransferWindows.Where(w => w.SeasonId == seasonId && w.MatchdayId == null && w.Status == TransferWindowStatus.Open).ToListAsync(ct);
        if (Pes17TransferWindows.IsOpen(number, total))
        {
            if (open.Count == 0)
            {
                db.TransferWindows.Add(new TransferWindow { SeasonId = seasonId, OpensAt = now, ClosesAt = now + FreeMarketService.Length, Status = TransferWindowStatus.Open });
                db.AdminAudit.Add(new AdminAuditEntry { At = now, Actor = SeasonScheduleService.SystemActor, EntityType = nameof(Season), EntityId = seasonId,
                    Action = "OpenTransferWindow", After = JsonSerializer.Serialize(new { Matchday = number }) });
            }
            return;
        }
        foreach (var w in open)
        {
            w.Status = TransferWindowStatus.Closed;
            w.ClosesAt = now;
            db.AdminAudit.Add(new AdminAuditEntry { At = now, Actor = SeasonScheduleService.SystemActor, EntityType = nameof(Season), EntityId = seasonId,
                Action = "CloseTransferWindow", After = JsonSerializer.Serialize(new { Matchday = number }) });
        }
    }

    /// <summary>Mùa đang đá chưa có kỳ CN mở (mùa bắt đầu trước 17/11, kỳ CN đã đóng khi khoá vòng) → mở 1 kỳ CN suốt mùa.
    /// PES17: chợ tự do (MatchdayId null) đã mở → trả kỳ đó; chợ đang đóng (ngoài 2 kỳ) → null, không tự mở.</summary>
    public static async Task<TransferWindow?> EnsureFreeTransfersAsync(ClubsDbContext db, Guid seasonId, DateTimeOffset now, CancellationToken ct = default)
    {
        var w = await db.TransferWindows.FirstOrDefaultAsync(x => x.SeasonId == seasonId && x.Status == TransferWindowStatus.Open && x.ClosesAt > now && x.OpensAt <= now, ct);
        if (w is not null) return w;
        if (!await db.Seasons.AnyAsync(x => x.Id == seasonId && x.Status == SeasonStatus.Active, ct)) return null;
        if (await (from s in db.Seasons join p in db.PatchVersions on s.PatchVersionId equals p.Id
                   where s.Id == seasonId && p.Name.StartsWith(Pes17RoundService.PatchPrefix) select p.Id).AnyAsync(ct)) return null;
        var md = await db.Matchdays.Where(m => m.SeasonId == seasonId && m.Status != MatchdayStatus.Settled).OrderBy(m => m.Number).FirstOrDefaultAsync(ct);
        if (md is null) return null;
        w = new TransferWindow { SeasonId = seasonId, MatchdayId = md.Id, OpensAt = now, ClosesAt = now.AddYears(5), Status = TransferWindowStatus.Open };
        db.TransferWindows.Add(w);
        await db.SaveChangesAsync(ct);
        return w;
    }

    /// <param name="lockMatchday">Khoá dòng vòng (FOR UPDATE) tới hết transaction: các trận cùng vòng ghi kết quả lần lượt
    /// → chỉ trận ghi sau cùng thấy đủ 4 trận và chốt vòng đúng 1 lần.</param>
    private async Task<(Fixture?, Matchday?)> LoadFixtureAsync(Guid fixtureId, CancellationToken ct, bool lockMatchday = false)
    {
        var f = await db.Fixtures.FindAsync([fixtureId], ct);
        if (f is null) return (null, null);
        if (!lockMatchday) return (f, await db.Matchdays.FindAsync([f.MatchdayId], ct));
        var md = await LockMatchdayAsync(f.MatchdayId, ct);
        await db.Entry(f).ReloadAsync(ct);
        return (f, md);
    }

    /// <summary>SELECT … FOR UPDATE rồi đọc lại (bỏ giá trị cũ trong change tracker). Chỉ dùng trong transaction.</summary>
    private async Task<Matchday?> LockMatchdayAsync(Guid matchdayId, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM clubs.\"Matchdays\" WHERE \"Id\" = {matchdayId} FOR UPDATE", ct);
        var md = await db.Matchdays.FindAsync([matchdayId], ct);
        if (md is not null) await db.Entry(md).ReloadAsync(ct);
        return md;
    }

    private async Task<bool> IsPes17PatchAsync(Guid patchVersionId, CancellationToken ct) =>
        Pes17RoundService.IsPes17(await db.PatchVersions.Where(p => p.Id == patchVersionId).Select(p => p.Name).FirstOrDefaultAsync(ct));

    /// <summary>Chạy trong transaction; kết quả lỗi (commit = false) → rollback để không ghi dở.</summary>
    private async Task<T> InTransactionAsync<T>(Func<Task<T>> action, Func<T, bool> commit, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var result = await action();
        if (commit(result)) await tx.CommitAsync(ct);
        else
        {
            await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
        }
        return result;
    }
}
