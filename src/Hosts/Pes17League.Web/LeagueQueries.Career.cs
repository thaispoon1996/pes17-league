using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Application;
using Pes17League.OptionFile;

namespace Pes17League.Web;

public sealed record NameLogo(string Name, string? Logo);
/// <summary>Cúp mùa này của CLB trong giải: cúp quốc gia cùng nước + cúp châu lục. <see cref="Entrants"/> = CLB của giải này có suất.</summary>
public sealed record CareerLeagueCup(string Name, string? Logo, CompetitionType Type, int Teams, CompetitionRules Rules, IReadOnlyList<NameLogo> Entrants);
/// <summary>Ngôi sao của giải: OVR cao nhất, mỗi tuyến (thủ môn/hậu vệ/tiền vệ/tiền đạo) tối đa 1 người.</summary>
public sealed record CareerLeaguePlayer(Guid Id, string Name, Position Position, int Ovr, long Price, NameLogo Club);
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
            Continental(1), Continental(2), cups, [.. top.Select(t => new CareerLeaguePlayer(t.Id, t.Name, t.Position, t.Overall, t.Price, clubs[t.ClubId]))], history);
    }
}
