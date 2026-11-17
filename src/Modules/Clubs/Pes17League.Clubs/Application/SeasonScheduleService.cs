using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Infrastructure;

namespace Pes17League.Clubs.Application;

public sealed record ScheduleClub(Guid ClubId, string Name, string? Country, int? Seed);
/// <summary>Mùa giải có CLB, theo thứ tự xếp lịch: giải quốc nội (nước, hạng, tên) rồi cúp (hạng, tên); CLB theo hạt giống.</summary>
public sealed record ScheduleEdition(Guid EditionId, Competition Competition, EditionStatus Status, IReadOnlyList<ScheduleClub> Clubs);
/// <summary>Trận trong lịch; bản xem trước chưa có <see cref="FixtureId"/>.</summary>
public sealed record ScheduleMatch(Guid EditionId, FixtureStage Stage, Guid Home, Guid Away, Guid? FixtureId = null, FixtureStatus Status = FixtureStatus.Scheduled,
    int? HomeGoals = null, int? AwayGoals = null, int? HomePenalties = null, int? AwayPenalties = null, int? Leg = null, Guid? TieId = null);
/// <summary>Khung ngày số <see cref="Number"/> (= Matchday.Number); <see cref="Status"/> null khi mới xem trước.</summary>
public sealed record ScheduleSlot(int Number, CalendarSlot Slot, IReadOnlyList<ScheduleMatch> Matches, MatchdayStatus? Status = null);
/// <param name="Seed">Mã bốc thăm; lịch đã ghi lấy lại từ nhật ký.</param>
public sealed record SeasonSchedule(Guid SeasonId, string SeasonName, SeasonStatus SeasonStatus, int? Seed, bool Written,
    IReadOnlyList<ScheduleEdition> Editions, IReadOnlyList<ScheduleSlot> Slots);

/// <summary>
/// Lịch mùa nhiều giải (PO chọn 06/11, bước 6): xem trước theo mã bốc thăm → "Ghi lịch mùa" (khung ngày, trận quốc nội + league phase,
/// mùa giải → Scheduled, khoá luật) → "Bắt đầu mùa" (<see cref="CompetitionService.StartScheduledSeasonAsync"/>).
/// Cùng mã + cùng CLB/hạt giống → cùng lịch. Vòng loại trực tiếp để trống tới khi vòng trước đá xong (<see cref="AdvanceAsync"/>).
/// </summary>
public sealed class SeasonScheduleService(ClubsDbContext db, TimeProvider clock, Random? rng = null)
{
    public const string ScheduleAction = "ScheduleSeason";
    public const string DrawAction = "DrawKnockout";
    /// <summary>Người thao tác của bước tự động (sinh vòng loại trực tiếp).</summary>
    public const string SystemActor = "system";

    private readonly Random _rng = rng ?? Random.Shared;

    public async Task<SeasonSchedule> PreviewAsync(Guid seasonId, int seed, CancellationToken ct = default)
    {
        var (season, editions) = await LoadDraftAsync(seasonId, ct);
        return new(season.Id, season.Name, season.Status, seed, false, editions, Plan(editions, seed));
    }

    /// <summary>Lịch đã ghi (kèm trạng thái, tỷ số); null = mùa chưa có khung ngày.</summary>
    public async Task<SeasonSchedule?> ViewAsync(Guid seasonId, CancellationToken ct = default)
    {
        var (season, editions) = await LoadAsync(seasonId, ct);
        var mds = await db.Matchdays.AsNoTracking().Where(m => m.SeasonId == seasonId).OrderBy(m => m.Number).ToListAsync(ct);
        if (mds.Count == 0) return null;
        var fixtures = (await (from f in db.Fixtures.AsNoTracking() join m in db.Matchdays on f.MatchdayId equals m.Id where m.SeasonId == seasonId select f)
            .ToListAsync(ct)).ToLookup(f => f.MatchdayId);
        var slots = SeasonCalendar.Number(mds.Select(m => (m.Stage, m.Leg)));
        var after = await db.AdminAudit.Where(a => a.EntityId == seasonId && a.Action == ScheduleAction).OrderByDescending(a => a.At)
            .Select(a => a.After).FirstOrDefaultAsync(ct);
        return new(season.Id, season.Name, season.Status, SeedOf(after), true, editions, [.. mds.Select((m, i) => new ScheduleSlot(m.Number, slots[i],
            [.. fixtures[m.Id].Select(f => new ScheduleMatch(f.CompetitionEditionId ?? Guid.Empty, f.Stage, f.HomeClubId, f.AwayClubId, f.Id, f.Status,
                f.HomeGoals, f.AwayGoals, f.HomePenalties, f.AwayPenalties, f.Leg, f.TieId))], m.Status))]);
    }

