using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Application;
using Pes17League.Clubs.Infrastructure;
using Pes17League.OptionFile;

namespace Pes17League.Web;

/// <summary>1 cầu thủ trong 1 trận đã đá: điểm + thống kê. <see cref="Goals"/> theo bảng bàn thắng (trận chưa có tên người ghi → số bàn của bảng điểm).</summary>
public sealed record PlayerLine(Guid FixtureId, int Matchday, Guid? EditionId, Guid ClubId, Guid OpponentId, Guid PlayerId, string Name, Position Position,
    int Slot, decimal? Rating, int Goals, int Shots, int ShotsOnTarget, int? InMinute, int? OutMinute)
{
    public bool Starter => Slot < 11;
}
/// <summary>1 dòng bảng chỉ số: <see cref="Id"/> = cầu thủ hoặc CLB; <see cref="ClubId"/> = CLB để vẽ huy hiệu.</summary>
public sealed record Leader(Guid Id, string Name, Guid ClubId, string Value, string? Note = null);
/// <param name="Players">true = bảng cầu thủ (link hồ sơ), false = bảng CLB.</param>
public sealed record Board(string Title, string Hint, bool Players, IReadOnlyList<Leader> Rows);
/// <param name="MinApps">Số trận tối thiểu để vào bảng điểm trung bình.</param>
public sealed record StatsPage(IReadOnlyList<Board> Players, IReadOnlyList<Board> Teams, int Matches, int Goals, int MinApps);
/// <summary>Kết quả bất ngờ: đội thắng có OVR trung bình đá chính thấp hơn đội thua ≥ 2.</summary>
public sealed record Upset(FixtureView Fixture, Guid Winner, double WinnerOvr, double LoserOvr);
/// <param name="Number">Khung ngày (vòng) đang xem; <paramref name="Rounds"/> = các khung đã có trận đá xong.</param>
public sealed record RoundRecap(int Number, IReadOnlyList<int> Rounds, IReadOnlyList<FixtureView> Fixtures, IReadOnlyList<PlayerLine> Team, PlayerLine? Best,
    IReadOnlyList<Upset> Upsets, FixtureView? Biggest, FixtureView? Wildest, int Played, int Goals, int HomeWins, int Draws, int AwayWins);
public sealed record AttrView(string Group, string Label, int Value, int Delta, int Max, string Field);
/// <summary>Cột thêm của bảng đội hình trang CLB: tuổi (chỉ số AGE mùa), số trận/bàn/điểm TB/xuất sắc trận cho CLB trong mùa.</summary>
public sealed record SquadStat(int? Age, int Apps, int Goals, decimal? Rating, int Motm);
public sealed record PlayerGame(PlayerLine Line, FixtureView Fixture, bool Motm);
public sealed record TransferView(DateTimeOffset At, string? From, string? To, long Fee, TransferKind Kind);
/// <summary>Hồ sơ công khai <c>/cau-thu/{id}</c>: CLB hiện tại, giá, hợp đồng, chỉ số hiện tại (snapshot mùa + thay đổi), trận đã đá trong mùa.</summary>
public sealed record PlayerProfile(Guid Id, string Name, Position Position, int? BirthYear, string? Nationality, Guid? ClubId, string? Club, string? ClubLogo, int? Shirt,
    bool Starter, int Overall, long Price, long BasePrice, PlayerContract? Contract, IReadOnlyDictionary<string, int> Info, IReadOnlyList<AttrView> Attributes,
    IReadOnlyList<PlayerGame> Games, IReadOnlyList<TransferView> Transfers);
public sealed record TransferRow(Guid PlayerId, string Player, Position Position, Guid? From, Guid? To, long Fee, TransferKind Kind, DateTimeOffset At);
/// <param name="Change">Giá hiện tại so với giá gốc đầu mùa: 0,12 = +12%.</param>
public sealed record PriceRow(Guid PlayerId, string Name, Position Position, Guid ClubId, int Overall, long Price, double Change);
public sealed record ClubMoney(Guid ClubId, long SquadValue, int Squad, long Payroll, long Balance);
public sealed record MarketOverview(IReadOnlyList<TransferRow> Transfers, IReadOnlyList<PriceRow> Top, IReadOnlyList<PriceRow> Risers, IReadOnlyList<PriceRow> Fallers,
    IReadOnlyList<ClubMoney> Clubs, IReadOnlyDictionary<Guid, string> Names);
/// <param name="Final">true = vô địch; false = đang dẫn đầu (mùa đang chạy).</param>
public sealed record HonourTitle(int SeasonNumber, string Season, string Competition, string? Logo, string Kind, Guid ClubId, bool Final);
public sealed record Cabinet(Guid ClubId, int Titles);
public sealed record RecordItem(string Label, string Value, string Detail, Guid? FixtureId = null, Guid? PlayerId = null);
public sealed record HonoursPage(IReadOnlyList<HonourTitle> Titles, IReadOnlyList<Cabinet> Cabinet, IReadOnlyList<Leader> Scorers, IReadOnlyList<RecordItem> Records,
    IReadOnlyDictionary<Guid, string> Clubs, IReadOnlyDictionary<Guid, string> Logos);
/// <param name="Result">'W' thắng, 'D' hoà, 'L' thua.</param>
public sealed record FormItem(Guid FixtureId, char Result, string Score, Guid OpponentId);
/// <summary>Phong độ 1 CLB trong mùa (mọi giải): 5 trận gần nhất cũ → mới, cầu thủ nổi bật, giá trị đội hình; <see cref="Rank"/> = hạng trong giải của trận.</summary>
public sealed record TeamForm(Guid ClubId, IReadOnlyList<FormItem> Last, int Played, int Won, int Drawn, int Lost, int GoalsFor, int GoalsAgainst,
    Leader? TopScorer, Leader? BestRated, long SquadValue, double StarterOvr, int? Rank, int? Points);
public sealed record H2HRow(Guid FixtureId, string Season, Guid Home, Guid Away, int HomeGoals, int AwayGoals);
public sealed record MatchPreview(TeamForm Home, TeamForm Away, IReadOnlyList<H2HRow> H2H);
/// <summary>Thanh tỉ số đầu trang: các trận của khung đang diễn ra (hết khung → khung vừa chốt).</summary>
public sealed record TickerData(SeasonInfo Season, int Matchday, string Status, IReadOnlyList<FixtureView> Fixtures, IReadOnlyDictionary<Guid, string> Short,
    IReadOnlyDictionary<Guid, string> Editions);

