using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Application;
using Pes17League.OptionFile;

namespace Pes17League.Web;

public sealed record NameLogo(string Name, string? Logo);
/// <summary>Cúp mùa này của CLB trong giải: cúp quốc gia cùng nước + cúp châu lục. <see cref="Entrants"/> = CLB của giải này có suất.</summary>
public sealed record CareerLeagueCup(string Name, string? Logo, CompetitionType Type, int Teams, CompetitionRules Rules, IReadOnlyList<NameLogo> Entrants);
/// <summary>Ngôi sao của giải: OVR cao nhất, mỗi tuyến (thủ môn/hậu vệ/tiền vệ/tiền đạo) tối đa 1 người.</summary>
public sealed record CareerLeaguePlayer(Guid Id, string Name, Position Position, int Ovr, long Price, NameLogo Club, int? Height = null, int? Weight = null);
/// <summary>1 mùa đã xong của giải: vô địch, vua phá lưới (bàn trong giải), CLB xuống hạng (= đá giải hạng dưới ở mùa kế).</summary>
public sealed record CareerLeagueSeason(int Number, string Season, NameLogo? Champion, string? Scorer, NameLogo? ScorerClub, int Goals, IReadOnlyList<NameLogo> Relegated);
/// <summary>Bảng chi tiết giải ở bước 1 career (PO 19/11 "chọn A": chỉ dữ liệu thật trong DB, lịch sử = các mùa đã đá trong career).</summary>
/// <param name="C1">Cúp châu lục hạng 1/2 của mùa (suất theo thứ hạng: <see cref="CompetitionRules.ContinentalPlaces"/>, <see cref="CompetitionRules.SecondaryContinentalPlaces"/>).</param>
public sealed record CareerLeagueInfo(string Name, string? Logo, string? Country, int Clubs, CompetitionRules Rules, NameLogo? Down, NameLogo? Up, NameLogo? C1, NameLogo? C2,
    IReadOnlyList<CareerLeagueCup> Cups, IReadOnlyList<CareerLeaguePlayer> Players, IReadOnlyList<CareerLeagueSeason> History)
{
    /// <summary>Số trận mỗi CLB: vòng tròn <see cref="CompetitionRules.Legs"/> lượt.</summary>
    public int Matches => (Clubs - 1) * Rules.Legs;
}

/// <summary>Cầu thủ trong bảng chi tiết CLB (bước 2 career). <see cref="Wage"/> = lương/trận (hợp đồng, chưa có thì giá × ‰ career như lúc bắt đầu).</summary>
public sealed record CareerClubPlayer(Guid Id, string Name, Position Position, int Shirt, bool Starter, int? Age, int Ovr, long Price, long Wage, int? ContractLeft, int? Height = null, int? Weight = null);
/// <summary>OVR TB người đá chính (11 ô đầu đội hình — đúng 11 người mô phỏng dùng) của 1 tuyến: CLB so với TB các CLB cùng giải.</summary>
public sealed record CareerClubLine(PositionGroup Group, int Count, double Club, double League);
/// <summary>1 mùa career đã xong của CLB: giải VĐQG, hạng cuối, danh hiệu.</summary>
public sealed record CareerClubSeason(int Number, string Season, NameLogo League, int? Position, int Teams, IReadOnlyList<NameLogo> Titles);
/// <summary>Bảng chi tiết CLB ở bước 2 career (PO 19/11: "CLB phải hiện thông tin chi tiết, đầy đủ" — chỉ dữ liệu thật).</summary>
/// <param name="XiOvr">OVR TB 11 người đá chính = sức mạnh mô phỏng dùng (<see cref="MatchSim.Strength"/>).</param>
/// <param name="Sponsor">Tài trợ/trận theo uy tín (hạng giá trị đội hình toàn thế giới) — cách tính lại đầu mùa của <c>FinanceService.RefreshSponsorsAsync</c>.</param>
public sealed record CareerClubInfo(Guid Id, string Name, string? Logo, double XiOvr, int XiRank, double LeagueXiAvg, int Teams,
    IReadOnlyList<NameLogo> Competitions, IReadOnlyList<CareerClubLine> Lines, IReadOnlyList<CareerClubPlayer> Players,
    int Capacity, int Fans, long TicketPrice, long Sponsor, IReadOnlyList<CareerClubSeason> History)
{
    public long WageBill => Players.Sum(p => p.Wage);
    public double AvgAge => Players.Where(p => p.Age is not null).Select(p => (double)p.Age!.Value).DefaultIfEmpty().Average();
    /// <summary>Vé 1 trận sân nhà với sức chứa/fan/giá vé hiện tại (<see cref="FinanceRules.Attendance"/>).</summary>
    public long TicketIncome => FinanceRules.Attendance(Capacity, Fans, TicketPrice) * TicketPrice;
}

