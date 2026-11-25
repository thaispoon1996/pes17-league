using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Infrastructure;

namespace Pes17League.Clubs.Application;

public sealed record YouthTableRow(Guid Club, int Played, int Won, int Drawn, int Lost, int GoalsFor, int GoalsAgainst)
{
    public int Points => Won * 3 + Drawn;
    public int GoalDifference => GoalsFor - GoalsAgainst;
}

public sealed record YouthScorer(Guid PlayerId, string Name, Guid Club, int Goals, int Apps);

public sealed record YouthSquadLine(YouthPlayer Player, int Apps, int Goals, decimal? AvgRating);

/// <summary>
/// Giải U19 mô phỏng (PO 25/11, giai đoạn 1). Bật cho giải quốc gia (<see cref="CompetitionType.DomesticLeague"/>) có CLB người chơi.
/// Lần đầu cần: sinh đội 18 người cho CLB chưa có (OVR theo đội một, <see cref="YouthLeagueRules"/>) + lịch vòng tròn 2 lượt.
/// Chốt một khung có trận của giải chính → đá vòng U19 kế tiếp bằng <see cref="MatchSim"/>.
/// </summary>
public sealed class YouthLeagueService(ClubsDbContext db, TimeProvider clock, Random? rng = null)
{
    private readonly Random _rng = rng ?? Random.Shared;

    /// <summary>Giải quốc gia trong mùa có CLB người chơi (giải có U19).</summary>
    public async Task<List<Guid>> EditionsAsync(Guid seasonId, CancellationToken ct = default)
    {
        var humans = db.TeamAssignments.Where(a => a.To == null && a.ControllerType == ControllerType.Human).Select(a => a.ClubId);
        return await (from e in db.Editions
                      join c in db.Competitions on e.CompetitionId equals c.Id
                      where e.SeasonId == seasonId && c.Type == CompetitionType.DomesticLeague
                            && db.EditionClubs.Any(ec => ec.EditionId == e.Id && humans.Contains(ec.ClubId))
                      select e.Id).ToListAsync(ct);
    }

    /// <summary>Có lịch U19 cho giải chưa thì tạo (kèm đội trẻ còn thiếu). Không lưu — người gọi SaveChanges.</summary>
    public async Task<bool> EnsureAsync(Guid editionId, CancellationToken ct = default)
    {
        if (await db.YouthFixtures.AnyAsync(f => f.EditionId == editionId, ct) || db.YouthFixtures.Local.Any(f => f.EditionId == editionId)) return false;
        var ed = await db.Editions.AsNoTracking().SingleAsync(e => e.Id == editionId, ct);
        var clubs = await db.EditionClubs.Where(x => x.EditionId == editionId).Select(x => x.ClubId).OrderBy(x => x).ToListAsync(ct);
        if (clubs.Count < 2) return false;

        var has = (await db.YouthPlayers.Where(p => clubs.Contains(p.ClubId) && p.LeftAt == null).Select(p => p.ClubId).Distinct().ToListAsync(ct)).ToHashSet();
        var missing = clubs.Where(c => !has.Contains(c)).ToList();
        if (missing.Count > 0)
        {
            var avg = await FirstTeamAverageAsync(ed.SeasonId, missing, ct);
            var now = clock.GetUtcNow();
            foreach (var club in missing)
            {
                var b = YouthLeagueRules.Base(avg.GetValueOrDefault(club, 70));
                for (var i = 0; i < YouthLeagueRules.Template.Length; i++)
                {
                    var ovr = YouthLeagueRules.Overall(b, i < 11, _rng);
                    db.YouthPlayers.Add(new YouthPlayer
                    {
                        ClubId = club, Name = YouthLeagueRules.Name(_rng), Position = YouthLeagueRules.Template[i], Age = YouthLeagueRules.Age(_rng),
                        Overall = ovr, Potential = YouthLeagueRules.Potential(ovr, _rng), Slot = i + 1, JoinedSeasonId = ed.SeasonId, CreatedAt = now,
                    });
                }
            }
        }

        var order = clubs.OrderBy(_ => _rng.Next()).ToList();
        var rounds = RoundRobin.DoubleRoundRobin(order);
        for (var r = 0; r < rounds.Count; r++)
            foreach (var (h, a) in rounds[r])
                db.YouthFixtures.Add(new YouthFixture { EditionId = editionId, SeasonId = ed.SeasonId, Round = r + 1, HomeClubId = h, AwayClubId = a });
        return true;
    }