public static class Insight
{
    /// <summary>Thứ tự "hay nhất" (xuất sắc trận/vòng, đội hình tiêu biểu): điểm → bàn → sút trúng → sút → đá chính.</summary>
    public static IOrderedEnumerable<PlayerLine> ByBest(this IEnumerable<PlayerLine> xs) =>
        xs.OrderByDescending(x => x.Rating ?? 0).ThenByDescending(x => x.Goals).ThenByDescending(x => x.ShotsOnTarget).ThenByDescending(x => x.Shots)
            .ThenByDescending(x => x.Starter);

    /// <summary>Cầu thủ xuất sắc mỗi trận (chỉ trận có điểm).</summary>
    public static Dictionary<Guid, PlayerLine> Motm(IEnumerable<PlayerLine> lines) =>
        lines.Where(l => l.Rating is not null).GroupBy(l => l.FixtureId).ToDictionary(g => g.Key, g => g.ByBest().First());

    /// <summary>Đội hình tiêu biểu 4-3-3 theo vị trí đăng ký; thiếu người ở 1 tuyến → lấy người hay nhất còn lại (trừ thủ môn).</summary>
    public static List<PlayerLine> TeamOf(IEnumerable<PlayerLine> lines)
    {
        var pool = lines.Where(l => l.Rating is not null).ByBest().DistinctBy(l => l.PlayerId).ToList();
        var team = new List<PlayerLine>();
        foreach (var (g, n) in new[] { (PositionGroup.Goalkeeper, 1), (PositionGroup.Defender, 4), (PositionGroup.Midfielder, 3), (PositionGroup.Forward, 3) })
            team.AddRange(pool.Where(l => l.Position.Group() == g).Take(n));
        team.AddRange(pool.Except(team).Where(l => l.Position.Group() != PositionGroup.Goalkeeper).Take(11 - team.Count));
        return team;
    }

    /// <summary>Cầu thủ đã ra sân của trang trận dưới dạng <see cref="PlayerLine"/> (bàn theo tên người ghi nếu trận có).</summary>
    public static List<PlayerLine> Lines(MatchPage m)
    {
        var f = m.Fixture;
        var known = f.Goals.Any(g => g.ScorerId is not null);
        return [.. m.Players.Select(p => new PlayerLine(f.Id, f.Matchday, f.EditionId, p.Stats.ClubId, p.Stats.ClubId == f.Home ? f.Away : f.Home, p.Stats.PlayerId,
            p.Name, p.Position, p.Stats.Slot, p.Stats.Rating, known ? f.Goals.Count(g => g.ScorerId == p.Stats.PlayerId) : p.Stats.Goals ?? 0,
            p.Stats.Shots ?? 0, p.Stats.ShotsOnTarget ?? 0, p.Stats.InMinute, p.Stats.OutMinute))];
    }

    /// <summary>Đội thắng (hoà có loạt penalty → đội thắng penalty); null = hoà hoặc chưa đá.</summary>
    public static Guid? Winner(FixtureView f) => (f.HomeGoals, f.AwayGoals, f.HomePenalties, f.AwayPenalties) switch
    {
        ({ } h, { } a, _, _) when h > a => f.Home,
        ({ } h, { } a, _, _) when h < a => f.Away,
        (_, _, { } ph, { } pa) when ph != pa => ph > pa ? f.Home : f.Away,
        _ => null,
    };

    /// <summary>Màu nhận diện của giải (lớp CSS <c>t-*</c>): theo nước, cúp châu Âu theo hạng.</summary>
    public static string Tone(EditionView? e) => e switch
    {
        null => "",
        { Type: CompetitionType.ContinentalCup } => $"t-c{e.Tier}",
        _ => $"t-{e.Country?.ToLowerInvariant()}",
    };

    /// <summary>Tên chỉ số PES6 → (nhóm, nhãn). Không có trong bảng = thông tin (tuổi, chiều cao…) hoặc thang 1–8.</summary>
    public static readonly IReadOnlyList<(string Field, string Group, string Label)> Attributes =
    [
        ("ATTACK", "Tấn công", "Tấn công"), ("SHOT ACCURACY", "Tấn công", "Sút chính xác"), ("SHOT POWER", "Tấn công", "Lực sút"),
        ("SHOT TECHNIQUE", "Tấn công", "Kỹ thuật sút"), ("FREE KICK ACCURACY", "Tấn công", "Đá phạt"), ("SWERVE", "Tấn công", "Độ xoáy"),
        ("HEADING", "Tấn công", "Đánh đầu"),
        ("TECHNIQUE", "Kỹ thuật", "Kỹ thuật"), ("DRIBBLE ACCURACY", "Kỹ thuật", "Rê bóng"), ("DRIBBLE SPEED", "Kỹ thuật", "Tốc độ rê"),
        ("SHORT PASS ACCURACY", "Kỹ thuật", "Chuyền ngắn"), ("SHORT PASS SPEED", "Kỹ thuật", "Lực chuyền ngắn"),
        ("LONG PASS ACCURACY", "Kỹ thuật", "Chuyền dài"), ("LONG PASS SPEED", "Kỹ thuật", "Lực chuyền dài"),
        ("TOP SPEED", "Thể chất", "Tốc độ"), ("ACCELERATION", "Thể chất", "Tăng tốc"), ("STAMINA", "Thể chất", "Thể lực"),
        ("BALANCE", "Thể chất", "Thăng bằng"), ("JUMP", "Thể chất", "Bật nhảy"), ("AGILITY", "Thể chất", "Khéo léo"), ("RESPONSE", "Thể chất", "Phản xạ"),
        ("DEFENSE", "Phòng ngự & tinh thần", "Phòng ngự"), ("GOAL KEEPING", "Phòng ngự & tinh thần", "Bắt bóng"),
        ("AGGRESSION", "Phòng ngự & tinh thần", "Quyết liệt"), ("MENTALITY", "Phòng ngự & tinh thần", "Tinh thần"), ("TEAM WORK", "Phòng ngự & tinh thần", "Đồng đội"),
        ("CONSISTENCY", "Thang 1–8", "Ổn định"), ("CONDITION / FITNESS", "Thang 1–8", "Thể trạng"),
        ("WEAK FOOT ACCURACY", "Thang 1–8", "Chân phụ – chính xác"), ("WEAK FOOT FREQUENCY", "Thang 1–8", "Chân phụ – tần suất"),
    ];
}

