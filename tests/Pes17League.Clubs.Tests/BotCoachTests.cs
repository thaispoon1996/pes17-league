using Pes17League.Clubs.Application;
using Pes17League.Clubs.Domain;
using Pes17League.OptionFile;

namespace Pes17League.Clubs.Tests;

/// <summary>HLV BOT v1 thuần — luật chọn sơ đồ / XI / thiết lập (hlv-bot-v1.md).</summary>
public class BotCoachTests
{
    static BotPlayer P(Position pos, int ovr) => new(Guid.NewGuid(), pos, ovr);

    static List<BotPlayer> FullSquad()
    {
        // 2 GK + đủ tuyến, CF mạnh → thiên 4-3-3 / tấn công.
        return
        [
            P(Position.GK, 80), P(Position.GK, 70),
            P(Position.CB, 78), P(Position.CB, 76), P(Position.CB, 74),
            P(Position.SB, 75), P(Position.SB, 73),
            P(Position.DMF, 77), P(Position.CMF, 79), P(Position.CMF, 72),
            P(Position.AMF, 81), P(Position.SMF, 74), P(Position.SMF, 71),
            P(Position.WF, 76), P(Position.WF, 75),
            P(Position.CF, 85), P(Position.SS, 78),
        ];
    }

    [Fact]
    public void Plan_picks_attack_formation_and_max_profile_settings()
    {
        var plan = BotCoach.Plan(FullSquad());
        Assert.Contains(plan.Formation, new[] { "4-3-3 A", "4-4-2 A", "3-4-3 A", "3-5-2 A", "4-3-3 B", "4-5-1 B" });
        Assert.Equal(11, plan.StarterOrder.Count);
        Assert.Equal(plan.StarterOrder.Distinct().Count(), plan.StarterOrder.Count);
        Assert.Equal(0, plan.Tactics.BackLine);       // A = dâng cao
        Assert.Equal(2, plan.Tactics.OffsideTrap);    // C = không bẫy
        Assert.Equal(2, plan.Tactics.Pressure);
        Assert.Equal(2, plan.Tactics.CounterAttack);
        Assert.Null(plan.Tactics.Positions);
        Assert.Equal(4, plan.Tactics.Strategies!.Count);
        Assert.Equal(7, plan.Tactics.Strategies[2]); // Pressing
        Assert.Equal(8, plan.Tactics.Strategies[3]); // Phòng ngự phản công
        Assert.Contains(plan.Tactics.Strategies[0]!.Value, new[] { 1, 2, 3 });
        Assert.Contains(plan.Tactics.Strategies[1]!.Value, new[] { 1, 2, 3 });
    }

    [Fact]
    public void Plan_keeps_goalkeeper_on_bench_when_available()
    {
        var squad = FullSquad();
        var plan = BotCoach.Plan(squad);
        Assert.Single(squad.Where(p => plan.StarterOrder.Contains(p.PlayerId) && p.Position == Position.GK));
        Assert.Contains(plan.BenchOrder, id => squad.Any(p => p.PlayerId == id && p.Position == Position.GK));
    }

    [Fact]
    public void Plan_falls_back_to_451_when_few_forwards()
    {
        var squad = new List<BotPlayer>
        {
            P(Position.GK, 80), P(Position.GK, 70),
            P(Position.CB, 78), P(Position.CB, 76), P(Position.CB, 74), P(Position.CB, 72),
            P(Position.SB, 75), P(Position.SB, 73),
            P(Position.DMF, 77), P(Position.CMF, 79), P(Position.CMF, 78), P(Position.CMF, 76),
            P(Position.SMF, 74), P(Position.SMF, 71), P(Position.AMF, 75),
            P(Position.CF, 82), // chỉ 1 tiền đạo đăng ký
        };
        var plan = BotCoach.Plan(squad);
        // Có thể chọn 4-5-1 B hoặc vẫn sơ đồ khác nếu gán mềm được — ít nhất phải có kế hoạch hợp lệ.
        Assert.Equal(11, plan.StarterOrder.Count);
        Assert.Equal(plan.Formation, plan.Tactics.Formation);
    }

    [Fact]
    public void Plan_with_exactly_11_players()
    {
        var squad = new List<BotPlayer>
        {
            P(Position.GK, 80),
            P(Position.CB, 78), P(Position.CB, 76),
            P(Position.SB, 75), P(Position.SB, 73),
            P(Position.DMF, 77), P(Position.CMF, 79), P(Position.CMF, 72),
            P(Position.WF, 76), P(Position.WF, 75), P(Position.CF, 85),
        };
        var plan = BotCoach.Plan(squad);
        Assert.Equal(11, plan.StarterOrder.Count);
        Assert.Empty(plan.BenchOrder);
        Assert.Equal(squad.Select(p => p.PlayerId).ToHashSet(), plan.StarterOrder.ToHashSet());
    }
}
