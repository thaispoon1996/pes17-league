using Pes17League.Clubs.Domain;

namespace Pes17League.Clubs.Tests;

public class AgingRulesTests
{
    [Fact]
    public void Young_grow_peak_holds_old_decline()
    {
        for (var roll = 0; roll < 1000; roll += 7)
        {
            Assert.InRange(AgingRules.Development(19, roll), 2, 4);
            Assert.InRange(AgingRules.Development(22, roll), 1, 3);
            Assert.Equal(0, AgingRules.Development(28, roll));
            Assert.InRange(AgingRules.Development(33, roll), -2, -1);
            Assert.InRange(AgingRules.Development(36, roll), -3, -2);
        }
    }

    [Fact]
    public void Next_season_adds_a_year_and_changes_only_role_fields_within_range()
    {
        var v = new Dictionary<string, int> { ["AGE"] = 19, ["ATTACK"] = 98, ["DEFENSE"] = 50, ["HEIGHT"] = 180 };
        var n = AgingRules.NextSeason(v, ["ATTACK", "DEFENSE"], roll: 2);   // 20 tuổi: +4
        Assert.Equal(20, n["AGE"]);
        Assert.Equal(99, n["ATTACK"]);
        Assert.Equal(54, n["DEFENSE"]);
        Assert.Equal(180, n["HEIGHT"]);
        Assert.Equal(19, v["AGE"]);   // không sửa bản gốc
    }

    [Fact]
    public void Growth_stops_at_potential_but_decline_does_not()
    {
        var young = new Dictionary<string, int> { ["AGE"] = 19, ["ATTACK"] = 70 };
        Assert.Equal(71, AgingRules.NextSeason(young, ["ATTACK"], roll: 2, maxGrowth: 1)["ATTACK"]);   // +4 chặn còn +1
        Assert.Equal(70, AgingRules.NextSeason(young, ["ATTACK"], roll: 2, maxGrowth: 0)["ATTACK"]);
        var old = new Dictionary<string, int> { ["AGE"] = 35, ["ATTACK"] = 70 };
        Assert.InRange(AgingRules.NextSeason(old, ["ATTACK"], roll: 1, maxGrowth: 0)["ATTACK"], 67, 68);
    }

    [Fact]
    public void Retirement_by_age()
    {
        Assert.False(AgingRules.Retires(33, 0));
        Assert.True(AgingRules.Retires(34, 99));
        Assert.False(AgingRules.Retires(34, 100));
        Assert.True(AgingRules.Retires(38, 999));
    }
}