public sealed partial class LeagueQueries
{
    /// <summary>Dòng cầu thủ-trận của các trận đã đá trong mùa, lọc tuỳ chọn.</summary>
    private static async Task<List<PlayerLine>> LinesAsync(ClubsDbContext db, Guid seasonId, int? matchday = null, Guid? editionId = null,
        List<Guid>? fixtureIds = null, Guid? playerId = null, Guid? clubId = null)
    {
        var q = from r in db.MatchRatings.AsNoTracking()
                join f in db.Fixtures on r.FixtureId equals f.Id
                join m in db.Matchdays on f.MatchdayId equals m.Id
                join p in db.Players on r.PlayerId equals p.Id
                where m.SeasonId == seasonId && f.Status == FixtureStatus.Played
                select new { r, m.Number, f.CompetitionEditionId, f.HomeClubId, f.AwayClubId, p.Name, p.Position };
        if (matchday is { } n) q = q.Where(x => x.Number == n);
        if (editionId is { } e) q = q.Where(x => x.CompetitionEditionId == e);
        if (fixtureIds is not null) q = q.Where(x => fixtureIds.Contains(x.r.FixtureId));
        if (playerId is { } pid) q = q.Where(x => x.r.PlayerId == pid);
        if (clubId is { } cid) q = q.Where(x => x.r.ClubId == cid);
        var rows = await q.ToListAsync();
        var fids = rows.Select(x => x.r.FixtureId).Distinct().ToList();
        var scored = await db.Goals.Where(g => fids.Contains(g.FixtureId) && g.ScorerPlayerId != null)
            .Select(g => new { g.FixtureId, Scorer = g.ScorerPlayerId!.Value }).ToListAsync();
        var known = scored.Select(g => g.FixtureId).ToHashSet();
        var goals = scored.GroupBy(g => (g.FixtureId, g.Scorer)).ToDictionary(g => g.Key, g => g.Count());
        return [.. rows.Select(x => new PlayerLine(x.r.FixtureId, x.Number, x.CompetitionEditionId, x.r.ClubId, x.r.ClubId == x.HomeClubId ? x.AwayClubId : x.HomeClubId,
            x.r.PlayerId, x.Name, x.Position, x.r.Slot, x.r.Rating,
            known.Contains(x.r.FixtureId) ? goals.GetValueOrDefault((x.r.FixtureId, x.r.PlayerId)) : x.r.Goals ?? 0,
            x.r.Shots ?? 0, x.r.ShotsOnTarget ?? 0, x.r.InMinute, x.r.OutMinute))];
    }