    /// <summary>Ghi lịch theo mã <paramref name="seed"/> (như <see cref="PreviewAsync"/>): khung ngày 1..N, trận quốc nội + league phase,
    /// mùa giải → Scheduled với luật chép vào RulesSnapshot; mã ghi vào nhật ký.</summary>
    public async Task<SeasonSchedule> ApplyAsync(Guid seasonId, int seed, string actor, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var (season, editions) = await LoadDraftAsync(seasonId, ct);
        var slots = Plan(editions, seed);
        foreach (var s in slots)
        {
            var md = new Matchday { SeasonId = seasonId, Number = s.Number, Stage = s.Slot.Stage, Leg = s.Slot.Leg };
            db.Matchdays.Add(md);
            db.Fixtures.AddRange(s.Matches.Select(m => new Fixture { MatchdayId = md.Id, HomeClubId = m.Home, AwayClubId = m.Away, CompetitionEditionId = m.EditionId, Stage = m.Stage }));
        }
        var ids = editions.Select(e => e.EditionId).ToList();
        foreach (var e in await db.Editions.Where(e => ids.Contains(e.Id)).ToListAsync(ct))
        {
            e.Status = EditionStatus.Scheduled;
            e.RulesSnapshot = editions.Single(x => x.EditionId == e.Id).Competition.Rules;
        }
        db.AdminAudit.Add(new AdminAuditEntry
        {
            At = clock.GetUtcNow(), Actor = actor, EntityType = nameof(Season), EntityId = season.Id, Action = ScheduleAction,
            After = JsonSerializer.Serialize(new
            {
                Seed = seed, Matchdays = slots.Count, Fixtures = slots.Sum(s => s.Matches.Count), Editions = editions.Select(e => e.Competition.Name),
            }),
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return (await ViewAsync(seasonId, ct))!;
    }

    /// <summary>
    /// Sau khi chốt khung ngày (trong transaction chốt vòng, trước khi mở kỳ CN khung sau): giải quốc nội đá hết trận → Finished;
    /// cúp đá hết các trận đã có → sinh vòng loại trực tiếp kế tiếp; chung kết có đội thắng → Finished.
    /// Cúp châu lục: nhánh cố định từ BXH league phase. Cúp quốc gia: bốc lại mỗi vòng (1 lượt).
    /// </summary>
    public async Task AdvanceAsync(Guid seasonId, CancellationToken ct = default)
    {
        var editions = await db.Editions.Where(e => e.SeasonId == seasonId && e.Status == EditionStatus.Active).ToListAsync(ct);
        var ids = editions.Select(e => e.Id).ToList();
        var fixtures = await db.Fixtures.Where(f => f.CompetitionEditionId != null && ids.Contains(f.CompetitionEditionId.Value)).ToListAsync(ct);
        var compIds = editions.Select(e => e.CompetitionId).ToList();
        var comps = await db.Competitions.AsNoTracking().Where(c => compIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        foreach (var e in editions)
        {
            var own = fixtures.Where(f => f.CompetitionEditionId == e.Id).ToList();
            if (own.Count == 0 || own.Any(f => f.Status != FixtureStatus.Played)) continue;
            var comp = comps[e.CompetitionId];
            if (comp.Type == CompetitionType.DomesticLeague) e.Status = EditionStatus.Finished;
            else if (own.Max(f => f.Stage) != FixtureStage.Final)
            {
                if (comp.Type == CompetitionType.DomesticCup) await DrawDomesticNextAsync(e, comp, own, ct);
                else await DrawContinentalNextAsync(e, comp, own, ct);
            }
            else if (own.Single(f => f.Stage == FixtureStage.Final).WinnerClubId() is not null) e.Status = EditionStatus.Finished;   // hoà → chờ admin nhập penalty
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task DrawContinentalNextAsync(CompetitionEdition e, Competition comp, List<Fixture> own, CancellationToken ct)
    {
        var rules = e.RulesSnapshot ?? comp.Rules;
        var clubs = await db.EditionClubs.Where(x => x.EditionId == e.Id).Select(x => x.ClubId).ToListAsync(ct);
        var names = await db.Clubs.Where(c => clubs.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var played = own.Where(f => f.Stage == FixtureStage.LeaguePhase)
            .Select(f => new PlayedFixture<Guid>(f.HomeClubId, f.AwayClubId, f.HomeGoals!.Value, f.AwayGoals!.Value)).ToList();
        var ranking = Standings.Compute(clubs, played, id => names[id]).Select(r => r.Club).ToList();
        Guid Winner(KnockoutTie<Guid> t)
        {
            var leg1 = own.SingleOrDefault(f => f.Stage == t.Stage && f.Leg == 1 && f.HomeClubId == t.Lower && f.AwayClubId == t.Higher);
            var leg2 = own.SingleOrDefault(f => f.Stage == t.Stage && f.Leg == 2 && f.HomeClubId == t.Higher && f.AwayClubId == t.Lower);
            if (leg1 is null || leg2 is null) throw new InvalidOperationException($"{comp.Name}: thiếu trận {t.Stage} {names[t.Higher]}–{names[t.Lower]}");
            return CupDraw.TieWinner(t, (leg1.HomeGoals!.Value, leg1.AwayGoals!.Value), (leg2.HomeGoals!.Value, leg2.AwayGoals!.Value));
        }
        List<Guid> Winners(IReadOnlyList<KnockoutTie<Guid>> ties) => [.. ties.Select(Winner)];

        var last = own.Max(f => f.Stage);
        var (direct, playoff) = (rules.DirectKnockoutPlaces!.Value, rules.PlayoffPlaces!.Value);
        var next = CupDraw.Playoff(ranking, direct, playoff);
        if (last != FixtureStage.LeaguePhase)
        {
            next = CupDraw.FirstRound(ranking, direct, Winners(next), _rng);
            while (next[0].Stage <= last) next = CupDraw.NextRound(Winners(next), ranking, _rng);
        }

        var stage = next[0].Stage;
        foreach (var t in next)
        {
            if (stage == FixtureStage.Final)
            {
                db.Fixtures.Add(new Fixture { MatchdayId = await TargetMatchdayAsync(e, CompetitionType.ContinentalCup, stage, null, ct),
                    HomeClubId = t.Higher, AwayClubId = t.Lower, CompetitionEditionId = e.Id, Stage = stage });
                continue;
            }
            var tie = Guid.NewGuid();
            db.Fixtures.Add(new Fixture { MatchdayId = await TargetMatchdayAsync(e, CompetitionType.ContinentalCup, stage, 1, ct),
                HomeClubId = t.Lower, AwayClubId = t.Higher, CompetitionEditionId = e.Id, Stage = stage, Leg = 1, TieId = tie });
            db.Fixtures.Add(new Fixture { MatchdayId = await TargetMatchdayAsync(e, CompetitionType.ContinentalCup, stage, 2, ct),
                HomeClubId = t.Higher, AwayClubId = t.Lower, CompetitionEditionId = e.Id, Stage = stage, Leg = 2, TieId = tie });
        }
        AuditDraw(e.Id, comp.Name, stage, next.Select(t => stage == FixtureStage.Final ? $"{names[t.Higher]} – {names[t.Lower]}" : $"{names[t.Lower]} – {names[t.Higher]}"));
    }

    private async Task DrawDomesticNextAsync(CompetitionEdition e, Competition comp, List<Fixture> own, CancellationToken ct)
    {
        var clubs = await db.EditionClubs.Where(x => x.EditionId == e.Id).Select(x => x.ClubId).ToListAsync(ct);
        var names = await db.Clubs.Where(c => clubs.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var stages = CupDraw.SingleLegStages(clubs.Count).Select(s => s.Stage).ToList();
        var last = own.Max(f => f.Stage);
        var idx = stages.IndexOf(last);
        if (idx < 0 || idx >= stages.Count - 1) throw new InvalidOperationException($"{comp.Name}: không có vòng sau {last}");
        var lastFixtures = own.Where(f => f.Stage == last).ToList();
        var winners = lastFixtures.Select(f => f.WinnerClubId() ?? throw new InvalidOperationException(
            $"{comp.Name}: {names[f.HomeClubId]}–{names[f.AwayClubId]} hoà, cần tỷ số penalty")).ToList();
        if (idx == 0)
        {
            var played = lastFixtures.SelectMany(f => new[] { f.HomeClubId, f.AwayClubId }).ToHashSet();
            winners = [.. clubs.Where(c => !played.Contains(c)), .. winners];
        }
        var next = CupDraw.SingleLegDraw(winners, _rng);
        var stage = next[0].Stage;
        var md = await TargetMatchdayAsync(e, CompetitionType.DomesticCup, stage, null, ct);
        foreach (var t in next)
            db.Fixtures.Add(new Fixture { MatchdayId = md, HomeClubId = t.Higher, AwayClubId = t.Lower, CompetitionEditionId = e.Id, Stage = stage });
        AuditDraw(e.Id, comp.Name, stage, next.Select(t => $"{names[t.Higher]} – {names[t.Lower]}"));
    }

    /// <summary>Chọn khung ngày Planned đúng Stage/Leg: dùng chung với cúp cùng loại; cúp quốc gia lấy khung sớm, châu lục lấy khung muộn (khi trùng tên giai đoạn).</summary>
    private async Task<Guid> TargetMatchdayAsync(CompetitionEdition e, CompetitionType type, FixtureStage stage, int? leg, CancellationToken ct)
    {
        var mds = await db.Matchdays.Where(m => m.SeasonId == e.SeasonId && m.Stage == stage && m.Leg == leg && m.Status == MatchdayStatus.Planned)
            .OrderBy(m => m.Number).ToListAsync(ct);
        if (mds.Count == 0) throw new InvalidOperationException($"Không có khung ngày {stage} lượt {leg} chưa mở");
        var mdIds = mds.Select(m => m.Id).ToList();
        var on = (await db.Fixtures.Where(f => mdIds.Contains(f.MatchdayId)).Select(f => new { f.MatchdayId, f.CompetitionEditionId }).ToListAsync(ct))
            .ToLookup(x => x.MatchdayId);
        var editionIds = on.SelectMany(g => g).Where(x => x.CompetitionEditionId is not null).Select(x => x.CompetitionEditionId!.Value).Distinct().ToList();
        var types = editionIds.Count == 0 ? new Dictionary<Guid, CompetitionType>()
            : await (from ed in db.Editions join c in db.Competitions on ed.CompetitionId equals c.Id where editionIds.Contains(ed.Id)
                     select new { ed.Id, c.Type }).ToDictionaryAsync(x => x.Id, x => x.Type, ct);
        var same = mds.Where(m => on[m.Id].Any() && on[m.Id].All(f => f.CompetitionEditionId is { } id && types.GetValueOrDefault(id) == type)).ToList();
        if (same.Count > 0) return same[0].Id;
        var empty = mds.Where(m => !on[m.Id].Any()).ToList();
        if (empty.Count == 0) throw new InvalidOperationException($"Không còn khung {stage} trống cho {type}");
        return type == CompetitionType.DomesticCup ? empty[0].Id : empty[^1].Id;
    }

    private void AuditDraw(Guid editionId, string competition, FixtureStage stage, IEnumerable<string> ties) =>
        db.AdminAudit.Add(new AdminAuditEntry
        {
            At = clock.GetUtcNow(), Actor = SystemActor, EntityType = nameof(CompetitionEdition), EntityId = editionId, Action = DrawAction,
            After = JsonSerializer.Serialize(new { Competition = competition, Stage = stage.ToString(), Ties = ties }),
        });

    /// <summary>Xếp lịch (không ghi): giải quốc nội vòng tròn theo Legs; cúp châu lục bốc league phase; cúp quốc gia bốc vòng mở (1 lượt).
    /// Giải ít vòng hơn nghỉ rải đều trong các khung quốc nội.</summary>
    private static List<ScheduleSlot> Plan(IReadOnlyList<ScheduleEdition> editions, int seed)
    {
        var rng = new Random(seed);
        var domestic = editions.Where(e => e.Competition.Type == CompetitionType.DomesticLeague).ToList();
        var continental = editions.Where(e => e.Competition.Type == CompetitionType.ContinentalCup).ToList();
        var domesticCups = editions.Where(e => e.Competition.Type == CompetitionType.DomesticCup).ToList();
        foreach (var group in new[] { domestic, continental, domesticCups })
            if (group.SelectMany(e => e.Clubs.Select(c => (Edition: e, Club: c))).GroupBy(x => x.Club.ClubId).FirstOrDefault(g => g.Count() > 1) is { } dup)
                throw new AdminRuleException($"{dup.First().Club.Name} có ở 2 giải: {string.Join(", ", dup.Select(x => x.Edition.Competition.Name))}");

        var leagues = domestic.Select(e =>
        {
            var n = e.Clubs.Count;
            if (n < 2 || n % 2 != 0) throw new AdminRuleException($"{e.Competition.Name}: số CLB phải chẵn, tối thiểu 2 (đang có {n})");
            var clubs = e.Clubs.Select(c => c.ClubId).Order().ToArray();
            rng.Shuffle(clubs);
            var rounds = RoundRobin.DoubleRoundRobin(clubs);
            return (Edition: e, Rounds: e.Competition.Rules.Legs == 1 ? [.. rounds.Take(n - 1)] : rounds);
        }).ToList();
        var draws = continental.Select(e =>
        {
            var r = e.Competition.Rules;
            if (r is not { LeaguePhaseMatches: { } lpMatches, DirectKnockoutPlaces: { } direct, PlayoffPlaces: { } playoff })
                throw new AdminRuleException($"{e.Competition.Name}: thiếu luật league phase");
            if (r.Legs != 2) throw new AdminRuleException($"{e.Competition.Name}: loại trực tiếp 1 lượt chưa hỗ trợ");
            try
            {
                var knockout = CupDraw.KnockoutStages(e.Clubs.Count, direct, playoff);
                return (Edition: e, Rounds: CupDraw.LeaguePhaseRounds(e.Clubs.Select(c => (c.ClubId, c.Country)).ToList(), lpMatches, rng), Knockout: knockout);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                var message = ex is ArgumentException { ParamName: { } p } ? ex.Message.Replace($" (Parameter '{p}')", "") : ex.Message;
                throw new AdminRuleException($"{e.Competition.Name}: {message}");
            }
        }).ToList();
        var cupOpens = domesticCups.Select(e =>
        {
            if (e.Competition.Rules.Legs != 1) throw new AdminRuleException($"{e.Competition.Name}: cúp quốc gia phải 1 lượt");
            if (e.Clubs.Count < 2) throw new AdminRuleException($"{e.Competition.Name}: cần ≥ 2 CLB");
            try
            {
                var seeded = e.Clubs.OrderBy(c => c.Seed ?? int.MaxValue).ThenBy(c => c.Name, StringComparer.Ordinal).Select(c => c.ClubId).ToList();
                return (Edition: e, Open: CupDraw.SingleLegOpen(seeded, rng), Stages: CupDraw.SingleLegStages(e.Clubs.Count));
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                var message = ex is ArgumentException { ParamName: { } p } ? ex.Message.Replace($" (Parameter '{p}')", "") : ex.Message;
                throw new AdminRuleException($"{e.Competition.Name}: {message}");
            }
        }).ToList();

        var domesticSlots = leagues.Count == 0 ? 0 : leagues.Max(l => l.Rounds.Count);
        var domesticKo = SeasonCalendar.Union(cupOpens.Select(c => c.Stages));
        var calendar = SeasonCalendar.Build(domesticSlots, draws.Count == 0 ? 0 : draws.Max(d => d.Rounds.Count),
            SeasonCalendar.Union(draws.Select(d => d.Knockout)), domesticKo);
        var bySlot = calendar.Select(_ => new List<ScheduleMatch>()).ToList();
        void Add(FixtureStage? stage, int? round, int? leg, ScheduleEdition e, FixtureStage fixtureStage, IEnumerable<(Guid Home, Guid Away)> games) =>
            bySlot[calendar.Select((s, i) => (s, i)).First(x => x.s.Stage == stage && x.s.Round == round && x.s.Leg == leg).i]
                .AddRange(games.Select(g => new ScheduleMatch(e.EditionId, fixtureStage, g.Home, g.Away)));
        foreach (var (e, rounds) in leagues)
            for (var r = 1; r <= rounds.Count; r++) Add(null, SeasonCalendar.DomesticSlot(r, rounds.Count, domesticSlots), null, e, FixtureStage.League, rounds[r - 1]);
        foreach (var (e, rounds, _) in draws)
            for (var r = 1; r <= rounds.Count; r++) Add(FixtureStage.LeaguePhase, r, null, e, FixtureStage.LeaguePhase, rounds[r - 1]);
        // Vòng mở cúp quốc gia: ghi vào khung Stage đầu tiên (các nước chung khung).
        foreach (var (e, open, stages) in cupOpens)
        {
            var stage = stages[0].Stage;
            Add(stage, null, null, e, stage, open.Select(t => (t.Higher, t.Lower)));
        }
        return [.. calendar.Select((s, i) => new ScheduleSlot(i + 1, s, bySlot[i]))];
    }

    /// <summary>Mùa chưa bắt đầu, chưa có khung ngày, có mùa giải có CLB và mọi mùa giải đó còn Draft.</summary>
    private async Task<(Season, List<ScheduleEdition>)> LoadDraftAsync(Guid seasonId, CancellationToken ct)
    {
        var (season, editions) = await LoadAsync(seasonId, ct);
        if (season.Status != SeasonStatus.Planned) throw new AdminRuleException($"{season.Name} đã bắt đầu");
        if (await db.Matchdays.AnyAsync(m => m.SeasonId == seasonId, ct)) throw new AdminRuleException($"{season.Name} đã ghi lịch");
        if (editions.Count == 0) throw new AdminRuleException($"{season.Name} chưa chia CLB vào giải");
        if (editions.FirstOrDefault(e => e.Status != EditionStatus.Draft) is { } locked) throw new AdminRuleException($"{locked.Competition.Name} đã xếp lịch");
        return (season, editions);
    }

    private async Task<(Season, List<ScheduleEdition>)> LoadAsync(Guid seasonId, CancellationToken ct)
    {
        var season = await db.Seasons.AsNoTracking().SingleOrDefaultAsync(s => s.Id == seasonId, ct) ?? throw new AdminRuleException("Không tìm thấy mùa");
        var editions = await db.Editions.AsNoTracking().Where(e => e.SeasonId == seasonId).ToListAsync(ct);
        var ids = editions.Select(e => e.Id).ToList();
        var compIds = editions.Select(e => e.CompetitionId).ToList();
        var comps = await db.Competitions.AsNoTracking().Where(c => compIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var members = (await (from x in db.EditionClubs join c in db.Clubs on x.ClubId equals c.Id where ids.Contains(x.EditionId)
                              select new { x.EditionId, c.Id, c.Name, c.Country, x.Seed }).ToListAsync(ct)).ToLookup(m => m.EditionId);
        return (season, [.. editions
            .Select(e => new ScheduleEdition(e.Id, comps[e.CompetitionId], e.Status, [.. members[e.Id].Select(m => new ScheduleClub(m.Id, m.Name, m.Country, m.Seed))
                .OrderBy(c => c.Seed ?? int.MaxValue).ThenBy(c => c.Name, StringComparer.Ordinal)]))
            .Where(e => e.Clubs.Count > 0)
            .OrderBy(e => e.Competition.Type).ThenBy(e => e.Competition.Country, StringComparer.Ordinal).ThenBy(e => e.Competition.Tier)
            .ThenBy(e => e.Competition.Name, StringComparer.Ordinal)]);
    }

    private static int? SeedOf(string? json)
    {
        if (json is null) return null;
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("Seed", out var s) ? s.GetInt32() : null;
    }
}
