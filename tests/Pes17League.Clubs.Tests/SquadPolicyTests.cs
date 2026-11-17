namespace Pes17League.Clubs.Tests;

/// <summary>Luật thuần, không cần DB/OF. Hành vi trên OF thật đã có oracle ở Pes17League.OptionFile.Tests.</summary>
public class SquadPolicyTests
{
    private static readonly SquadPolicy<int> Policy = new();

    // 18 người: 0 GK, 1-4 DF, 5-7 MF, 8-10 FW (đá chính); dự bị 11 GK, 12 CB, 13 CMF, 14 CMF, 15 WF, 16 GK, 17 SB
    private static List<SquadEntry<int>> Squad() =>
    [
        new(100, 1, Position.GK), new(101, 2, Position.SB), new(102, 4, Position.CB), new(103, 5, Position.CB), new(104, 3, Position.SB),
        new(105, 6, Position.DMF), new(106, 8, Position.CMF), new(107, 10, Position.AMF), new(108, 7, Position.WF), new(109, 9, Position.CF),
        new(110, 11, Position.WF), new(111, 12, Position.GK), new(112, 15, Position.CB), new(113, 14, Position.CMF), new(114, 16, Position.CMF),
        new(115, 17, Position.WF), new(116, 30, Position.GK), new(117, 18, Position.SB),
    ];

    [Fact]
    public void Release_starter_takes_first_sub_same_position()
    {
        var r = Policy.Release(Squad(), 102);   // CB đá chính → 112 (CB dự bị)
        Assert.True(r.Ok);
        Assert.False(r.NeedsLineupReview);
        Assert.Equal(112, r.Squad[2].PlayerId);
        Assert.Equal(17, r.Squad.Count);
    }

    [Fact]
    public void Release_starter_falls_back_to_group_and_flags_review()
    {
        var r = Policy.Release(Squad(), 109);   // CF: không có CF dự bị → FW đầu tiên (115 WF)
        Assert.True(r.NeedsLineupReview);
        Assert.Equal(115, r.Squad[9].PlayerId);
    }

    [Fact]
    public void Release_blocks_min_squad_and_min_gk()
    {
        var s = Squad();
        Assert.Equal(SquadError.BelowMinGoalkeepers, new SquadPolicy<int>(new() { MinGoalkeepers = 3 }).Release(s, 116).Error);
        Assert.Equal(SquadError.BelowMinSquadSize, new SquadPolicy<int>(new() { MinSquadSize = 18 }).Release(s, 117).Error);
        Assert.True(Policy.Release(s, 116).Ok);   // còn 2 GK
    }

    [Fact]
    public void Add_uses_lowest_free_number_and_blocks_full()
    {
        var r = Policy.Add(Squad(), 200, Position.CF);
        Assert.Equal(new SquadEntry<int>(200, 13, Position.CF), r.Squad[^1]);
        Assert.Equal(SquadError.AlreadyInSquad, Policy.Add(Squad(), 100, Position.GK).Error);
        Assert.Equal(SquadError.SquadFull, new SquadPolicy<int>(new() { MaxSquadSize = 18 }).Add(Squad(), 200, Position.CF).Error);
    }

    [Fact]
    public void Shirt_numbers()
    {
        Assert.Equal(SquadError.ShirtNumberTaken, Policy.SetShirtNumber(Squad(), 100, 30).Error);
        Assert.Equal(SquadError.InvalidShirtNumber, Policy.SetShirtNumber(Squad(), 100, 0).Error);
        Assert.Equal(99, Policy.SetShirtNumber(Squad(), 100, 99).Squad[0].ShirtNumber);

        var dup = Squad();
        dup[5] = dup[5] with { ShirtNumber = 1 };
        dup[6] = dup[6] with { ShirtNumber = 0 };
        var (fixedSquad, changes) = SquadPolicy<int>.NormalizeShirtNumbers(dup);
        Assert.Equal([(105, 1, 6), (106, 0, 8)], changes.Select(c => (c.PlayerId, c.From, c.To)));
        Assert.Equal(1, fixedSquad[0].ShirtNumber);   // người đứng trước giữ số
    }
}