    /// <summary>Trung tâm thống kê <c>/thong-ke</c>: bảng cầu thủ + bảng CLB của mùa, lọc theo giải.</summary>
    public async Task<StatsPage> StatsAsync(SeasonInfo season, Guid? editionId)
    {
        var fixtures = (await FixturesAsync(season.Id, editionId: editionId)).Where(f => f.Status == FixtureStatus.Played).ToList();
        await using var db = await dbs.CreateDbContextAsync();
        var lines = await LinesAsync(db, season.Id, editionId: editionId);
        var fids = fixtures.Select(f => f.Id).ToList();
        var teamStats = await db.MatchTeamStats.AsNoTracking().Where(t => fids.Contains(t.FixtureId)).ToListAsync();
        var byId = fixtures.ToDictionary(f => f.Id);

        var apps = lines.GroupBy(l => l.PlayerId).ToDictionary(g => g.Key, g => g.OrderBy(l => l.Matchday).ToList());
        var maxApps = apps.Count == 0 ? 0 : apps.Values.Max(v => v.Count);
        var minApps = Math.Max(2, (int)Math.Ceiling(maxApps * 0.4));
        Leader P(List<PlayerLine> ls, string value, string? note) => new(ls[0].PlayerId, ls[0].Name, ls[^1].ClubId, value, note);
        static decimal Avg(List<PlayerLine> ls) => ls.Where(l => l.Rating is not null).Select(l => l.Rating!.Value).DefaultIfEmpty().Average();
        static int Rated(List<PlayerLine> ls) => ls.Count(l => l.Rating is not null);

        var scorers = fixtures.SelectMany(f => f.Goals).Where(g => g.ScorerId is not null).GroupBy(g => g.ScorerId!.Value)
            .Select(g => (Id: g.Key, Name: g.First().Scorer!, Club: g.GroupBy(x => x.ClubId).MaxBy(c => c.Count())!.Key, Goals: g.Count()))
            .OrderByDescending(x => x.Goals).ThenBy(x => x.Name).Take(10)
            .Select(x => new Leader(x.Id, x.Name, x.Club, $"{x.Goals}", apps.TryGetValue(x.Id, out var a) ? $"{a.Count} trận" : null)).ToList();
        var qualified = apps.Values.Where(ls => Rated(ls) >= minApps).ToList();
        var ratings = qualified.OrderByDescending(Avg).ThenByDescending(Rated).Take(10).Select(ls => P(ls, $"{Avg(ls):0.00}", $"{Rated(ls)} trận")).ToList();
        int CleanSheets(List<PlayerLine> ls) => ls.Count(l => l.Starter && byId.TryGetValue(l.FixtureId, out var f) && (l.ClubId == f.Home ? f.AwayGoals : f.HomeGoals) == 0);
        var keepers = qualified.Where(ls => ls[0].Position == Position.GK).OrderByDescending(Avg).ThenByDescending(CleanSheets).Take(10)
            .Select(ls => P(ls, $"{Avg(ls):0.00}", $"{CleanSheets(ls)} trận sạch lưới")).ToList();
        var motm = Insight.Motm(lines).Values.GroupBy(l => l.PlayerId).Select(g => (Lines: apps[g.Key], N: g.Count()))
            .OrderByDescending(x => x.N).ThenByDescending(x => Avg(x.Lines)).Take(10).Select(x => P(x.Lines, $"{x.N}", $"TB {Avg(x.Lines):0.00}")).ToList();
        var shooters = apps.Values.Where(ls => ls.Sum(l => l.ShotsOnTarget) > 0).OrderByDescending(ls => ls.Sum(l => l.ShotsOnTarget)).ThenBy(ls => ls.Sum(l => l.Shots)).Take(10)
            .Select(ls => P(ls, $"{ls.Sum(l => l.ShotsOnTarget)}", $"{ls.Sum(l => l.Shots)} cú sút")).ToList();
        var ironmen = apps.Values.OrderByDescending(ls => ls.Count).ThenByDescending(ls => ls.Count(l => l.Starter)).ThenByDescending(Avg).Take(10)
            .Select(ls => P(ls, $"{ls.Count}", $"{ls.Count(l => l.Starter)} đá chính")).ToList();

        var clubs = fixtures.SelectMany(f => new[] { (Club: f.Home, For: f.HomeGoals ?? 0, Against: f.AwayGoals ?? 0), (Club: f.Away, For: f.AwayGoals ?? 0, Against: f.HomeGoals ?? 0) })
            .GroupBy(x => x.Club).Select(g => (Id: g.Key, P: g.Count(), F: g.Sum(x => x.For), A: g.Sum(x => x.Against), Cs: g.Count(x => x.Against == 0))).ToList();
        Leader T(Guid id, string value, string? note) => new(id, season.Clubs.GetValueOrDefault(id, "?"), id, value, note);
        var attack = clubs.OrderByDescending(c => c.F).ThenBy(c => c.P).Take(10).Select(c => T(c.Id, $"{c.F}", $"{(double)c.F / c.P:0.0}/trận")).ToList();
        var defence = clubs.OrderBy(c => (double)c.A / c.P).ThenByDescending(c => c.P).Take(10).Select(c => T(c.Id, $"{c.A}", $"{c.P} trận")).ToList();
        var clean = clubs.Where(c => c.Cs > 0).OrderByDescending(c => c.Cs).ThenBy(c => c.P).Take(10).Select(c => T(c.Id, $"{c.Cs}", $"{c.P} trận")).ToList();
        var ts = teamStats.GroupBy(t => t.ClubId).Select(g => (Id: g.Key, N: g.Count(), Pos: g.Average(t => t.PossessionPct),
            Pass: g.Sum(t => t.Passes) == 0 ? 0 : 100.0 * g.Sum(t => t.PassesCompleted) / g.Sum(t => t.Passes), Shots: g.Average(t => t.Shots))).ToList();
        var possession = ts.OrderByDescending(t => t.Pos).Take(10).Select(t => T(t.Id, $"{t.Pos:0}%", $"{t.N} trận")).ToList();
        var passing = ts.OrderByDescending(t => t.Pass).Take(10).Select(t => T(t.Id, $"{t.Pass:0}%", $"{t.N} trận")).ToList();
        var shots = ts.OrderByDescending(t => t.Shots).Take(10).Select(t => T(t.Id, $"{t.Shots:0.0}", $"{t.N} trận")).ToList();

        return new StatsPage(
        [
            new("Vua phá lưới", "Bàn thắng có tên người ghi", true, scorers),
            new("Điểm trung bình", $"Tối thiểu {minApps} trận có điểm", true, ratings),
            new("Xuất sắc nhất trận", "Số lần điểm cao nhất trận", true, motm),
            new("Sút trúng đích", "Tổng cú sút trúng khung thành", true, shooters),
            new("Thủ môn", $"Điểm trung bình, tối thiểu {minApps} trận", true, keepers),
            new("Ra sân nhiều nhất", "Số trận có tên trong bảng điểm", true, ironmen),
        ],
        [
            new("Hàng công", "Tổng bàn thắng", false, attack),
            new("Hàng thủ", "Bàn thua, ít nhất theo trung bình/trận", false, defence),
            new("Giữ sạch lưới", "Số trận không thủng lưới", false, clean),
            new("Kiểm soát bóng", "Trung bình các trận có số liệu", false, possession),
            new("Chuyền chính xác", "Tỉ lệ chuyền thành công", false, passing),
            new("Dứt điểm", "Số cú sút mỗi trận", false, shots),
        ], fixtures.Count, fixtures.Sum(f => (f.HomeGoals ?? 0) + (f.AwayGoals ?? 0)), minApps);
    }

    /// <summary>Tổng kết 1 khung ngày (mọi giải): con số, xuất sắc nhất, đội hình tiêu biểu, kết quả bất ngờ; null = mùa chưa có trận đá xong.</summary>
    public async Task<RoundRecap?> RoundRecapAsync(SeasonInfo season, int? number)
    {
        var all = await FixturesAsync(season.Id);
        var rounds = all.Where(f => f.Status == FixtureStatus.Played).Select(f => f.Matchday).Distinct().Order().ToList();
        if (rounds.Count == 0) return null;
        var n = number is { } x && rounds.Contains(x) ? x : rounds[^1];
        var fixtures = all.Where(f => f.Matchday == n).ToList();
        var played = fixtures.Where(f => f.Status == FixtureStatus.Played && f.HomeGoals is not null && f.AwayGoals is not null).ToList();
        await using var db = await dbs.CreateDbContextAsync();
        var lines = await LinesAsync(db, season.Id, matchday: n);
        var ids = lines.Where(l => l.Starter).Select(l => l.PlayerId).Distinct().ToList();
        var ovr = await db.PlayerPrices.Where(p => p.SeasonId == season.Id && ids.Contains(p.PlayerId)).ToDictionaryAsync(p => p.PlayerId, p => p.Overall);
        var strength = lines.Where(l => l.Starter && ovr.ContainsKey(l.PlayerId)).GroupBy(l => (l.FixtureId, l.ClubId))
            .ToDictionary(g => g.Key, g => g.Average(l => (double)ovr[l.PlayerId]));
        var upsets = new List<Upset>();
        foreach (var f in played)
        {
            if (Insight.Winner(f) is not { } w) continue;
            var loser = w == f.Home ? f.Away : f.Home;
            if (strength.TryGetValue((f.Id, w), out var sw) && strength.TryGetValue((f.Id, loser), out var sl) && sl - sw >= 2) upsets.Add(new(f, w, sw, sl));
        }
        static int Diff(FixtureView f) => Math.Abs(f.HomeGoals!.Value - f.AwayGoals!.Value);
        static int Total(FixtureView f) => f.HomeGoals!.Value + f.AwayGoals!.Value;
        return new RoundRecap(n, rounds, fixtures, Insight.TeamOf(lines), lines.Where(l => l.Rating is not null).ByBest().FirstOrDefault(),
            [.. upsets.OrderByDescending(u => u.LoserOvr - u.WinnerOvr).Take(5)],
            played.Where(f => Diff(f) > 0).OrderByDescending(Diff).ThenByDescending(Total).FirstOrDefault(),
            played.Where(f => Total(f) > 0).OrderByDescending(Total).ThenByDescending(Diff).FirstOrDefault(),
            played.Count, played.Sum(Total), played.Count(f => f.HomeGoals > f.AwayGoals), played.Count(f => f.HomeGoals == f.AwayGoals),
            played.Count(f => f.HomeGoals < f.AwayGoals));
    }