public sealed partial class LeagueQueries
{
    public async Task<CareerLeagueInfo?> CareerLeagueAsync(Guid seasonId, string league, IOptionFileStore store, TimeProvider clock)
    {
        var editions = await EditionsAsync(seasonId);
        if (editions.FirstOrDefault(e => e.Type == CompetitionType.DomesticLeague && e.Name == league) is not { } ed) return null;
        await using var db = await dbs.CreateDbContextAsync();
        var comp = await db.Competitions.AsNoTracking().SingleAsync(c => c.Id == ed.CompetitionId);
        var cupEds = editions.Where(e => e.Type == CompetitionType.ContinentalCup || (e.Type == CompetitionType.DomesticCup && e.Country == ed.Country)).ToList();
        var edIds = cupEds.Select(e => e.Id).Append(ed.Id).ToList();
        var members = await db.EditionClubs.AsNoTracking().Where(x => edIds.Contains(x.EditionId)).Select(x => new { x.EditionId, x.ClubId }).ToListAsync();
        var clubs = await db.Clubs.AsNoTracking().ToDictionaryAsync(c => c.Id, c => new NameLogo(c.Name, c.LogoPath));
        var mine = members.Where(m => m.EditionId == ed.Id).Select(m => m.ClubId).ToHashSet();
        var links = await db.Competitions.AsNoTracking().Where(c => c.Id == comp.RelegatesToId || c.Id == comp.PromotesToId)
            .ToDictionaryAsync(c => c.Id, c => new NameLogo(c.Name, c.LogoPath));

        List<CareerLeagueCup> cups = [.. cupEds.OrderBy(e => e.Type == CompetitionType.DomesticCup ? 0 : 1).ThenBy(e => e.Tier).Select(e =>
        {
            var ids = members.Where(m => m.EditionId == e.Id).Select(m => m.ClubId).ToList();
            return new CareerLeagueCup(e.Name, e.Logo, e.Type, ids.Count, e.Rules, [.. ids.Where(mine.Contains).Select(id => clubs[id]).OrderBy(c => c.Name, StringComparer.Ordinal)]);
        }).Where(c => c.Entrants.Count > 0)];

        var ids = mine.ToList();
        var top = await (from m in db.SquadMemberships join p in db.Players on m.PlayerId equals p.Id join pr in db.PlayerPrices on p.Id equals pr.PlayerId
                         where m.LeftAt == null && ids.Contains(m.ClubId) && pr.SeasonId == seasonId
                         orderby pr.Overall descending, pr.Price descending
                         select new { p.Id, p.Name, p.Position, pr.Overall, pr.Price, m.ClubId }).Take(60).AsNoTracking().ToListAsync();
        // OVR thủ môn PES17 cao hơn hẳn → mỗi tuyến 1 người, không thì top 3 toàn thủ môn.
        top = [.. top.GroupBy(t => t.Position.Group()).Select(g => g.First()).OrderByDescending(t => t.Overall).ThenByDescending(t => t.Price).Take(3)];
        var starIds = top.Select(t => t.Id).ToList();
        var starBody = (await db.PlayerAttributes.AsNoTracking().Where(a => starIds.Contains(a.PlayerId) && a.SeasonId == seasonId).ToListAsync())
            .ToDictionary(a => a.PlayerId, a => (
                Height: a.Values.TryGetValue("HEIGHT", out var h) && h > 0 ? (int?)h : null,
                Weight: a.Values.TryGetValue("WEIGHT", out var w) && w > 0 ? (int?)w : null));

        // Lịch sử: các mùa đã xong có giải này (mùa chờ bắt đầu là mùa sau cùng → mọi mùa trước đã Finished).
        var past = await (from e in db.Editions join s in db.Seasons on e.SeasonId equals s.Id
                          where e.CompetitionId == comp.Id && e.SeasonId != seasonId && s.Status == SeasonStatus.Finished && e.Status == EditionStatus.Finished
                          orderby s.Number descending select new { e.Id, e.SeasonId, s.Number, s.Name }).AsNoTracking().ToListAsync();
        var history = new List<CareerLeagueSeason>();
        foreach (var p in past)
        {
            var view = (await EditionsAsync(p.SeasonId)).Where(e => e.Id == p.Id).ToList();
            var champ = (await SummariesAsync(p.SeasonId, view, store, clock)).FirstOrDefault()?.Champion;
            var goals = await (from g in db.Goals join f in db.Fixtures on g.FixtureId equals f.Id join pl in db.Players on g.ScorerPlayerId equals pl.Id
                               where f.CompetitionEditionId == p.Id select new { pl.Id, pl.Name, g.ClubId }).AsNoTracking().ToListAsync();
            var best = goals.GroupBy(g => g.Id).OrderByDescending(g => g.Count()).FirstOrDefault();
            List<NameLogo> down = [];
            if (comp.RelegatesToId is { } lower)
            {
                var was = await db.EditionClubs.Where(x => x.EditionId == p.Id).Select(x => x.ClubId).ToListAsync();
                var dropped = await (from x in db.EditionClubs join e in db.Editions on x.EditionId equals e.Id join s in db.Seasons on e.SeasonId equals s.Id
                                     where e.CompetitionId == lower && s.Number == p.Number + 1 && was.Contains(x.ClubId) select x.ClubId).ToListAsync();
                down = [.. dropped.Select(id => clubs[id]).OrderBy(c => c.Name, StringComparer.Ordinal)];
            }
            history.Add(new(p.Number, p.Name, champ is { } c ? clubs[c] : null, best?.First().Name,
                best is null ? null : clubs[best.GroupBy(g => g.ClubId).OrderByDescending(g => g.Count()).First().Key], best?.Count() ?? 0, down));
        }

        NameLogo? Continental(int tier) => editions.FirstOrDefault(e => e.Type == CompetitionType.ContinentalCup && e.Tier == tier) is { } c ? new(c.Name, c.Logo) : null;
        return new CareerLeagueInfo(comp.Name, comp.LogoPath, comp.Country, mine.Count, ed.Rules,
            comp.RelegatesToId is { } r ? links.GetValueOrDefault(r) : null, comp.PromotesToId is { } u ? links.GetValueOrDefault(u) : null,
            Continental(1), Continental(2), cups, [.. top.Select(t =>
            {
                starBody.TryGetValue(t.Id, out var b);
                return new CareerLeaguePlayer(t.Id, t.Name, t.Position, t.Overall, t.Price, clubs[t.ClubId], b.Height, b.Weight);
            })], history);
    }

