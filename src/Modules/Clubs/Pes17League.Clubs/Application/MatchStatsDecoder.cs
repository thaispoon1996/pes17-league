using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pes17League.Clubs.Application;

/// <summary>Khối bộ nhớ pes6.exe đọc lúc hết giờ (tools/match/memstats.py): tên, địa chỉ đầu, dữ liệu.</summary>
public sealed record MemoryBlock(string Name, long Address, byte[] Data);

/// <summary>
/// File thô thống kê 1 trận (StatsDir/{FixtureId:N}.json, B3): khối bộ nhớ (hex) + mốc + bàn — đủ để giải mã lại (vd thẻ khi đã dò ra)
/// mà không cần đá lại.
/// </summary>
public sealed record MatchStatsRaw(Guid FixtureId, IReadOnlyDictionary<string, MatchStatsRaw.Block> Blocks, IReadOnlyList<RunMoment> Moments,
    IReadOnlyList<RunGoal> Goals)
{
    public sealed record Block(long Addr, string Hex);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public static MatchStatsRaw Of(Guid fixtureId, IReadOnlyList<MemoryBlock> blocks, IReadOnlyList<RunMoment> moments, IReadOnlyList<RunGoal> goals) =>
        new(fixtureId, blocks.ToDictionary(b => b.Name, b => new Block(b.Address, Convert.ToHexString(b.Data).ToLowerInvariant())), moments, goals);

    public IReadOnlyList<MemoryBlock> MemoryBlocks() => Blocks.Select(b => new MemoryBlock(b.Key, b.Value.Addr, Convert.FromHexString(b.Value.Hex))).ToList();

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static MatchStatsRaw Parse(string json) => JsonSerializer.Deserialize<MatchStatsRaw>(json, Json) ?? throw new InvalidDataException("File thống kê rỗng");
}

/// <summary>Mốc 1 pha lúc đá (pesmatch.py "moment", 2 lần/giây): Kind = shot/on_target/foul/offside/corner/sub.</summary>
/// <param name="Half">1 = hiệp 1, 2 = hiệp 2, 3/4 = hiệp phụ (như <see cref="RunGoal"/>).</param>
/// <param name="Slot">Ô cầu thủ (thay người: ô người vào); <paramref name="OutSlot"/>: ô người ra (null = không xác định).</param>
public sealed record RunMoment(MatchSide Side, int Half, int Minute, double WallSeconds, string Kind, int Slot, int? OutSlot = null);

/// <param name="Slot">Ô cầu thủ (cùng thứ tự ô với <see cref="RunRating"/>); thay người: ô vào, <paramref name="OtherSlot"/> ô ra.</param>
public sealed record DecodedEvent(int Seq, MatchSide Side, int Half, int Minute, bool AddedTime, MatchEventType Type, int RawType, int? Slot, int? OtherSlot,
    bool? OnTarget, double? VideoOffsetSec);

public sealed record DecodedPlayer(MatchSide Side, int Slot, int? PesPlayerId, int Goals, int Shots, int ShotsOnTarget, int Fouls, int Offsides, int Corners,
    int? InMinute, int? OutMinute);

/// <summary>Home/Away: chưa gán FixtureId/ClubId. Goals = số bàn trong khối đội (kiểm khớp tỷ số trước khi ghi).</summary>
public sealed record DecodedMatchStats(MatchTeamStats Home, MatchTeamStats Away, (int Home, int Away) Goals, IReadOnlyList<DecodedEvent> Events,
    IReadOnlyList<DecodedPlayer> Players);

/// <summary>
/// Giải mã thống kê trận từ bộ nhớ (B3; địa chỉ + ý nghĩa: spike-log 07/11, 3 trận đối chiếu màn "Thông số trận đấu"). Hàm thuần.
/// Khối đội: nhà 0x10d0d50, khách 0x10d0d8c. Diễn biến: số dòng u8 @0x10d0691, mỗi dòng 3 byte (phút | 0x80 bù giờ, loại, cầu thủ = X | 0x80 khách);
/// X → ô qua bảng X→ô. Từng ô: 40 byte (u16 +0 bàn, +6 sút, +0xa trúng đích, +0xc phạm lỗi gồm việt vị, +0xe việt vị, +0x10/+0x12 phạt góc trái/phải),
/// đầu đội 36 byte trước ô 0 (+0xe tổng việt vị, +0x18 thời gian giữ bóng). Phạm lỗi (đội và từng ô) gồm cả việt vị như màn game "1(1)".
/// Thẻ: chưa giải mã (null).
/// </summary>
public static class MatchStatsDecoder
{
    private const long TeamHome = 0x10d0d50, TeamAway = 0x10d0d8c, LogCount = 0x10d0691, LogStart = 0x10d0692;
    private const long SlotsHome = 0x1017b5c, SlotsAway = 0x1017e50, SlotSize = 40, HeaderSize = 0x24;
    private const long XToSlot = 0x1018d28, SlotToX = 0x1018de8, PesIds = 0x10cf5ba;
    public const int Slots = 18;