    /// <summary>Hồ sơ cầu thủ trong mùa <paramref name="season"/>; null = không có cầu thủ.</summary>
    public async Task<PlayerProfile?> PlayerAsync(Guid id, SeasonInfo season)
    {
        await using var db = await dbs.CreateDbContextAsync();
        var p = await db.Players.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        if (p is null) return null;
        var m = await db.SquadMemberships.AsNoTracking().Where(x => x.PlayerId == id && x.LeftAt == null).FirstOrDefaultAsync();
        var club = m is null ? null : await db.Clubs.Where(c => c.Id == m.ClubId).Select(c => new { c.Name, c.LogoPath }).SingleOrDefaultAsync();
        var price = await db.PlayerPrices.AsNoTracking().Where(x => x.PlayerId == id && x.SeasonId == season.Id).SingleOrDefaultAsync()
                    ?? await db.PlayerPrices.AsNoTracking().Where(x => x.PlayerId == id).OrderByDescending(x => x.UpdatedAt).FirstOrDefaultAsync();
        var contract = await db.Contracts.AsNoTracking().Where(c => c.PlayerId == id && c.Status == ContractStatus.Active).FirstOrDefaultAsync();
        var snap = await db.PlayerAttributes.AsNoTracking().Where(a => a.PlayerId == id && a.SeasonId == season.Id).SingleOrDefaultAsync()
                   ?? await db.PlayerAttributes.AsNoTracking().Where(a => a.PlayerId == id).OrderByDescending(a => a.CapturedAt).FirstOrDefaultAsync();
        var deltas = snap is null ? new Dictionary<string, int>()
            : await db.AttributeChanges.Where(c => c.PlayerId == id && c.SeasonId == snap.SeasonId).GroupBy(c => c.Field)
                .Select(g => new { g.Key, D = g.Sum(c => c.Delta) }).ToDictionaryAsync(x => x.Key, x => x.D);
        var values = snap?.Values ?? new Dictionary<string, int>();
        var attrs = Insight.Attributes.Where(a => values.ContainsKey(a.Field))
            .Select(a => new AttrView(a.Group, a.Label, values[a.Field] + deltas.GetValueOrDefault(a.Field), deltas.GetValueOrDefault(a.Field), a.Group == "Thang 1–8" ? 8 : 99, a.Field))
            .ToList();
        var info = new[] { "AGE", "HEIGHT", "WEIGHT" }.Where(values.ContainsKey).ToDictionary(k => k, k => values[k]);

        var mine = await LinesAsync(db, season.Id, playerId: id);
        var fids = mine.Select(l => l.FixtureId).Distinct().ToList();
        var motm = Insight.Motm(await LinesAsync(db, season.Id, fixtureIds: fids));
        var transfers = await db.Transfers.AsNoTracking().Where(t => t.PlayerId == id && t.Status == TransferStatus.Completed)
            .OrderByDescending(t => t.CompletedAt).ToListAsync();
        var cids = transfers.SelectMany(t => new[] { t.FromClubId, t.ToClubId }).OfType<Guid>().Distinct().ToList();
        var names = await db.Clubs.Where(c => cids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name);
        var fx = (await FixturesAsync(season.Id)).Where(f => fids.Contains(f.Id)).ToDictionary(f => f.Id);
        return new PlayerProfile(p.Id, p.Name, p.Position, p.BirthYear, p.Nationality, m?.ClubId, club?.Name, club?.LogoPath, m?.ShirtNumber, m is { SlotOrder: < 11 },
            price?.Overall ?? 0, price?.Price ?? 0, price?.BasePrice ?? 0, contract, info, attrs,
            [.. mine.OrderByDescending(l => l.Matchday).Select(l => new PlayerGame(l, fx[l.FixtureId], motm.TryGetValue(l.FixtureId, out var b) && b.PlayerId == id))],
            [.. transfers.Select(t => new TransferView(t.CompletedAt ?? t.RequestedAt, t.FromClubId is { } f ? names.GetValueOrDefault(f) : null,
                t.ToClubId is { } to ? names.GetValueOrDefault(to) : null, t.Fee, t.Kind))]);
    }

    /// <summary>Chiều cao / cân nặng TB của tập cầu thủ (snapshot mùa; chỉ giá trị &gt; 0).</summary>
    public async Task<PhysiqueAvg> PhysiqueAvgAsync(Guid seasonId, IReadOnlyCollection<Guid> playerIds)
    {
        if (playerIds.Count == 0) return new(null, null);
        await using var db = await dbs.CreateDbContextAsync();
        var rows = await db.PlayerAttributes.AsNoTracking()
            .Where(a => a.SeasonId == seasonId && playerIds.Contains(a.PlayerId)).ToListAsync();
        return PhysiqueAvg.Of(rows.Select(a => (
            a.Values.TryGetValue("HEIGHT", out var h) ? h : (int?)null,
            a.Values.TryGetValue("WEIGHT", out var w) ? w : (int?)null)));
    }

    /// <summary>Chiều cao / cân nặng TB theo CLB (đội hình hiện tại, snapshot mùa).</summary>
    public async Task<Dictionary<Guid, PhysiqueAvg>> ClubsPhysiqueAsync(Guid seasonId, IReadOnlyCollection<Guid> clubIds)
    {
        if (clubIds.Count == 0) return [];
        await using var db = await dbs.CreateDbContextAsync();
        var members = await db.SquadMemberships.AsNoTracking()
            .Where(m => m.LeftAt == null && clubIds.Contains(m.ClubId))
            .Select(m => new { m.ClubId, m.PlayerId }).ToListAsync();
        var ids = members.Select(m => m.PlayerId).Distinct().ToList();
        var attrs = ids.Count == 0 ? [] : await db.PlayerAttributes.AsNoTracking()
            .Where(a => a.SeasonId == seasonId && ids.Contains(a.PlayerId)).ToListAsync();
        var byPlayer = attrs.ToDictionary(a => a.PlayerId, a => (
            a.Values.TryGetValue("HEIGHT", out var h) ? h : (int?)null,
            a.Values.TryGetValue("WEIGHT", out var w) ? w : (int?)null));
        return clubIds.ToDictionary(c => c, c => PhysiqueAvg.Of(
            members.Where(m => m.ClubId == c).Select(m => byPlayer.GetValueOrDefault(m.PlayerId))));
    }

