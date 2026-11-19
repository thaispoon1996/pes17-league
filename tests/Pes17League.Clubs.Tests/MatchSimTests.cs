using Pes17League.Clubs.Domain;

namespace Pes17League.Clubs.Tests;

public class MatchSimTests
{
    static readonly Position[] Shape = [Position.GK, Position.CB, Position.CB, Position.SB, Position.SB, Position.DMF, Position.CMF, Position.AMF, Position.WF, Position.WF, Position.CF];

    static SimTeam Team(int ovr) => new(
        [.. Shape.Select((p, i) => new SimPlayer(Guid.NewGuid(), p, ovr, i))],
        [.. Enumerable.Range(11, 7).Select(i => new SimPlayer(Guid.NewGuid(), Shape[1 + i % 10], ovr - 3, i))]);

    [Fact]
    public void Even_teams_give_realistic_scores()
    {
        var rng = new Random(1);
        int n = 4000, goals = 0, home = 0, draw = 0;
        for (var i = 0; i < n; i++)
        {
            var r = MatchSim.Play(Team(75), Team(75), rng);
            goals += r.HomeGoals + r.AwayGoals;
            if (r.HomeGoals > r.AwayGoals) home++;
            else if (r.HomeGoals == r.AwayGoals) draw++;
        }
        Assert.InRange(goals / (double)n, 2.3, 3.1);
        Assert.InRange(home / (double)n, 0.38, 0.52);
        Assert.InRange(draw / (double)n, 0.18, 0.32);
    }

    [Fact]
    public void Stronger_team_wins_more_but_not_always()
    {
        var rng = new Random(2);
        int wins = 0, n = 2000;
        for (var i = 0; i < n; i++)
        {
            var r = MatchSim.Play(Team(70), Team(80), rng);
            if (r.AwayGoals > r.HomeGoals) wins++;
        }
        Assert.InRange(wins / (double)n, 0.5, 0.85);
    }

    [Fact]
    public void Knockout_draws_go_to_valid_penalties_and_goals_belong_to_team()
    {
        var rng = new Random(3);
        for (var i = 0; i < 500; i++)
        {
            SimTeam h = Team(76), a = Team(76);
            var r = MatchSim.Play(h, a, rng, knockout: true);
            if (r.HomeGoals == r.AwayGoals)
            {
                Assert.NotNull(r.Penalties);
                Assert.True(Fixture.PenaltiesValid(r.HomeGoals, r.AwayGoals, r.Penalties!.Value.Home, r.Penalties.Value.Away));
            }
            else Assert.Null(r.Penalties);
            var homeIds = h.Starters.Concat(h.Bench).Select(p => p.PlayerId).ToHashSet();
            var awayIds = a.Starters.Concat(a.Bench).Select(p => p.PlayerId).ToHashSet();
            Assert.All(r.Goals, g => Assert.Contains(g.Scorer!.Value, g.Home ? homeIds : awayIds));
            Assert.All(r.Players, p => Assert.InRange(p.Rating, 4.0m, 10.0m));
            Assert.Equal(r.HomeGoals, r.Players.Where(p => p.Home).Sum(p => p.Goals));
            Assert.Equal(14, r.Players.Count(p => p.Home));   // 11 đá chính + 3 dự bị vào sân
        }
    }

    [Fact]
    public void League_match_never_has_penalties_or_extra_time()
    {
        var rng = new Random(4);
        for (var i = 0; i < 300; i++)
        {
            var r = MatchSim.Play(Team(75), Team(75), rng);
            Assert.Null(r.Penalties);
            Assert.All(r.Goals, g => Assert.InRange(g.Minute, 1, 90));
        }
    }

    [Fact]
    public void Short_squad_still_plays()
    {
        var r = MatchSim.Play(new SimTeam([], []), Team(75), new Random(5));
        Assert.Equal(0, r.Players.Count(p => p.Home));
        Assert.True(r.HomeGoals >= 0);
    }

    [Fact]
    public void Injured_starters_are_replaced_by_best_fit_bench_player()
    {
        var gk = new SimPlayer(Guid.NewGuid(), Position.GK, 85, 0);
        var cb = new SimPlayer(Guid.NewGuid(), Position.CB, 80, 1);
        var outfield = Enumerable.Range(2, 9).Select(i => new SimPlayer(Guid.NewGuid(), Position.CMF, 75, i)).ToList();
        var benchGk = new SimPlayer(Guid.NewGuid(), Position.GK, 70, 11);
        var benchCbWeak = new SimPlayer(Guid.NewGuid(), Position.CB, 68, 12);
        var benchCbGood = new SimPlayer(Guid.NewGuid(), Position.SB, 74, 13);
        var benchCbHurt = new SimPlayer(Guid.NewGuid(), Position.CB, 79, 14);
        var squad = new List<SimPlayer> { gk, cb }.Concat(outfield).Concat([benchGk, benchCbWeak, benchCbGood, benchCbHurt]).ToList();
        var hurt = new HashSet<Guid> { gk.PlayerId, cb.PlayerId, benchCbHurt.PlayerId };

        var t = MatchSim.Lineup(squad, hurt.Contains);

        Assert.Equal(11, t.Starters.Count);
        Assert.Equal(benchGk.PlayerId, t.Starters[0].PlayerId);
        Assert.Equal(0, t.Starters[0].Slot);
        Assert.Equal(benchCbGood.PlayerId, t.Starters[1].PlayerId);   // cùng tuyến hậu vệ, tốt nhất còn khoẻ
        Assert.Equal([benchCbWeak.PlayerId], t.Bench.Select(p => p.PlayerId));
    }
}
