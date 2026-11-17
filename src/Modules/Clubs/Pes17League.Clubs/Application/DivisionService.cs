using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Infrastructure;

namespace Pes17League.Clubs.Application;

/// <summary>Sức mạnh CLB: OVR trung bình đội hình hiện tại (chỉ số mùa đang xét); Players = số cầu thủ tính được.</summary>
public sealed record ClubStrength(Guid ClubId, string Name, string? Country, double Ovr, int Players);

public sealed record EditionAllocation(Competition Competition, IReadOnlyList<(ClubStrength Club, int Seed)> Clubs);

/// <param name="FromPreviousSeason">false = chia theo OVR (mùa đầu), true = lên/xuống hạng từ BXH mùa trước.</param>
/// <param name="Unassigned">CLB không được xếp: không có nước (vd CLB alpha) hoặc (mùa sau) chưa dự giải quốc gia nào mùa trước — admin thêm tay.</param>
/// <param name="Errors">Lỗi chặn ghi (số CLB lẻ lệch suất, thiếu CLB…).</param>
/// <param name="Notes">Diễn giải cho admin: lên/xuống hạng, suất thêm.</param>
public sealed record SeasonAllocation(bool FromPreviousSeason, IReadOnlyList<EditionAllocation> Editions, IReadOnlyList<ClubStrength> Unassigned,
    IReadOnlyList<string> Errors, IReadOnlyList<string> Notes);

/// <summary>Vòng xa nhất của CLB ở cúp (theo <see cref="FixtureStage"/>; Champion = vô địch).</summary>
public enum CupReach { LeaguePhase, KnockoutPlayoff, RoundOf16, QuarterFinal, SemiFinal, Final, Champion }

/// <summary>Thành tích cúp mùa trước của 1 CLB: cúp hạng CupTier (1 = C1), vòng xa nhất, điểm/hiệu số/bàn thắng league phase.</summary>
public sealed record CupRecord(Guid ClubId, int CupTier, CupReach Reached, int Points, int GoalDifference, int GoalsFor);

/// <summary>
/// Bước 4 (PO duyệt 06/11): chia CLB vào các giải của mùa (docs/domain-admin-design.md: A3, A4, A5, C1/C2).
/// Mùa đầu (phần A) theo OVR:
/// - Mỗi nước: xếp CLB theo OVR trung bình đội hình, nửa mạnh vào hạng 1 (Đức 10/8).
/// - C1: ContinentalPlaces đội OVR cao nhất hạng 1 mỗi nước + LooseClubPlaces CLB lẻ mạnh nhất; C2: SecondaryContinentalPlaces đội kế tiếp
///   + CLB lẻ còn lại. Thiếu so với LeaguePhaseTeams = suất thêm cho CLB quốc nội OVR cao nhất chưa có suất cúp.
/// Mùa sau (phần B) theo BXH cuối mùa trước (mọi giải mùa trước phải Finished):
/// - RelegationPlaces đội cuối hạng 1 xuống, PromotionPlaces đội đầu hạng 2 lên (giải đích theo RelegatesToId/PromotesToId).
/// - C1/C2 theo thứ hạng hạng 1 mùa trước. Suất C2 thêm: vô địch C2 mùa trước; đã có suất (qua giải quốc gia hoặc là CLB lẻ) thì cho
///   đội đứng ngay sau nhóm có suất (thứ 7) nhiều điểm nhất trong các giải hạng 1 (bằng điểm → hiệu số → bàn thắng).
/// - CLB lẻ xếp theo thành tích cúp mùa trước: vòng xa nhất; cùng vòng thì C1 trên C2, rồi điểm, hiệu số, bàn thắng league phase;
///   CLB không dự cúp mùa trước xếp cuối theo OVR. LooseClubPlaces đầu vào C1, kế tiếp vào C2.
/// CLB lẻ = có nước nhưng nước đó không có giải quốc gia đang hoạt động; số CLB lẻ phải khớp tổng suất CLB lẻ của các cúp (không tự cắt/bù).
/// Hạt giống = thứ hạng OVR trong giải (cả mùa sau). Ghi khi mọi mùa giải liên quan còn Draft (ghi đè lần trước), mỗi mùa giải 1 dòng AdminAuditEntry.
/// </summary>
public sealed class DivisionService(ClubsDbContext db, TimeProvider clock)
{
    /// <summary>Số đội hạng 1 mùa đầu khác nửa (A5); nước khác = nửa, lẻ thì hạng 1 hơn 1 đội.</summary>
    public static readonly IReadOnlyDictionary<string, int> FirstSeasonTopTierTeams = new Dictionary<string, int> { ["GER"] = 10 };