    /// <summary>Số liệu thêm cho bảng đội hình trang CLB (<paramref name="ids"/> = cầu thủ đang trong đội); trận tính theo CLB này trong mùa.</summary>
    public async Task<Dictionary<Guid, SquadStat>> SquadStatsAsync(SeasonInfo season, Guid clubId, IReadOnlyCollection<Guid> ids)
    {
        await using var db = await dbs.CreateDbContextAsync();
        var age = (await db.PlayerAttributes.AsNoTracking().Where(a => ids.Contains(a.PlayerId) && a.SeasonId == season.Id).ToListAsync())
            .ToDictionary(a => a.PlayerId, a => a.Values.TryGetValue("AGE", out var v) ? v : (int?)null);
        var lines = (await LinesAsync(db, season.Id, clubId: clubId)).Where(l => ids.Contains(l.PlayerId)).ToList();
        var motm = Insight.Motm(await LinesAsync(db, season.Id, fixtureIds: [.. lines.Select(l => l.FixtureId).Distinct()]));
        var by = lines.ToLookup(l => l.PlayerId);
        return ids.ToDictionary(id => id, id =>
        {
            var mine = by[id].ToList();
            var rated = mine.Where(l => l.Rating is not null).ToList();
            return new SquadStat(age.GetValueOrDefault(id), mine.Count, mine.Sum(l => l.Goals),
                rated.Count == 0 ? null : Math.Round(rated.Average(l => l.Rating!.Value), 2), mine.Count(l => motm.TryGetValue(l.FixtureId, out var b) && b.PlayerId == id));
        });
    }

    /// <summary>Vụ chuyển nhượng đã hoàn tất trong mùa, mới nhất trước (trang Thị trường, tab Giao dịch trang Chuyển nhượng).</summary>
    public async Task<List<TransferRow>> TransfersAsync(Guid seasonId, int take)
    {
        await using var db = await dbs.CreateDbContextAsync();
        return await (from t in db.Transfers.AsNoTracking()
                      join w in db.TransferWindows on t.TransferWindowId equals w.Id
                      join p in db.Players on t.PlayerId equals p.Id
                      where w.SeasonId == seasonId && t.Status == TransferStatus.Completed
                      orderby t.CompletedAt descending
                      select new TransferRow(p.Id, p.Name, p.Position, t.FromClubId, t.ToClubId, t.Fee, t.Kind, t.CompletedAt ?? t.RequestedAt)).Take(take).ToListAsync();
    }

    /// <summary>Chuyên mục Thị trường <c>/thi-truong</c>: chuyển nhượng gần đây, cầu thủ đắt giá, biến động giá, tài chính các CLB (chỉ cầu thủ đang có CLB).</summary>
    public async Task<MarketOverview> MarketOverviewAsync(SeasonInfo season)
    {
        var transfers = await TransfersAsync(season.Id, 15);
        await using var db = await dbs.CreateDbContextAsync();
        var squad = (await (from m in db.SquadMemberships
                            where m.LeftAt == null
                            join pr in db.PlayerPrices.Where(x => x.SeasonId == season.Id) on m.PlayerId equals pr.PlayerId
                            join p in db.Players on m.PlayerId equals p.Id
                            select new { p.Id, p.Name, p.Position, m.ClubId, pr.Overall, pr.Price, pr.BasePrice }).ToListAsync())
            .Where(s => season.Clubs.ContainsKey(s.ClubId))
            .Select(s => new PriceRow(s.Id, s.Name, s.Position, s.ClubId, s.Overall, s.Price, s.BasePrice == 0 ? 0 : (double)s.Price / s.BasePrice - 1)).ToList();
        var payroll = await db.Contracts.Where(c => c.Status == ContractStatus.Active).GroupBy(c => c.ClubId)
            .Select(g => new { g.Key, W = g.Sum(c => c.Wage ?? 0) }).ToDictionaryAsync(x => x.Key, x => x.W);
        var balance = await db.Ledger.GroupBy(e => e.ClubId).Select(g => new { g.Key, B = g.Sum(e => e.Amount) }).ToDictionaryAsync(x => x.Key, x => x.B);
        var names = await db.Clubs.ToDictionaryAsync(c => c.Id, c => c.Name);
        var money = squad.GroupBy(s => s.ClubId).Select(g => new ClubMoney(g.Key, g.Sum(s => s.Price), g.Count(), payroll.GetValueOrDefault(g.Key), balance.GetValueOrDefault(g.Key)))
            .OrderByDescending(c => c.SquadValue).ToList();
        return new MarketOverview(transfers, [.. squad.OrderByDescending(s => s.Price).ThenByDescending(s => s.Overall).Take(10)],
            [.. squad.Where(s => s.Change > 0).OrderByDescending(s => s.Change).ThenByDescending(s => s.Price).Take(8)],
            [.. squad.Where(s => s.Change < 0).OrderBy(s => s.Change).ThenByDescending(s => s.Price).Take(8)], money, names);
    }