    public async Task<CareerClubInfo?> CareerClubAsync(Guid seasonId, Guid clubId, IOptionFileStore store, TimeProvider clock)
    {
        var editions = await EditionsAsync(seasonId);
        await using var db = await dbs.CreateDbContextAsync();
        if (await db.Clubs.AsNoTracking().SingleOrDefaultAsync(c => c.Id == clubId) is not { } club) return null;
        var mineEds = await db.EditionClubs.AsNoTracking().Where(x => x.ClubId == clubId).Select(x => x.EditionId).ToListAsync();
        var eds = editions.Where(e => mineEds.Contains(e.Id)).OrderBy(e => e.Type == CompetitionType.DomesticLeague ? 0 : e.Type == CompetitionType.DomesticCup ? 1 : 2).ThenBy(e => e.Tier).ToList();
        if (eds.FirstOrDefault(e => e.Type == CompetitionType.DomesticLeague) is not { } league) return null;
        var peers = await db.EditionClubs.AsNoTracking().Where(x => x.EditionId == league.Id).Select(x => x.ClubId).ToListAsync();

        // 11 ô đầu đội hình của mọi CLB cùng giải → sức mạnh + so sánh từng tuyến
        var xi = await (from m in db.SquadMemberships join p in db.Players on m.PlayerId equals p.Id
                        join pr in db.PlayerPrices.Where(x => x.SeasonId == seasonId) on p.Id equals pr.PlayerId
                        where m.LeftAt == null && peers.Contains(m.ClubId) && m.SlotOrder < 11
                        select new { m.ClubId, p.Position, pr.Overall }).AsNoTracking().ToListAsync();
        var strength = xi.GroupBy(x => x.ClubId).ToDictionary(g => g.Key, g => g.Average(x => (double)x.Overall));
        var ranked = strength.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).ToList();
        var mineXi = xi.Where(x => x.ClubId == clubId).ToList();
        List<CareerClubLine> lines = [.. Enum.GetValues<PositionGroup>().Select(g =>
        {
            var own = mineXi.Where(x => x.Position.Group() == g).ToList();
            var all = xi.Where(x => x.Position.Group() == g).ToList();
            return new CareerClubLine(g, own.Count, own.Count == 0 ? 0 : own.Average(x => x.Overall), all.Count == 0 ? 0 : all.Average(x => x.Overall));
        }).Where(l => l.Count > 0)];

