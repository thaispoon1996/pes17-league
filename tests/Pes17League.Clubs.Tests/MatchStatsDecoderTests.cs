using Pes17League.Clubs.Application;

namespace Pes17League.Clubs.Tests;

/// <summary>
/// B3: giải mã thống kê từ bộ nhớ 3 trận thử spike 07/11. MatchStats/match{1,2,3}.json = file thô tạo từ statdump (khối lúc hết giờ + mốc + bàn
/// phát lại qua pesmatch.Moments); đáp án = màn "Thông số trận đấu" của game (spike-log 07/11).
/// </summary>
public class MatchStatsDecoderTests
{
    internal static MatchStatsRaw Raw(int n) => MatchStatsRaw.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "MatchStats", $"match{n}.json")));

    // giữ bóng, sút, trúng đích, TM bắt, trong vòng cấm, đánh đầu, chân phải, chân trái, chuyền, chuyền thành công, căng ngang, cắt bóng,
    // phạm lỗi (gồm việt vị), trực tiếp, gián tiếp, việt vị, phạt góc, cánh phải, cánh trái, đá phạt, trực tiếp, rê bóng, mét rê bóng
    private static int[] Numbers(MatchTeamStats s) => [s.PossessionPct, s.Shots, s.ShotsOnTarget, s.ShotsSaved, s.ShotsInBox, s.Headers, s.RightFoot, s.LeftFoot,
        s.Passes, s.PassesCompleted, s.Crosses, s.Interceptions, s.Fouls, s.FoulsDirect, s.FoulsIndirect, s.Offsides, s.Corners, s.CornersRight, s.CornersLeft,
        s.FreeKicks, s.FreeKicksDirect, s.Dribbles, s.DribbleMeters];

    private static DecodedPlayer Player(DecodedMatchStats d, MatchSide side, int slot) => d.Players.Single(p => p.Side == side && p.Slot == slot);

    /// <summary>Pha trong danh sách của game đều ghép được mốc video (phút lệch ±1) — trừ giao bóng/đá phạt (không có bộ đếm).</summary>
    private static void AllTimed(DecodedMatchStats d) =>
        Assert.All(d.Events.Where(e => e.Type is MatchEventType.Shot or MatchEventType.Goal or MatchEventType.Foul or MatchEventType.Offside or MatchEventType.Corner
            or MatchEventType.Substitution), e => Assert.NotNull(e.VideoOffsetSec));

    [Fact]
    public void Match3_real_barca_0_1_with_two_subs()
    {
        var d = MatchStatsDecoder.Decode(Raw(3));
        Assert.Equal((0, 1), d.Goals);
        Assert.Equal([49, 6, 2, 0, 5, 4, 2, 0, 105, 100, 14, 11, 1, 1, 0, 0, 2, 1, 1, 0, 0, 84, 545], Numbers(d.Home));
        Assert.Equal([51, 5, 1, 0, 5, 1, 3, 1, 90, 89, 14, 12, 0, 0, 0, 0, 0, 0, 0, 1, 1, 82, 620], Numbers(d.Away));
        Assert.Null(d.Home.YellowCards);

        Assert.Equal(20, d.Events.Count);   // 18 dòng của game + 2 thay người
        Assert.Equal(Enumerable.Range(0, 20), d.Events.Select(e => e.Seq));
        var subs = d.Events.Where(e => e.Type == MatchEventType.Substitution).Select(e => (e.Side, e.Half, e.Minute, e.Slot, e.OtherSlot)).ToList();
        Assert.Equal([(MatchSide.Home, 2, 70, 11, 8), (MatchSide.Home, 2, 73, 12, 4)], subs);
        // người vào thay ghi theo X (0x10) → ô 11
        Assert.Contains(d.Events, e => (e.Type, e.Side, e.Minute, e.Slot) == (MatchEventType.Shot, MatchSide.Home, 73, 11));
        var goal = Assert.Single(d.Events, e => e.Type == MatchEventType.Goal);
        Assert.Equal((MatchSide.Away, 2, 70, 8, true), (goal.Side, goal.Half, goal.Minute, goal.Slot, goal.OnTarget));
        Assert.Equal(Raw(3).Goals.Single().WallSeconds, goal.VideoOffsetSec);
        Assert.Equal(2, d.Events.Count(e => e.Type == MatchEventType.Shot && e.OnTarget == true));   // trúng đích khách = chính bàn thắng
        AllTimed(d);

        Assert.Equal((13, 11), (d.Players.Count(p => p.Side == MatchSide.Home), d.Players.Count(p => p.Side == MatchSide.Away)));
        Assert.Equal((3, 2), (Player(d, MatchSide.Home, 10).Shots, Player(d, MatchSide.Home, 10).ShotsOnTarget));
        Assert.Equal(2, Player(d, MatchSide.Home, 6).Corners);
        Assert.Equal(1, Player(d, MatchSide.Away, 8).Goals);
        Assert.Equal((70, (int?)null), (Player(d, MatchSide.Home, 11).InMinute, Player(d, MatchSide.Home, 11).OutMinute));
        Assert.Equal(((int?)null, 70), (Player(d, MatchSide.Home, 8).InMinute, Player(d, MatchSide.Home, 8).OutMinute));
        Assert.Equal((73, 73), (Player(d, MatchSide.Home, 12).InMinute, Player(d, MatchSide.Home, 4).OutMinute));
        Assert.All(d.Players, p => Assert.NotNull(p.PesPlayerId));   // ID dự bị đã có ngay lúc hết giờ
        Assert.Equal(d.Home.Shots, d.Players.Where(p => p.Side == MatchSide.Home).Sum(p => p.Shots));
        Assert.Equal(d.Away.Fouls, d.Players.Where(p => p.Side == MatchSide.Away).Sum(p => p.Fouls));
    }

    [Fact]
    public void Match1_city_1_0_arsenal_offside_counts_as_foul()
    {
        var d = MatchStatsDecoder.Decode(Raw(1));
        Assert.Equal((1, 0), d.Goals);
        Assert.Equal((45, 55), (d.Home.PossessionPct, d.Away.PossessionPct));
        Assert.Equal((6, 3, 1, 1), (d.Home.Shots, d.Home.ShotsOnTarget, d.Home.Fouls, d.Home.Offsides));   // màn game "1(1)"
        Assert.Equal((3, 1, 3, 0), (d.Away.Shots, d.Away.ShotsOnTarget, d.Away.Fouls, d.Away.Offsides));
        Assert.Equal(21, d.Events.Count);
        var sub = Assert.Single(d.Events, e => e.Type == MatchEventType.Substitution);
        Assert.Equal((MatchSide.Home, 2, 74, 11, 4), (sub.Side, sub.Half, sub.Minute, sub.Slot, sub.OtherSlot));
        var offside = Assert.Single(d.Events, e => e.Type == MatchEventType.Offside);
        Assert.Equal((MatchSide.Home, 10, 74), (offside.Side, offside.Slot, offside.Minute));
        Assert.Equal([(MatchSide.Away, 34), (MatchSide.Away, 50), (MatchSide.Away, 65)],
            d.Events.Where(e => e.Type == MatchEventType.Foul).Select(e => (e.Side, e.Minute)).ToList());
        var goal = Assert.Single(d.Events, e => e.Type == MatchEventType.Goal);
        Assert.Equal((MatchSide.Home, 1, 26, 9), (goal.Side, goal.Half, goal.Minute, goal.Slot));
        Assert.Equal(3, d.Events.Count(e => e.Type == MatchEventType.Shot && e.OnTarget == true));
        AllTimed(d);
        Assert.Equal((1, 1), (Player(d, MatchSide.Home, 10).Offsides, Player(d, MatchSide.Home, 10).Fouls));
        Assert.Equal(1, Player(d, MatchSide.Home, 9).Goals);
    }

    [Fact]
    public void Match2_liverpool_0_0_chelsea_corners_and_added_time()
    {
        var d = MatchStatsDecoder.Decode(Raw(2));
        Assert.Equal((0, 0), d.Goals);
        Assert.Equal((50, 50), (d.Home.PossessionPct, d.Away.PossessionPct));   // 6281/6096: chia nguyên như game
        Assert.Equal((2, 0, 2), (d.Home.Corners, d.Home.CornersRight, d.Home.CornersLeft));
        Assert.Equal((1, 0, 1), (d.Away.Corners, d.Away.CornersRight, d.Away.CornersLeft));
        Assert.Equal(18, d.Events.Count);
        var sub = Assert.Single(d.Events, e => e.Type == MatchEventType.Substitution);
        Assert.Equal((MatchSide.Away, 2, 88, 11, 4), (sub.Side, sub.Half, sub.Minute, sub.Slot, sub.OtherSlot));
        Assert.Equal([(MatchSide.Home, 20, 4), (MatchSide.Home, 21, 4), (MatchSide.Away, 75, 4)],   // phút theo danh sách game (mốc bộ đếm 76')
            d.Events.Where(e => e.Type == MatchEventType.Corner).Select(e => (e.Side, e.Minute, e.Slot!.Value)).ToList());
        Assert.Equal([(MatchEventType.Foul, MatchSide.Home, 1, 45), (MatchEventType.FreeKick, MatchSide.Away, 1, 45)],   // 45'+: H6 phạm lỗi, A4 đá phạt
            d.Events.Where(e => e.AddedTime).Select(e => (e.Type, e.Side, e.Half, e.Minute)).ToList());
        AllTimed(d);
        Assert.Equal(2, Player(d, MatchSide.Home, 4).Corners);
        Assert.Equal((88, 88), (Player(d, MatchSide.Away, 11).InMinute, Player(d, MatchSide.Away, 4).OutMinute));
    }

    [Fact]
    public void Raw_file_round_trips()
    {
        var raw = Raw(3);
        var back = MatchStatsRaw.Parse(raw.ToJson());
        Assert.Equal(raw.FixtureId, back.FixtureId);
        Assert.Equal(raw.Moments, back.Moments);
        Assert.Equal(raw.Goals, back.Goals);
        Assert.Equal(raw.MemoryBlocks().Select(b => (b.Name, b.Address, Convert.ToHexString(b.Data))),
            back.MemoryBlocks().Select(b => (b.Name, b.Address, Convert.ToHexString(b.Data))));
    }

    [Fact]
    public void Missing_block_is_reported()
    {
        var raw = Raw(3);
        var e = Assert.Throws<InvalidDataException>(() => MatchStatsDecoder.Decode(raw.MemoryBlocks().Where(b => b.Name != "players").ToList(), raw.Moments, raw.Goals));
        Assert.Contains("0x1017", e.Message);
    }
}