    /// <summary>Vinh danh <c>/vinh-danh</c>: nhà vô địch từng mùa (mùa đang chạy: đội dẫn đầu), tủ cúp, vua phá lưới mọi thời đại, kỷ lục.</summary>
    public async Task<HonoursPage> HonoursAsync(IOptionFileStore store, TimeProvider clock)
    {
        await using var db = await dbs.CreateDbContextAsync();
        var seasons = await db.Seasons.AsNoTracking().Where(s => s.Status != SeasonStatus.Planned).OrderByDescending(s => s.CreatedAt).ToListAsync();
        var clubs = await db.Clubs.AsNoTracking().Select(c => new { c.Id, c.Name, c.LogoPath }).ToListAsync();
        var titles = new List<HonourTitle>();
        foreach (var s in seasons)
        {
            var editions = await EditionsAsync(s.Id);
            if (editions.Count == 0)
            {
                if ((await StandingsAsync(s.Id, store, clock)).FirstOrDefault() is { Played: > 0 } top)
                    titles.Add(new(s.Number, s.Name, "Vô địch mùa", null, "", top.Club, s.Status == SeasonStatus.Finished));
                continue;
            }
            foreach (var x in await SummariesAsync(s.Id, editions, store, clock))
            {
                if (x.Champion is { } c) titles.Add(new(s.Number, s.Name, x.Edition.Name, x.Edition.Logo, Fmt.Kind(x.Edition), c, true));
                else if (s.Status == SeasonStatus.Active && x.Edition.Type != CompetitionType.DomesticCup && x.Leader is { Played: > 0 } l)
                    titles.Add(new(s.Number, s.Name, x.Edition.Name, x.Edition.Logo, Fmt.Kind(x.Edition), l.Club, false));
            }
        }

        var fx = await (from f in db.Fixtures.AsNoTracking()
                        join m in db.Matchdays on f.MatchdayId equals m.Id
                        join s in db.Seasons on m.SeasonId equals s.Id
                        where f.Status == FixtureStatus.Played && f.HomeGoals != null && f.AwayGoals != null
                        select new { f.Id, f.HomeClubId, f.AwayClubId, H = f.HomeGoals!.Value, A = f.AwayGoals!.Value, m.Number, SeasonId = s.Id, Season = s.Name })
                       .ToListAsync();
        var name = clubs.ToDictionary(c => c.Id, c => c.Name);
        var records = new List<RecordItem>();
        string At(Guid id)
        {
            var x = fx.Single(f => f.Id == id);
            return $"{x.Season} · vòng {x.Number}";
        }
        if (fx.Where(f => f.H != f.A).OrderByDescending(f => Math.Abs(f.H - f.A)).ThenByDescending(f => f.H + f.A).FirstOrDefault() is { } big)
            records.Add(new("Thắng đậm nhất", $"{big.H}–{big.A}", $"{name[big.HomeClubId]} – {name[big.AwayClubId]} · {At(big.Id)}", big.Id));
        if (fx.Where(f => f.H + f.A > 0).OrderByDescending(f => f.H + f.A).ThenByDescending(f => Math.Abs(f.H - f.A)).FirstOrDefault() is { } wild)
            records.Add(new("Trận nhiều bàn nhất", $"{wild.H + wild.A} bàn", $"{name[wild.HomeClubId]} {wild.H}–{wild.A} {name[wild.AwayClubId]} · {At(wild.Id)}", wild.Id));
        var best = await (from r in db.MatchRatings join p in db.Players on r.PlayerId equals p.Id
                          where r.Rating != null orderby r.Rating descending, r.Goals descending, r.ShotsOnTarget descending
                          select new { r.FixtureId, r.ClubId, p.Id, p.Name, r.Rating }).FirstOrDefaultAsync();
        if (best is not null && fx.Any(f => f.Id == best.FixtureId))
            records.Add(new("Điểm cao nhất 1 trận", $"{best.Rating:0.0}", $"{best.Name} ({name[best.ClubId]}) · {At(best.FixtureId)}", best.FixtureId, best.Id));
        var goals = await (from g in db.Goals join p in db.Players on g.ScorerPlayerId equals p.Id
                           select new { g.FixtureId, g.ClubId, ScorerId = p.Id, p.Name }).ToListAsync();
        if (goals.GroupBy(g => (g.FixtureId, g.ScorerId)).OrderByDescending(g => g.Count()).FirstOrDefault() is { } hat && hat.Count() > 1 && fx.Any(f => f.Id == hat.Key.FixtureId))
            records.Add(new("Ghi nhiều bàn nhất 1 trận", $"{hat.Count()} bàn", $"{hat.First().Name} ({name[hat.First().ClubId]}) · {At(hat.Key.FixtureId)}",
                hat.Key.FixtureId, hat.Key.ScorerId));
        // Chuỗi trong 1 mùa, mọi giải, theo thứ tự khung ngày.
        var runs = fx.SelectMany(f => new[] { (Club: f.HomeClubId, f.SeasonId, f.Season, f.Number, For: f.H, Against: f.A), (Club: f.AwayClubId, f.SeasonId, f.Season, f.Number, For: f.A, Against: f.H) })
            .GroupBy(x => (x.Club, x.SeasonId)).Select(g =>
            {
                int win = 0, unbeaten = 0, bestWin = 0, bestUnbeaten = 0;
                foreach (var x in g.OrderBy(x => x.Number))
                {
                    win = x.For > x.Against ? win + 1 : 0;
                    unbeaten = x.For >= x.Against ? unbeaten + 1 : 0;
                    bestWin = Math.Max(bestWin, win);
                    bestUnbeaten = Math.Max(bestUnbeaten, unbeaten);
                }
                return (g.Key.Club, Season: g.First().Season, Win: bestWin, Unbeaten: bestUnbeaten);
            }).ToList();
        if (runs.OrderByDescending(r => r.Win).FirstOrDefault() is { Win: > 1 } w)
            records.Add(new("Chuỗi thắng dài nhất", $"{w.Win} trận", $"{name[w.Club]} · {w.Season}"));
        if (runs.OrderByDescending(r => r.Unbeaten).FirstOrDefault() is { Unbeaten: > 1 } u)
            records.Add(new("Chuỗi bất bại dài nhất", $"{u.Unbeaten} trận", $"{name[u.Club]} · {u.Season}"));

        var scorers = goals.Where(g => fx.Any(f => f.Id == g.FixtureId)).GroupBy(g => g.ScorerId)
            .Select(g => new Leader(g.Key, g.First().Name, g.GroupBy(x => x.ClubId).MaxBy(c => c.Count())!.Key, $"{g.Count()}"))
            .OrderByDescending(l => int.Parse(l.Value)).ThenBy(l => l.Name).Take(10).ToList();
        return new HonoursPage(titles, [.. titles.Where(t => t.Final).GroupBy(t => t.ClubId).Select(g => new Cabinet(g.Key, g.Count())).OrderByDescending(c => c.Titles)],
            scorers, records, name, clubs.Where(c => c.LogoPath != null).ToDictionary(c => c.Id, c => c.LogoPath!));
    }