    private readonly PriceModel _price = new();

    private static List<ClubStrength> ByOvr(IEnumerable<ClubStrength> clubs) =>
        [.. clubs.OrderByDescending(c => c.Ovr).ThenBy(c => c.Name, StringComparer.Ordinal).ThenBy(c => c.ClubId)];

    private static Dictionary<string, List<Competition>> Leagues(IEnumerable<Competition> active) =>
        active.Where(c => c.Type == CompetitionType.DomesticLeague).GroupBy(c => c.Country!).ToDictionary(g => g.Key, g => g.OrderBy(c => c.Tier).ToList());

    public static SeasonAllocation AllocateFirstSeason(IReadOnlyList<Competition> competitions, IReadOnlyList<ClubStrength> clubs)
    {
        var errors = new List<string>();
        var notes = new List<string>();
        var active = competitions.Where(c => c.Status == CompetitionStatus.Active).ToList();
        var byStrength = ByOvr(clubs);
        var leagues = Leagues(active);
        var result = new Dictionary<Competition, List<ClubStrength>>();

        foreach (var (country, tiers) in leagues.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var pool = byStrength.Where(c => c.Country == country).ToList();
            if (tiers.Count > 2) { errors.Add($"{country}: chưa hỗ trợ quá 2 hạng"); continue; }
            var top = tiers.Count == 1 ? pool.Count : FirstSeasonTopTierTeams.GetValueOrDefault(country, (pool.Count + 1) / 2);
            if (top <= 0 || (tiers.Count == 2 && top >= pool.Count)) { errors.Add($"{country}: {pool.Count} CLB không đủ chia {tiers.Count} hạng"); continue; }
            result[tiers[0]] = pool.Take(top).ToList();
            if (tiers.Count == 2) result[tiers[1]] = pool.Skip(top).ToList();
        }

        var loose = byStrength.Where(c => c.Country is not null && !leagues.ContainsKey(c.Country)).ToList();
        var topOrder = leagues.Values.Where(t => result.ContainsKey(t[0])).ToDictionary(t => t[0], t => result[t[0]]);
        var domestic = byStrength.Where(c => c.Country is not null && leagues.ContainsKey(c.Country)).ToList();
        FillCups(active, topOrder, loose, (_, inCup) => domestic.Where(c => !inCup.Contains(c.ClubId)), result, errors, notes, "OVR cao nhất chưa có suất");
        FillDomesticCups(active, result, errors);
        return new SeasonAllocation(false, ToEditions(active, result), [.. byStrength.Where(c => c.Country is null)], errors, notes);
    }