        var squad = await SquadAsync(seasonId, clubId);
        var ids = squad.Select(r => r.PlayerId).ToList();
        var attrs = await new EconomyService(db, clock).CurrentAttributesAsync(seasonId, ids);
        List<CareerClubPlayer> players = [.. squad.Select(r => new CareerClubPlayer(r.PlayerId, r.Name, r.Position, r.Shirt, r.Slot < 11,
            attrs.TryGetValue(r.PlayerId, out var v) && v.TryGetValue("AGE", out var age) ? age : null, r.Overall, r.Price,
            r.Wage ?? ContractRules.Wage(r.Price, Pes17CareerService.WagePermille), r.MatchesTotal is { } t ? t - r.MatchesPlayed : null,
            r.Height, r.Weight))];

        // Sân + tài trợ: như FinanceService (uy tín = hạng giá trị đội hình trong mọi CLB đang hoạt động)
        var values = await db.Clubs.Where(c => c.Status == ClubStatus.Active).Select(c => new
        {
            c.Id,
            Value = (from m in db.SquadMemberships join p in db.PlayerPrices on new { m.PlayerId, SeasonId = seasonId } equals new { p.PlayerId, p.SeasonId }
                     where m.ClubId == c.Id && m.LeftAt == null select (long?)p.Price).Sum() ?? 0,
        }).OrderByDescending(c => c.Value).ThenBy(c => c.Id).Select(c => c.Id).ToListAsync();
        var rep = FinanceRules.Reputation(Math.Max(0, values.IndexOf(clubId)), values.Count);
        var fin = await db.ClubFinances.AsNoTracking().SingleOrDefaultAsync(f => f.ClubId == clubId);
        var cap = fin?.Capacity ?? FinanceRules.DefaultCapacity(rep);

        // Lịch sử: các mùa career đã xong của CLB
        var clubs = await db.Clubs.AsNoTracking().ToDictionaryAsync(c => c.Id, c => new NameLogo(c.Name, c.LogoPath));
        var past = await (from x in db.EditionClubs join e in db.Editions on x.EditionId equals e.Id join s in db.Seasons on e.SeasonId equals s.Id
                          where x.ClubId == clubId && e.SeasonId != seasonId && s.Status == SeasonStatus.Finished
                          select new { e.SeasonId, s.Number, s.Name }).Distinct().AsNoTracking().ToListAsync();
        var history = new List<CareerClubSeason>();
        foreach (var p in past.OrderByDescending(p => p.Number))
        {
            var pe = (await EditionsAsync(p.SeasonId)).Where(e => mineEds.Contains(e.Id)).ToList();
            if (pe.FirstOrDefault(e => e.Type == CompetitionType.DomesticLeague) is not { } pl) continue;
            var table = await EditionStandingsAsync(pl.Id, store, clock);
            var pos = table.Select((r, i) => (r.Club, i)).FirstOrDefault(x => x.Club == clubId);
            var titles = (await SummariesAsync(p.SeasonId, pe, store, clock)).Where(x => x.Champion == clubId).Select(x => new NameLogo(x.Edition.Name, x.Edition.Logo)).ToList();
            history.Add(new(p.Number, p.Name, new(pl.Name, pl.Logo), pos.Club == clubId ? pos.i + 1 : null, table.Count, titles));
        }

        return new CareerClubInfo(club.Id, club.Name, club.LogoPath, strength.GetValueOrDefault(clubId), ranked.IndexOf(clubId) + 1,
            strength.Count == 0 ? 0 : strength.Values.Average(), peers.Count, [.. eds.Select(e => new NameLogo(e.Name, e.Logo))], lines, players,
            cap, fin?.Fans ?? cap * 9 / 10, fin?.TicketPrice ?? FinanceRules.DefaultTicketPrice, FinanceRules.Sponsor(rep), history);
    }
}
