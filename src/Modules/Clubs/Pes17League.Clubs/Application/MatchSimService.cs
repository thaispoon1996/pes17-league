using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Infrastructure;

namespace Pes17League.Clubs.Application;

/// <param name="Simulated">Số trận đã mô phỏng và ghi kết quả.</param>
/// <param name="Errors">Trận ghi kết quả lỗi (giữ Scheduled).</param>
/// <param name="Settled">Khung đã chốt (trận cuối của khung vừa ghi).</param>
public sealed record SimMatchdayResult(int Simulated, IReadOnlyList<string> Errors, bool Settled, bool SeasonFinished);

/// <summary>
/// Career PES17 (PO 19/11): trận không có người chơi thì mô phỏng (<see cref="MatchSim"/>) thay vì đá trong game. Kết quả ghi qua
/// <see cref="CompetitionService.RecordResultAsync"/> (nguồn <see cref="ResultSource.Simulated"/>) kèm người ghi bàn, điểm cầu thủ, thống kê,
/// nên phần chốt khung (BXH, kinh tế, tiến hoá chỉ số theo điểm, thể lực/chấn thương, ban lãnh đạo, bốc thăm cúp) chạy như trận thật.
/// </summary>
public sealed class MatchSimService(ClubsDbContext db, IOptionFileStore store, TimeProvider clock, Random? rng = null)
{
    private readonly Random _rng = rng ?? Random.Shared;

    /// <summary>Mô phỏng các trận Scheduled của khung: mặc định bỏ trận có CLB người chơi (chờ người đá); <paramref name="onlyFixture"/> = chỉ trận đó
    /// (kể cả trận của người chơi, khi người chơi chọn mô phỏng).</summary>
    public async Task<SimMatchdayResult> SimulateMatchdayAsync(Guid matchdayId, Guid? onlyFixture = null, CancellationToken ct = default)
    {
        var md = await db.Matchdays.AsNoTracking().SingleAsync(m => m.Id == matchdayId, ct);
        var fixtures = await db.Fixtures.AsNoTracking().Where(f => f.MatchdayId == matchdayId && f.Status == FixtureStatus.Scheduled
            && (onlyFixture == null || f.Id == onlyFixture)).ToListAsync(ct);
        if (onlyFixture is null)
        {
            var humans = await MatchOrchestrator.HumanClubsAsync(db, fixtures.SelectMany(f => new[] { f.HomeClubId, f.AwayClubId }), ct);
            fixtures = [.. fixtures.Where(f => !MatchOrchestrator.NeedsHuman(f, humans))];
        }
        if (fixtures.Count == 0) return new(0, [], false, false);

        var clubs = fixtures.SelectMany(f => new[] { f.HomeClubId, f.AwayClubId }).Distinct().ToList();
        await new BotCoachService(db, clock).ApplyAsync(matchdayId, ct, clubs);   // HLV BOT xếp đội (chấn thương / mệt ra dự bị)
        db.ChangeTracker.Clear();
        var teams = await TeamsAsync(md.SeasonId, clubs, ct);

        var errors = new List<string>();
        var done = 0;
        var settled = false;
        var finished = false;
        foreach (var f in fixtures)
        {
            ct.ThrowIfCancellationRequested();
            var knockout = f.Leg is null && f.Stage > FixtureStage.LeaguePhase;
            var r = MatchSim.Play(teams.GetValueOrDefault(f.HomeClubId, Empty), teams.GetValueOrDefault(f.AwayClubId, Empty), _rng, knockout, f.Stage == FixtureStage.Final);
            Guid Club(bool home) => home ? f.HomeClubId : f.AwayClubId;
            var goals = r.Goals.Select(g => new GoalInput(Club(g.Home), g.Half, g.Minute, null, g.Scorer)).ToList();
            var ratings = r.Players.Select(p => new RatingInput(Club(p.Home), p.PlayerId, p.Slot, p.Rating)).ToList();
            MatchTeamStats Team(bool home, SimTeamLine t) => new()
            {
                ClubId = Club(home), PossessionPct = t.Possession, Shots = t.Shots, ShotsOnTarget = t.ShotsOnTarget, Passes = t.Passes,
                PassesCompleted = t.PassesCompleted, Fouls = t.Fouls, Corners = t.Corners, Offsides = t.Offsides,
            };
            var stats = new MatchStatsInput([Team(true, r.HomeStats), Team(false, r.AwayStats)], [],
                [.. r.Players.Select(p => new PlayerStatsInput(Club(p.Home), p.PlayerId, p.Slot, p.Goals, p.Shots, p.ShotsOnTarget, 0, 0, 0, p.InMinute, p.OutMinute))]);
            var res = await new CompetitionService(db, store, clock).RecordResultAsync(f.Id, r.HomeGoals, r.AwayGoals, goals, ResultSource.Simulated, ct: ct,
                ratings: ratings, penalties: r.Penalties, stats: stats);
            db.ChangeTracker.Clear();
            if (!res.Ok) { errors.Add($"{f.Id}: {res.Error}"); continue; }
            done++;
            if (res.Settlement is not null) settled = true;
            finished |= res.SeasonFinished;
        }
        return new(done, errors, settled, finished);
    }

    static readonly SimTeam Empty = new([], []);

    /// <summary>Đội hình hiện tại theo ô (SlotOrder): 11 đầu đá chính, còn lại dự bị, người chấn thương được thay (<see cref="MatchSim.Lineup"/>);
    /// OVR hiệu dụng = OVR mùa trừ mệt.</summary>
    private async Task<Dictionary<Guid, SimTeam>> TeamsAsync(Guid seasonId, IReadOnlyCollection<Guid> clubs, CancellationToken ct)
    {
        var rows = await (from m in db.SquadMemberships
                          where m.LeftAt == null && clubs.Contains(m.ClubId)
                          join p in db.Players on m.PlayerId equals p.Id
                          select new { m.ClubId, m.PlayerId, m.SlotOrder, p.Position }).ToListAsync(ct);
        var ids = rows.Select(r => r.PlayerId).ToList();
        var ovr = await db.PlayerPrices.Where(x => x.SeasonId == seasonId && ids.Contains(x.PlayerId)).ToDictionaryAsync(x => x.PlayerId, x => x.Overall, ct);
        var cond = await db.PlayerConditions.Where(c => ids.Contains(c.PlayerId)).ToDictionaryAsync(c => c.PlayerId, ct);
        return rows.GroupBy(r => r.ClubId).ToDictionary(g => g.Key, g =>
        {
            var list = g.OrderBy(r => r.SlotOrder).Select((r, i) => new SimPlayer(r.PlayerId, r.Position,
                ConditionRules.EffectiveOverall(ovr.GetValueOrDefault(r.PlayerId), cond.GetValueOrDefault(r.PlayerId)), i)).ToList();
            return MatchSim.Lineup(list, id => cond.GetValueOrDefault(id)?.InjuredRounds is > 0);
        });
    }
}