    /// <summary>Gọi khi chốt khung <paramref name="md"/>: mỗi giải có U19 và có trận trong khung → đá vòng U19 kế tiếp. Không lưu.</summary>
    public async Task ApplyRoundAsync(Matchday md, CancellationToken ct = default)
    {
        var editions = await EditionsAsync(md.SeasonId, ct);
        if (editions.Count == 0) return;
        var inRound = await db.Fixtures.Where(f => f.MatchdayId == md.Id && f.CompetitionEditionId != null && editions.Contains(f.CompetitionEditionId!.Value))
            .Select(f => f.CompetitionEditionId!.Value).Distinct().ToListAsync(ct);
        foreach (var ed in inRound)
        {
            await EnsureAsync(ed, ct);
            await PlayNextRoundAsync(ed, md.Id, ct);
        }
    }

    private async Task PlayNextRoundAsync(Guid editionId, Guid matchdayId, CancellationToken ct)
    {
        var all = await db.YouthFixtures.Where(f => f.EditionId == editionId).ToListAsync(ct);
        all.AddRange(db.YouthFixtures.Local.Where(f => f.EditionId == editionId && !all.Contains(f)));
        var next = all.Where(f => f.HomeGoals == null).Select(f => (int?)f.Round).Min();
        if (next is null) return;
        var fixtures = all.Where(f => f.Round == next).ToList();
        var clubs = fixtures.SelectMany(f => new[] { f.HomeClubId, f.AwayClubId }).Distinct().ToList();
        var squads = (await db.YouthPlayers.Where(p => clubs.Contains(p.ClubId) && p.LeftAt == null).ToListAsync(ct))
            .Concat(db.YouthPlayers.Local.Where(p => clubs.Contains(p.ClubId) && p.LeftAt == null)).DistinctBy(p => p.Id)
            .GroupBy(p => p.ClubId)
            .ToDictionary(g => g.Key, g => MatchSim.Lineup([.. g.OrderBy(p => p.Slot).Select(p => new SimPlayer(p.Id, p.Position, p.Overall, p.Slot))], _ => false));
        var now = clock.GetUtcNow();
        foreach (var f in fixtures)
        {
            var r = MatchSim.Play(squads.GetValueOrDefault(f.HomeClubId, Empty), squads.GetValueOrDefault(f.AwayClubId, Empty), _rng);
            (f.HomeGoals, f.AwayGoals, f.MatchdayId, f.PlayedAt) = (r.HomeGoals, r.AwayGoals, matchdayId, now);
            foreach (var p in r.Players.DistinctBy(p => p.PlayerId))
                db.YouthAppearances.Add(new YouthAppearance
                {
                    FixtureId = f.Id, PlayerId = p.PlayerId, ClubId = p.Home ? f.HomeClubId : f.AwayClubId, Rating = p.Rating, Goals = p.Goals,
                    Started = p.InMinute is null,
                });
        }
    }

    static readonly SimTeam Empty = new([], []);

    /// <summary>OVR trung bình 18 người tốt nhất đội một (giá mùa).</summary>
    private async Task<Dictionary<Guid, double>> FirstTeamAverageAsync(Guid seasonId, List<Guid> clubs, CancellationToken ct)
    {
        var rows = await (from m in db.SquadMemberships
                          where m.LeftAt == null && clubs.Contains(m.ClubId)
                          join pr in db.PlayerPrices on new { m.PlayerId, SeasonId = seasonId } equals new { pr.PlayerId, pr.SeasonId }
                          select new { m.ClubId, pr.Overall }).ToListAsync(ct);
        return rows.GroupBy(r => r.ClubId).ToDictionary(g => g.Key, g => g.Select(x => x.Overall).OrderByDescending(x => x).Take(18).Average(x => (double)x));
    }

    // ---- đọc cho trang /u19 ----

    public async Task<List<YouthFixture>> FixturesAsync(Guid editionId, CancellationToken ct = default) =>
        await db.YouthFixtures.AsNoTracking().Where(f => f.EditionId == editionId).OrderBy(f => f.Round).ToListAsync(ct);