    /// <summary>Phong độ 1 CLB trong mùa từ <paramref name="all"/> (mọi trận của mùa); <paramref name="table"/> = BXH giải của trận để lấy hạng.</summary>
    private static async Task<TeamForm> TeamFormAsync(ClubsDbContext db, SeasonInfo season, IReadOnlyList<FixtureView> all, Guid club, IReadOnlyList<StandingRow<Guid>> table)
    {
        var played = all.Where(f => f.Status == FixtureStatus.Played && f.HomeGoals is not null && (f.Home == club || f.Away == club)).OrderBy(f => f.Matchday).ToList();
        var last = played.TakeLast(5).Select(f =>
        {
            var (s, c) = f.Home == club ? (f.HomeGoals!.Value, f.AwayGoals!.Value) : (f.AwayGoals!.Value, f.HomeGoals!.Value);
            return new FormItem(f.Id, s > c ? 'W' : s == c ? 'D' : 'L', $"{s}–{c}", f.Home == club ? f.Away : f.Home);
        }).ToList();
        int For(FixtureView f) => f.Home == club ? f.HomeGoals!.Value : f.AwayGoals!.Value;
        int Against(FixtureView f) => f.Home == club ? f.AwayGoals!.Value : f.HomeGoals!.Value;
        var top = played.SelectMany(f => f.Goals).Where(g => g.ClubId == club && g.ScorerId is not null).GroupBy(g => g.ScorerId!.Value)
            .OrderByDescending(g => g.Count()).Select(g => new Leader(g.Key, g.First().Scorer!, club, $"{g.Count()}", "bàn")).FirstOrDefault();
        var lines = await LinesAsync(db, season.Id, clubId: club);
        var bestRated = lines.Where(l => l.Rating is not null).GroupBy(l => l.PlayerId).Where(g => g.Count() >= 2)
            .OrderByDescending(g => g.Average(l => l.Rating!.Value)).ThenByDescending(g => g.Count())
            .Select(g => new Leader(g.Key, g.First().Name, club, $"{g.Average(l => l.Rating!.Value):0.00}", "điểm TB")).FirstOrDefault();
        var squad = await (from m in db.SquadMemberships
                           where m.ClubId == club && m.LeftAt == null
                           join pr in db.PlayerPrices.Where(x => x.SeasonId == season.Id) on m.PlayerId equals pr.PlayerId
                           select new { m.SlotOrder, pr.Overall, pr.Price }).ToListAsync();
        var rank = table.Select((r, i) => (r, i)).FirstOrDefault(x => x.r.Club == club);
        return new TeamForm(club, last, played.Count, played.Count(f => For(f) > Against(f)), played.Count(f => For(f) == Against(f)), played.Count(f => For(f) < Against(f)),
            played.Sum(For), played.Sum(Against), top, bestRated, squad.Sum(s => s.Price),
            squad.Where(s => s.SlotOrder < 11).Select(s => (double)s.Overall).DefaultIfEmpty().Average(),
            rank.r is null ? (int?)null : rank.i + 1, rank.r?.Points);
    }

    /// <summary>Nhận định trước trận: phong độ, hạng, cầu thủ nổi bật, giá trị đội hình 2 đội; đối đầu mọi mùa (6 trận gần nhất).</summary>
    public async Task<MatchPreview> PreviewAsync(MatchPage page, IOptionFileStore store, TimeProvider clock)
    {
        var f = page.Fixture;
        var all = await FixturesAsync(page.Season.Id);
        IReadOnlyList<StandingRow<Guid>> table = page.Edition switch
        {
            null => await StandingsAsync(page.Season.Id, store, clock),
            { Type: CompetitionType.DomesticCup } => [],
            var e => await EditionStandingsAsync(e.Id, store, clock),
        };
        await using var db = await dbs.CreateDbContextAsync();
        var home = await TeamFormAsync(db, page.Season, all, f.Home, table);
        var away = await TeamFormAsync(db, page.Season, all, f.Away, table);
        var h2h = await (from x in db.Fixtures.AsNoTracking()
                         join m in db.Matchdays on x.MatchdayId equals m.Id
                         join s in db.Seasons on m.SeasonId equals s.Id
                         where x.Status == FixtureStatus.Played && x.Id != f.Id && x.HomeGoals != null && x.AwayGoals != null
                               && ((x.HomeClubId == f.Home && x.AwayClubId == f.Away) || (x.HomeClubId == f.Away && x.AwayClubId == f.Home))
                         orderby s.CreatedAt descending, m.Number descending
                         select new H2HRow(x.Id, s.Name, x.HomeClubId, x.AwayClubId, x.HomeGoals!.Value, x.AwayGoals!.Value)).Take(6).ToListAsync();
        return new MatchPreview(home, away, h2h);
    }

    /// <summary>Tóm tắt CLB ở đầu trang CLB: phong độ mùa + trận sắp tới gần nhất.</summary>
    public async Task<(TeamForm Form, FixtureView? Next)> ClubGlanceAsync(SeasonInfo season, Guid clubId)
    {
        var all = await FixturesAsync(season.Id);
        await using var db = await dbs.CreateDbContextAsync();
        var form = await TeamFormAsync(db, season, all, clubId, []);
        var next = all.Where(f => f.Status != FixtureStatus.Played && (f.Home == clubId || f.Away == clubId)).OrderBy(f => f.Matchday).FirstOrDefault();
        return (form, next);
    }

    /// <summary>Thanh tỉ số: khung đang diễn ra của mùa đang chạy, hết khung → khung gần nhất đã có trận đá xong; null = chưa có mùa/trận.</summary>
    public async Task<TickerData?> TickerAsync()
    {
        var season = await SeasonAsync();
        if (season is null) return null;
        await using var db = await dbs.CreateDbContextAsync();
        var md = await CurrentMatchdayAsync(season.Id)
                 ?? await db.Matchdays.AsNoTracking().Where(m => m.SeasonId == season.Id && m.Status == MatchdayStatus.Settled).OrderByDescending(m => m.Number).FirstOrDefaultAsync();
        if (md is null) return null;
        var fixtures = await FixturesAsync(season.Id, md.Id);
        if (fixtures.Count == 0) return null;
        var ids = season.Clubs.Keys.ToList();
        var shorts = await db.Clubs.Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.ShortName ?? c.Name);
        var editions = await EditionsAsync(season.Id);
        var order = editions.Select((e, i) => (e.Id, i)).ToDictionary(x => x.Id, x => x.i);
        return new TickerData(season, md.Number, Fmt.Status(md.Status, md.TransferWindowId is not null),
            [.. fixtures.OrderBy(f => f.EditionId is { } e ? order.GetValueOrDefault(e) : 0)], shorts, editions.ToDictionary(e => e.Id, e => e.Name));
    }
}