    public static DecodedMatchStats Decode(MatchStatsRaw raw) => Decode(raw.MemoryBlocks(), raw.Moments, raw.Goals);

    public static DecodedMatchStats Decode(IReadOnlyList<MemoryBlock> blocks, IReadOnlyList<RunMoment> moments, IReadOnlyList<RunGoal> goals)
    {
        var m = new Memory(blocks);
        var events = Events(m, moments, goals);
        return new DecodedMatchStats(Team(m, 0), Team(m, 1), (m.U16(TeamHome + 2), m.U16(TeamAway + 2)), events, Players(m, events));
    }

    private static MatchTeamStats Team(Memory m, int t)
    {
        var b = t == 0 ? TeamHome : TeamAway;
        int h = m.U16(SlotsHome - HeaderSize + 0x18), a = m.U16(SlotsAway - HeaderSize + 0x18);
        var home = h + a == 0 ? 50 : h * 100 / (h + a);   // chia nguyên như game (6281/6096 → 50/50)
        return new MatchTeamStats
        {
            PossessionPct = t == 0 ? home : 100 - home,
            Shots = m.U8(b + 0xe), ShotsOnTarget = m.U8(b + 0xf), ShotsSaved = m.U8(b + 0x10), ShotsInBox = m.U8(b + 0x11), Headers = m.U8(b + 0x12),
            RightFoot = m.U8(b + 0x13), LeftFoot = m.U8(b + 0x14), Passes = m.U16(b + 0x16), PassesCompleted = m.U16(b + 0x18),
            Crosses = m.U8(b + 0x1a), Interceptions = m.U8(b + 0x1b), Fouls = m.U8(b + 0x1c), FoulsDirect = m.U8(b + 0x1d), FoulsIndirect = m.U8(b + 0x1e),
            Offsides = m.U16((t == 0 ? SlotsHome : SlotsAway) - HeaderSize + 0xe),
            Corners = m.U8(b + 0x21), CornersRight = m.U8(b + 0x22), CornersLeft = m.U8(b + 0x23), FreeKicks = m.U8(b + 0x24), FreeKicksDirect = m.U8(b + 0x25),
            Dribbles = m.U16(b + 0x2a), DribbleMeters = m.U16(b + 0x2c),
        };
    }

    private static MatchEventType Type(int raw) => raw switch
    {
        0x2c => MatchEventType.Kickoff, 0x01 => MatchEventType.Shot, 0x21 => MatchEventType.Goal, 0x29 => MatchEventType.Foul,
        0x18 => MatchEventType.FreeKick, 0x2b => MatchEventType.Offside, 0x1a or 0x1b => MatchEventType.Corner, _ => MatchEventType.Other,
    };

    private static string? MomentKind(MatchEventType t) => t switch
    {
        MatchEventType.Shot => "shot", MatchEventType.Foul => "foul", MatchEventType.Offside => "offside", MatchEventType.Corner => "corner", _ => null,
    };

    /// <summary>Hiệp theo phút (danh sách không ghi hiệp): bù giờ 45'+ → 1, 90'+ → 2…; còn lại &lt;45 → 1, &lt;90 → 2, &lt;105 → 3, còn lại 4.</summary>
    public static int HalfOf(int minute, bool added) =>
        added ? (minute <= 45 ? 1 : minute <= 90 ? 2 : minute <= 105 ? 3 : 4) : (minute < 45 ? 1 : minute < 90 ? 2 : minute < 105 ? 3 : 4);

    private static int? SlotOf(Memory m, int t, int x) =>
        x < 0x20 && m.U8(XToSlot + 0x20 * t + x) is var s && s < Slots ? s : null;

    /// <summary>Cú sút từ mốc: "on_target" tăng cùng lúc "shot" của cùng ô (≤ 2 s) → 1 cú sút trúng đích; on_target lẻ → cú trúng đích.</summary>
    public static List<(RunMoment Moment, bool OnTarget)> Shots(IReadOnlyList<RunMoment> moments)
    {
        var shots = new List<(RunMoment, bool)>();
        var onTarget = moments.Where(x => x.Kind == "on_target").ToList();
        foreach (var s in moments.Where(x => x.Kind == "shot").OrderBy(x => x.WallSeconds))
        {
            var o = onTarget.FirstOrDefault(x => x.Side == s.Side && x.Slot == s.Slot && Math.Abs(x.WallSeconds - s.WallSeconds) <= 2);
            if (o is not null) onTarget.Remove(o);
            shots.Add((s, o is not null));
        }
        shots.AddRange(onTarget.Select(o => (o, true)));
        return shots;
    }