    public static List<YouthTableRow> Table(IEnumerable<Guid> clubs, IEnumerable<YouthFixture> fixtures)
    {
        var t = clubs.ToDictionary(c => c, c => new YouthTableRow(c, 0, 0, 0, 0, 0, 0));
        foreach (var f in fixtures.Where(f => f.HomeGoals is not null))
        {
            int h = f.HomeGoals!.Value, a = f.AwayGoals!.Value;
            YouthTableRow Add(YouthTableRow r, int gf, int ga) => r with
            {
                Played = r.Played + 1, Won = r.Won + (gf > ga ? 1 : 0), Drawn = r.Drawn + (gf == ga ? 1 : 0), Lost = r.Lost + (gf < ga ? 1 : 0),
                GoalsFor = r.GoalsFor + gf, GoalsAgainst = r.GoalsAgainst + ga,
            };
            if (t.TryGetValue(f.HomeClubId, out var hr)) t[f.HomeClubId] = Add(hr, h, a);
            if (t.TryGetValue(f.AwayClubId, out var ar)) t[f.AwayClubId] = Add(ar, a, h);
        }
        return [.. t.Values.OrderByDescending(r => r.Points).ThenByDescending(r => r.GoalDifference).ThenByDescending(r => r.GoalsFor)];
    }

    public async Task<List<YouthScorer>> ScorersAsync(Guid editionId, int take = 10, CancellationToken ct = default)
    {
        var rows = await (from ap in db.YouthAppearances
                          join f in db.YouthFixtures on ap.FixtureId equals f.Id
                          where f.EditionId == editionId
                          group ap by new { ap.PlayerId, ap.ClubId } into g
                          select new { g.Key.PlayerId, g.Key.ClubId, Goals = g.Sum(x => x.Goals), Apps = g.Count() })
                         .Where(x => x.Goals > 0).OrderByDescending(x => x.Goals).ThenBy(x => x.Apps).Take(take).ToListAsync(ct);
        var ids = rows.Select(r => r.PlayerId).ToList();
        var names = await db.YouthPlayers.Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        return [.. rows.Select(r => new YouthScorer(r.PlayerId, names.GetValueOrDefault(r.PlayerId, "?"), r.ClubId, r.Goals, r.Apps))];
    }

    public async Task<List<YouthSquadLine>> SquadAsync(Guid clubId, Guid editionId, CancellationToken ct = default)
    {
        var players = await db.YouthPlayers.AsNoTracking().Where(p => p.ClubId == clubId && p.LeftAt == null).OrderBy(p => p.Slot).ToListAsync(ct);
        var stats = await (from ap in db.YouthAppearances
                           join f in db.YouthFixtures on ap.FixtureId equals f.Id
                           where f.EditionId == editionId && ap.ClubId == clubId
                           group ap by ap.PlayerId into g
                           select new { Id = g.Key, Apps = g.Count(), Goals = g.Sum(x => x.Goals), Avg = g.Average(x => x.Rating) }).ToDictionaryAsync(x => x.Id, ct);
        return [.. players.Select(p => stats.TryGetValue(p.Id, out var s) ? new YouthSquadLine(p, s.Apps, s.Goals, Math.Round(s.Avg, 1)) : new YouthSquadLine(p, 0, 0, null))];
    }

    /// <summary>Người ghi bàn từng trận (tên, số bàn) cho danh sách kết quả.</summary>
    public async Task<Dictionary<Guid, List<(Guid Club, string Name, int Goals)>>> GoalsAsync(IReadOnlyCollection<Guid> fixtures, CancellationToken ct = default)
    {
        var rows = await (from ap in db.YouthAppearances
                          where fixtures.Contains(ap.FixtureId) && ap.Goals > 0
                          join p in db.YouthPlayers on ap.PlayerId equals p.Id
                          select new { ap.FixtureId, ap.ClubId, p.Name, ap.Goals }).ToListAsync(ct);
        return rows.GroupBy(r => r.FixtureId).ToDictionary(g => g.Key, g => g.Select(x => (x.ClubId, x.Name, x.Goals)).ToList());
    }
}