    /// <param name="tables">BXH cuối mùa trước của từng giải quốc gia (khoá = Competition.Id), đã xếp hạng.</param>
    /// <param name="cups">Thành tích cúp mùa trước (mỗi CLB tối đa 1 dòng).</param>
    public static SeasonAllocation AllocateNextSeason(IReadOnlyList<Competition> competitions, IReadOnlyList<ClubStrength> clubs,
        IReadOnlyDictionary<Guid, IReadOnlyList<StandingRow<Guid>>> tables, IReadOnlyList<CupRecord> cups)
    {
        var errors = new List<string>();
        var notes = new List<string>();
        var active = competitions.Where(c => c.Status == CompetitionStatus.Active).ToList();
        var byId = clubs.ToDictionary(c => c.ClubId);
        var leagues = Leagues(active);
        var result = new Dictionary<Competition, List<ClubStrength>>();
        var topOrder = new Dictionary<Competition, List<ClubStrength>>();
        string Names(IEnumerable<StandingRow<Guid>> rows) => string.Join(", ", rows.Select(r => byId.TryGetValue(r.Club, out var c) ? c.Name : "(CLB đã ngừng)"));
        List<ClubStrength> Present(IEnumerable<StandingRow<Guid>> rows) => [.. rows.Where(r => byId.ContainsKey(r.Club)).Select(r => byId[r.Club])];

        foreach (var (country, tiers) in leagues.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (tiers.Count > 2) { errors.Add($"{country}: chưa hỗ trợ quá 2 hạng"); continue; }
            var missing = tiers.Where(t => !tables.ContainsKey(t.Id)).ToList();
            if (missing.Count > 0) { errors.Add($"{string.Join(", ", missing.Select(t => t.Name))}: mùa trước không có BXH"); continue; }
            var t1 = tables[tiers[0].Id];
            topOrder[tiers[0]] = Present(t1);
            if (tiers.Count == 1) { result[tiers[0]] = Present(t1); continue; }
            var (top, second) = (tiers[0], tiers[1]);
            if (top.RelegatesToId != second.Id || second.PromotesToId != top.Id) { errors.Add($"{top.Name} và {second.Name} không liên kết lên/xuống hạng với nhau"); continue; }
            var t2 = tables[second.Id];
            var down = t1.TakeLast(Math.Min(top.Rules.RelegationPlaces, t1.Count)).ToList();
            var up = t2.Take(second.Rules.PromotionPlaces).ToList();
            result[top] = Present(t1.SkipLast(down.Count).Concat(up));
            result[second] = Present(t2.Skip(up.Count).Concat(down));
            if (down.Count > 0) notes.Add($"Xuống {second.Name}: {Names(down)}");
            if (up.Count > 0) notes.Add($"Lên {top.Name}: {Names(up)}");
        }

        var placed = result.Values.SelectMany(l => l).Select(c => c.ClubId).ToHashSet();
        var unassigned = ByOvr(clubs.Where(c => c.Country is null || leagues.ContainsKey(c.Country) && !placed.Contains(c.ClubId)));

        var record = cups.GroupBy(r => r.ClubId).ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.Reached).ThenBy(r => r.CupTier).First());
        var looseClubs = clubs.Where(c => c.Country is not null && !leagues.ContainsKey(c.Country)).ToList();
        var loose = looseClubs.Where(c => record.ContainsKey(c.ClubId)).Select(c => (Club: c, R: record[c.ClubId]))
            .OrderByDescending(x => x.R.Reached).ThenBy(x => x.R.CupTier).ThenByDescending(x => x.R.Points).ThenByDescending(x => x.R.GoalDifference)
            .ThenByDescending(x => x.R.GoalsFor).ThenBy(x => x.Club.Name, StringComparer.Ordinal).Select(x => x.Club)
            .Concat(ByOvr(looseClubs.Where(c => !record.ContainsKey(c.ClubId)))).ToList();

        // Ứng viên suất thêm: CLB hạng 1 mùa trước chưa có suất, theo vị trí rồi điểm/hiệu số/bàn thắng (thứ 7 nhiều điểm nhất đứng đầu).
        var byPlace = topOrder.Keys.SelectMany(c => tables[c.Id].Select((r, i) => (Row: r, Place: i))).Where(x => byId.ContainsKey(x.Row.Club))
            .OrderBy(x => x.Place).ThenByDescending(x => x.Row.Points).ThenByDescending(x => x.Row.GoalDifference).ThenByDescending(x => x.Row.GoalsFor)
            .ThenBy(x => byId[x.Row.Club].Name, StringComparer.Ordinal).Select(x => byId[x.Row.Club]).ToList();
        IEnumerable<ClubStrength> Extra(Competition cup, HashSet<Guid> inCup)
        {
            var champion = cups.FirstOrDefault(r => r.CupTier == cup.Tier && r.Reached == CupReach.Champion);
            if (champion is not null && cup.Tier == 2)
            {
                var name = byId.TryGetValue(champion.ClubId, out var ch) ? ch.Name : "(CLB đã ngừng)";
                if (ch is not null && !inCup.Contains(ch.ClubId)) { notes.Add($"{cup.Name}: suất thêm cho {name} (vô địch mùa trước)"); yield return ch; }
                else notes.Add($"{cup.Name}: vô địch mùa trước {name} đã có suất → suất thêm theo thứ hạng");
            }
            foreach (var c in byPlace) if (!inCup.Contains(c.ClubId)) yield return c;
        }
        FillCups(active, topOrder, loose, Extra, result, errors, notes, "thứ hạng");
        FillDomesticCups(active, result, errors);
        return new SeasonAllocation(true, ToEditions(active, result), unassigned, errors, notes);
    }

    /// <summary>Cúp quốc gia: mọi CLB hạng 1+2 cùng nước (đã xếp giải quốc nội).</summary>
    private static void FillDomesticCups(List<Competition> active, Dictionary<Competition, List<ClubStrength>> result, List<string> errors)
    {
        foreach (var cup in active.Where(c => c.Type == CompetitionType.DomesticCup).OrderBy(c => c.Country, StringComparer.Ordinal))
        {
            if (cup.Country is null) { errors.Add($"{cup.Name}: thiếu mã nước"); continue; }
            var entrants = result.Where(kv => kv.Key.Type == CompetitionType.DomesticLeague && kv.Key.Country == cup.Country)
                .SelectMany(kv => kv.Value).GroupBy(c => c.ClubId).Select(g => g.First()).ToList();
            if (entrants.Count < 2) errors.Add($"{cup.Name}: cần ≥ 2 CLB từ giải quốc nội {cup.Country} (đang có {entrants.Count})");
            result[cup] = entrants;
        }
    }

    /// <summary>Chia suất các cúp (C1 trước C2): suất quốc nội theo thứ tự <paramref name="topOrder"/> từng giải hạng 1, CLB lẻ theo thứ tự
    /// <paramref name="loose"/>; rồi suất thêm (thiếu so với LeaguePhaseTeams) lấy lần lượt từ <paramref name="extra"/>.</summary>
    private static void FillCups(List<Competition> active, Dictionary<Competition, List<ClubStrength>> topOrder, List<ClubStrength> loose,
        Func<Competition, HashSet<Guid>, IEnumerable<ClubStrength>> extra, Dictionary<Competition, List<ClubStrength>> result,
        List<string> errors, List<string> notes, string extraRule)
    {
        var cups = active.Where(c => c.Type == CompetitionType.ContinentalCup).OrderBy(c => c.Tier).ToList();
        if (cups.Count == 0) return;
        int Places(Competition cup, Competition league) => cup.Tier == 1 ? league.Rules.ContinentalPlaces : league.Rules.SecondaryContinentalPlaces;
        var inCup = new HashSet<Guid>();
        var looseLeft = loose.AsEnumerable();
        foreach (var cup in cups)
        {
            var entrants = new List<ClubStrength>();
            foreach (var (league, order) in topOrder) entrants.AddRange(order.Where(c => !inCup.Contains(c.ClubId)).Take(Places(cup, league)));
            var looseTake = cup.Rules.LooseClubPlaces ?? 0;
            entrants.AddRange(looseLeft.Take(looseTake));
            looseLeft = looseLeft.Skip(looseTake);
            inCup.UnionWith(entrants.Select(c => c.ClubId));
            result[cup] = entrants;
        }
        var looseSlots = cups.Sum(c => c.Rules.LooseClubPlaces ?? 0);
        if (loose.Count != looseSlots) errors.Add($"Có {loose.Count} CLB lẻ nhưng các cúp có {looseSlots} suất CLB lẻ: sửa nước CLB hoặc luật cúp");
        foreach (var cup in cups)
        {
            var count = cup.Rules.LeaguePhaseTeams is { } n ? n - result[cup].Count : 0;
            if (count < 0) { errors.Add($"{cup.Name}: {result[cup].Count} đội vượt {cup.Rules.LeaguePhaseTeams}"); continue; }
            if (count == 0) continue;
            var add = extra(cup, inCup).Take(count).ToList();
            if (add.Count < count) errors.Add($"{cup.Name}: thiếu {count - add.Count} đội");
            if (add.Count > 0) notes.Add($"{cup.Name}: suất thêm ({extraRule}) — {string.Join(", ", add.Select(c => c.Name))}");
            result[cup].AddRange(add);
            inCup.UnionWith(add.Select(c => c.ClubId));
        }
    }

    private static List<EditionAllocation> ToEditions(List<Competition> active, Dictionary<Competition, List<ClubStrength>> result) =>
        [.. active.Where(result.ContainsKey).OrderBy(c => c.Type).ThenBy(c => c.Country, StringComparer.Ordinal).ThenBy(c => c.Tier)
            .Select(c => new EditionAllocation(c, [.. ByOvr(result[c]).Select((x, i) => (x, i + 1))]))];

    /// <summary>Xem trước chia đội cho mùa <paramref name="seasonId"/>: chưa có mùa trước dự các giải đang hoạt động → theo OVR, có rồi → lên/xuống hạng.</summary>
    public async Task<SeasonAllocation> PreviewAsync(Guid seasonId, CancellationToken ct = default)
    {
        var (season, patch, competitions) = await LoadAsync(seasonId, ct);
        var ids = competitions.Select(c => c.Id).ToList();
        var prev = await (from e in db.Editions join s in db.Seasons on e.SeasonId equals s.Id
                          where ids.Contains(e.CompetitionId) && s.Number < season.Number
                          orderby s.Number descending select s).FirstOrDefaultAsync(ct);
        var clubs = await StrengthsAsync(season.Id, patch, ct);
        if (prev is null) return AllocateFirstSeason(competitions, clubs);

        var editions = await db.Editions.Where(e => e.SeasonId == prev.Id && ids.Contains(e.CompetitionId)).ToListAsync(ct);
        var byComp = competitions.ToDictionary(c => c.Id);
        if (editions.FirstOrDefault(e => e.Status != EditionStatus.Finished) is { } open)
            throw new AdminRuleException($"{byComp[open.CompetitionId].Name} {prev.Name} chưa kết thúc");
        var edIds = editions.Select(e => e.Id).ToList();
        var members = await db.EditionClubs.Where(x => edIds.Contains(x.EditionId)).ToListAsync(ct);
        var names = await db.Clubs.Where(c => members.Select(m => m.ClubId).Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var fixtures = await db.Fixtures.Where(f => f.CompetitionEditionId != null && edIds.Contains(f.CompetitionEditionId.Value)
                && f.Status == FixtureStatus.Played && f.HomeGoals != null && f.AwayGoals != null).ToListAsync(ct);

        var tables = new Dictionary<Guid, IReadOnlyList<StandingRow<Guid>>>();
        var cups = new List<CupRecord>();
        foreach (var e in editions)
        {
            var comp = byComp[e.CompetitionId];
            var clubIds = members.Where(m => m.EditionId == e.Id).Select(m => m.ClubId).ToList();
            var played = fixtures.Where(f => f.CompetitionEditionId == e.Id).ToList();
            List<PlayedFixture<Guid>> Of(FixtureStage stage) =>
                [.. played.Where(f => f.Stage == stage).Select(f => new PlayedFixture<Guid>(f.HomeClubId, f.AwayClubId, f.HomeGoals!.Value, f.AwayGoals!.Value))];
            if (comp.Type == CompetitionType.DomesticLeague)
            {
                tables[comp.Id] = Standings.Compute(clubIds, Of(FixtureStage.League), id => names[id]);
                continue;
            }
            if (comp.Type == CompetitionType.DomesticCup) continue;   // cúp quốc gia không ảnh hưởng suất C1/C2 mùa sau
            var final = played.Where(f => f.Stage == FixtureStage.Final).ToList();
            if (final.Count != 1 || final[0].WinnerClubId() is not { } champion)     // hoà sau 120' → đội thắng loạt penalty (PO 06/11)
                throw new AdminRuleException($"{comp.Name} {prev.Name}: chưa xác định được vô địch (cần đúng 1 trận chung kết có đội thắng; hoà thì cần tỷ số penalty)");
            foreach (var row in Standings.Compute(clubIds, Of(FixtureStage.LeaguePhase), id => names[id]))
            {
                var stages = played.Where(f => f.HomeClubId == row.Club || f.AwayClubId == row.Club).Select(f => f.Stage).Where(s => s != FixtureStage.League).ToList();
                var reached = row.Club == champion ? CupReach.Champion
                    : stages.Count == 0 ? CupReach.LeaguePhase : Enum.Parse<CupReach>(stages.Max().ToString());
                cups.Add(new CupRecord(row.Club, comp.Tier ?? 0, reached, row.Points, row.GoalDifference, row.GoalsFor));
            }
        }
        return AllocateNextSeason(competitions, clubs, tables, cups);
    }

    /// <summary>Ghi kết quả <see cref="PreviewAsync"/>: tạo mùa giải còn thiếu, thay toàn bộ CLB + hạt giống. Trả số mùa giải đã ghi.</summary>
    public async Task<int> ApplyAsync(Guid seasonId, string actor, CancellationToken ct = default)
    {
        var plan = await PreviewAsync(seasonId, ct);
        if (plan.Errors.Count > 0) throw new AdminRuleException(string.Join("; ", plan.Errors));
        var ids = plan.Editions.Select(e => e.Competition.Id).ToList();
        var editions = await db.Editions.Where(e => e.SeasonId == seasonId).ToListAsync(ct);
        if (editions.FirstOrDefault(e => ids.Contains(e.CompetitionId) && e.Status != EditionStatus.Draft) is { } locked)
            throw new AdminRuleException($"{plan.Editions.Single(x => x.Competition.Id == locked.CompetitionId).Competition.Name} đã xếp lịch: không chia lại được");
        var others = editions.Where(e => !ids.Contains(e.CompetitionId)).Select(e => e.Id).ToList();
        if (await db.EditionClubs.AnyAsync(x => others.Contains(x.EditionId), ct))
            throw new AdminRuleException("Mùa này còn giải khác (đã lưu trữ) có CLB: xoá CLB khỏi giải đó trước");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        foreach (var plan1 in plan.Editions)
        {
            var e = editions.SingleOrDefault(x => x.CompetitionId == plan1.Competition.Id);
            if (e is null) db.Editions.Add(e = new CompetitionEdition { CompetitionId = plan1.Competition.Id, SeasonId = seasonId });
            var old = await db.EditionClubs.Where(x => x.EditionId == e.Id).ToListAsync(ct);
            var oldNames = await db.Clubs.Where(c => old.Select(o => o.ClubId).Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
            db.EditionClubs.RemoveRange(old);
            db.EditionClubs.AddRange(plan1.Clubs.Select(x => new EditionClub { EditionId = e.Id, ClubId = x.Club.ClubId, Seed = x.Seed }));
            Audit(e.Id, plan.FromPreviousSeason ? "AllocateFromPrevious" : "AllocateByOvr",
                old.Count == 0 ? null : old.OrderBy(o => o.Seed).Select(o => new { Club = oldNames[o.ClubId], o.Seed }),
                new { Competition = plan1.Competition.Name, Clubs = plan1.Clubs.Select(x => new { Club = x.Club.Name, x.Seed, Ovr = Math.Round(x.Club.Ovr, 1) }) },
                actor);
            await db.SaveChangesAsync(ct);    // xoá trước khi thêm (cùng khoá EditionId + ClubId)
        }
        await tx.CommitAsync(ct);
        return plan.Editions.Count;
    }

    private async Task<(Season Season, Guid Patch, List<Competition> Competitions)> LoadAsync(Guid seasonId, CancellationToken ct)
    {
        var season = await db.Seasons.SingleOrDefaultAsync(s => s.Id == seasonId, ct) ?? throw new AdminRuleException("Không tìm thấy mùa");
        if (season.Status == SeasonStatus.Finished) throw new AdminRuleException("Mùa đã kết thúc");
        if (season.PatchVersionId is not { } patch) throw new AdminRuleException($"{season.Name} chưa nhập patch");
        return (season, patch, await db.Competitions.AsNoTracking().Where(c => c.Status == CompetitionStatus.Active).ToListAsync(ct));
    }

    /// <summary>OVR trung bình đội hình hiện tại (thành viên chưa rời) theo chỉ số hiện tại của mùa; CLB đang hoạt động thuộc patch.</summary>
    public async Task<IReadOnlyList<ClubStrength>> StrengthsAsync(Guid seasonId, Guid patchVersionId, CancellationToken ct = default)
    {
        var clubs = await db.Clubs.Where(c => c.Status == ClubStatus.Active && db.ClubPesMappings.Any(m => m.ClubId == c.Id && m.PatchVersionId == patchVersionId))
            .Select(c => new { c.Id, c.Name, c.Country }).ToListAsync(ct);
        var ids = clubs.Select(c => c.Id).ToList();
        var members = await (from m in db.SquadMemberships
                             where m.LeftAt == null && ids.Contains(m.ClubId)
                             join p in db.Players on m.PlayerId equals p.Id
                             select new { m.ClubId, p.Id, p.Position }).ToListAsync(ct);
        var values = await new EconomyService(db, clock).CurrentAttributesAsync(seasonId, null, ct);
        var ovr = members.Where(m => values.ContainsKey(m.Id)).GroupBy(m => m.ClubId)
            .ToDictionary(g => g.Key, g => (Avg: g.Average(m => _price.Overall(m.Position, values[m.Id])), Count: g.Count()));
        return [.. clubs.Select(c => ovr.TryGetValue(c.Id, out var o) ? new ClubStrength(c.Id, c.Name, c.Country, o.Avg, o.Count) : new ClubStrength(c.Id, c.Name, c.Country, 0, 0))];
    }

    private void Audit(Guid id, string action, object? before, object? after, string actor) =>
        db.AdminAudit.Add(new AdminAuditEntry
        {
            At = clock.GetUtcNow(), Actor = actor, EntityType = nameof(CompetitionEdition), EntityId = id, Action = action,
            Before = before is null ? null : JsonSerializer.Serialize(before),
            After = after is null ? null : JsonSerializer.Serialize(after),
        });
}
