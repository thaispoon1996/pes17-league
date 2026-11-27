using Pes17League.Clubs.Application;
using Pes17League.Clubs.Domain;
using E = Pes17League.Clubs.Application.Pes17InjuryFilter.Entry;

namespace Pes17League.Clubs.Tests;

/// <summary>Cầu thủ chấn thương không vào game (PO 27/11).</summary>
public class Pes17InjuryFilterTests
{
    // 0 GK, 1-4 hậu vệ, 5-7 tiền vệ, 8-10 tiền đạo | dự bị: 11 GK, 12 CB, 13 CMF, 14 CF, 15 SB, 16 AMF, 17 GK, 18 CB ...
    private static readonly Position[] Pos =
    [
        Position.GK, Position.CB, Position.CB, Position.SB, Position.SB, Position.DMF, Position.CMF, Position.AMF,
        Position.WF, Position.CF, Position.WF,
        Position.GK, Position.CB, Position.CMF, Position.CF, Position.SB, Position.AMF, Position.GK, Position.CB, Position.SMF,
    ];

    private static List<E> Squad(int n) => [.. Enumerable.Range(0, n).Select(i => new E(Guid.NewGuid(), Pos[i % Pos.Length]))];

    [Fact]
    public void No_injury_keeps_order()
    {
        var sq = Squad(20);
        var (outSq, removed) = Pes17InjuryFilter.Apply(sq, _ => false);
        Assert.Equal(sq, outSq);
        Assert.Empty(removed);
    }

    [Fact]
    public void Injured_keeper_replaced_by_bench_keeper_and_removed()
    {
        var sq = Squad(20);
        var hurt = sq[0].PlayerId;
        var (outSq, removed) = Pes17InjuryFilter.Apply(sq, id => id == hurt);
        Assert.Equal(sq[11].PlayerId, outSq[0].PlayerId);     // thủ môn dự bị vào ô 0
        Assert.Equal([hurt], removed);
        Assert.Equal(19, outSq.Count);
        Assert.DoesNotContain(outSq, e => e.PlayerId == hurt);
    }

    [Fact]
    public void Outfield_prefers_same_position_then_same_line()
    {
        var sq = Squad(20);
        var cf = sq[9].PlayerId;     // CF → CF dự bị (ô 14)
        var dmf = sq[5].PlayerId;    // DMF → không có DMF dự bị → tiền vệ dự bị đầu tiên (CMF ô 13)
        var (outSq, _) = Pes17InjuryFilter.Apply(sq, id => id == cf || id == dmf);
        Assert.Equal(sq[14].PlayerId, outSq[9].PlayerId);
        Assert.Equal(sq[13].PlayerId, outSq[5].PlayerId);
        Assert.Equal(18, outSq.Count);
    }

    [Fact]
    public void Keeps_injured_at_end_when_squad_would_drop_below_minimum()
    {
        var sq = Squad(17);
        var hurt = new HashSet<Guid> { sq[12].PlayerId, sq[13].PlayerId, sq[14].PlayerId };   // 3 dự bị
        var (outSq, removed) = Pes17InjuryFilter.Apply(sq, hurt.Contains);
        Assert.Equal(Pes17InjuryFilter.MinSquad, outSq.Count);   // 14 khoẻ + 2 chấn thương giữ ở cuối
        Assert.Single(removed);
        Assert.All(outSq.TakeLast(2), e => Assert.Contains(e.PlayerId, hurt));
    }

    [Fact]
    public void No_healthy_keeper_keeps_injured_keeper_in_goal()
    {
        var sq = Squad(17);   // thủ môn: ô 0, 11
        var hurt = new HashSet<Guid> { sq[0].PlayerId, sq[11].PlayerId };
        var (outSq, _) = Pes17InjuryFilter.Apply(sq, hurt.Contains);
        Assert.Equal(sq[0].PlayerId, outSq[0].PlayerId);
    }
}