    private static List<DecodedEvent> Events(Memory m, IReadOnlyList<RunMoment> moments, IReadOnlyList<RunGoal> goals)
    {
        var pool = moments.Where(x => x.Kind is "foul" or "offside" or "corner").Select(x => (Moment: x, OnTarget: false)).Concat(Shots(moments))
            .OrderBy(x => x.Moment.WallSeconds).ToList();
        var goalsLeft = goals.OrderBy(g => g.WallSeconds).ToList();

        var list = new List<DecodedEvent>();
        var n = Math.Min(m.U8(LogCount), (int)((TeamHome - LogStart) / 3));
        for (var i = 0; i < n; i++)
        {
            var at = LogStart + 3 * i;
            int b0 = m.U8(at), raw = m.U8(at + 1), b2 = m.U8(at + 2);
            var (minute, added, side) = (b0 & 0x7f, (b0 & 0x80) != 0, (b2 & 0x80) != 0 ? MatchSide.Away : MatchSide.Home);
            var half = HalfOf(minute, added);
            var type = Type(raw);
            var slot = SlotOf(m, side == MatchSide.Home ? 0 : 1, b2 & 0x7f);
            double? wall = null;
            bool? hit = null;
            if (type == MatchEventType.Goal && goalsLeft.FirstOrDefault(g => g.Side == side) is { } goal)
            {
                goalsLeft.Remove(goal);
                wall = goal.WallSeconds;
                hit = true;
                // cú sút thành bàn cũng tăng bộ đếm sút của ô → bỏ khỏi danh sách ghép (danh sách không ghi 0x01 cho bàn thắng)
                var shot = pool.FirstOrDefault(x => x.Moment.Kind is "shot" or "on_target" && x.Moment.Side == side && x.Moment.Slot == slot && Math.Abs(x.Moment.WallSeconds - goal.WallSeconds) <= 15);
                if (shot.Moment is not null) pool.Remove(shot);
            }
            else if (MomentKind(type) is { } kind && slot is not null)
            {
                var hitM = pool.FirstOrDefault(x => (x.Moment.Kind == kind || kind == "shot" && x.Moment.Kind == "on_target") && x.Moment.Side == side && x.Moment.Slot == slot
                    && x.Moment.Half == half && (added ? x.Moment.Minute >= minute - 1 : Math.Abs(x.Moment.Minute - minute) <= 1));
                if (hitM.Moment is not null)
                {
                    pool.Remove(hitM);
                    wall = hitM.Moment.WallSeconds;
                    if (type == MatchEventType.Shot) hit = hitM.OnTarget;
                }
            }
            list.Add(new DecodedEvent(0, side, half, minute, added, type, raw, slot, null, hit, wall));
        }
        // Thay người (không có trong danh sách của game): bù giờ theo đồng hồ (hiệp 1 từ 45', hiệp 2 từ 90'…) ghi như danh sách: 45'+.
        foreach (var s in moments.Where(x => x.Kind == "sub"))
        {
            var end = s.Half switch { 1 => 45, 2 => 90, 3 => 105, _ => 120 };
            var added = s.Minute >= end;
            list.Add(new DecodedEvent(0, s.Side, s.Half, added ? end : s.Minute, added, MatchEventType.Substitution, -1, s.Slot, s.OutSlot, null, s.WallSeconds));
        }
        return list.OrderBy(e => e.Half).ThenBy(e => e.AddedTime).ThenBy(e => e.Minute).Select((e, i) => e with { Seq = i }).ToList();
    }

    private static List<DecodedPlayer> Players(Memory m, IReadOnlyList<DecodedEvent> events)
    {
        var list = new List<DecodedPlayer>();
        for (var t = 0; t < 2; t++)
        {
            var side = t == 0 ? MatchSide.Home : MatchSide.Away;
            var subs = events.Where(e => e.Type == MatchEventType.Substitution && e.Side == side).ToList();
            for (var s = 0; s < Slots; s++)
            {
                if (m.U8(SlotToX + 18 * t + s) == 0xff) continue;   // chưa vào sân (ô chứa rác)
                var b = (t == 0 ? SlotsHome : SlotsAway) + SlotSize * s;
                var pes = m.U16(PesIds + 2 * (t * Slots + s));
                list.Add(new DecodedPlayer(side, s, pes == 0 ? null : pes, m.U16(b), m.U16(b + 6), m.U16(b + 0xa), m.U16(b + 0xc), m.U16(b + 0xe),
                    m.U16(b + 0x10) + m.U16(b + 0x12), subs.FirstOrDefault(e => e.Slot == s)?.Minute, subs.FirstOrDefault(e => e.OtherSlot == s)?.Minute));
            }
        }
        return list;
    }

    private sealed class Memory(IReadOnlyList<MemoryBlock> blocks)
    {
        private byte[] At(long addr, int n, out int i)
        {
            foreach (var b in blocks)
                if (addr >= b.Address && addr + n <= b.Address + b.Data.Length) { i = (int)(addr - b.Address); return b.Data; }
            throw new InvalidDataException($"Thiếu khối bộ nhớ chứa 0x{addr:x}");
        }

        public int U8(long addr) => At(addr, 1, out var i)[i];
        public int U16(long addr) { var d = At(addr, 2, out var i); return d[i] | d[i + 1] << 8; }
    }
}
