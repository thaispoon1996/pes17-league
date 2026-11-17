namespace Pes17League.OptionFile;

/// <summary>
/// Sơ đồ + thiết lập đội của 1 CLB (PO duyệt 03/11). Null = giữ nguyên giá trị trong OF gốc.
/// Thiết lập đội: 0/1/2 = A/B/C như menu game (Back line, Pressure, Offside Trap, Counter Attack).
/// Giai đoạn 2 (spike 05/11, xác minh trong game): Positions = X/Y tự do của ô 1–10 (đè lên sơ đồ, cả 3 bộ);
/// Strategies = 4 chiến thuật phím L2 (○ ✕ Select □), giá trị <see cref="TeamStrategies.Names"/>;
/// Jobs = chỉ số ô trong squad (0..31) theo thứ tự <see cref="TeamJob"/>, null từng phần tử = giữ như OF gốc.
/// </summary>
public sealed record TeamTactics(string? Formation, int? BackLine = null, int? Pressure = null, int? OffsideTrap = null, int? CounterAttack = null,
    IReadOnlyList<(int X, int Y)>? Positions = null, IReadOnlyList<int?>? Strategies = null, IReadOnlyList<int?>? Jobs = null);

/// <summary>Người đá phạt / đội trưởng — thứ tự đúng thứ tự byte 111..116 của khối đội hình (spike 05/11).</summary>
public enum TeamJob { LongFreeKick, ShortFreeKick, LeftCorner, RightCorner, Penalty, Captain }

/// <summary>4 chiến thuật phím L2 (byte 102..105: ○ ✕ Select □). Giá trị = thứ tự mục trong menu "Thiết lập chế độ" (spike 05/11).</summary>
public static class TeamStrategies
{
    public static readonly IReadOnlyList<string> Buttons = ["○", "✕", "Select", "□"];
    public static readonly IReadOnlyList<string> Names =
    [
        "Không chiến thuật", "Công trung lộ", "Công cánh phải", "Công cánh trái", "Công đối diện", "Đảo cánh",
        "Trung vệ tấn công", "Pressing", "Phòng ngự phản công", "Bẫy việt vị", "Đội hình A", "Đội hình B",
    ];
}

/// <summary>Giới hạn X/Y khi kéo cầu thủ tự do (sơ đồ mẫu dùng X 9..43, Y 17..87; game nhận X 42 cho trung vệ, spike 05/11).</summary>
public static class FormationBounds
{
    public const int MinX = 6, MaxX = 46, MinY = 8, MaxY = 96;
}

/// <summary>Bố cục đội hình (vẽ sân trên web): X/Y của ô 1–10 (X = độ sâu, ~9 hàng thủ → ~43 tiền đạo; Y = ngang, ~19 trái → ~85 phải)
/// và vai trò 11 ô (ô 0 = thủ môn).</summary>
public sealed record FormationLayout(IReadOnlyList<(int X, int Y)> Positions, IReadOnlyList<string> Roles)
{
    internal static FormationLayout From(ReadOnlySpan<byte> span)
    {
        var b = span.ToArray();
        return new([.. Enumerable.Range(0, 10).Select(i => ((int)b[2 * i], (int)b[2 * i + 1]))], [.. b[20..31].Select(r => Formations.RoleName(r))]);
    }
}

/// <summary>
/// 21 sơ đồ mẫu của PES6 và tên vai trò. Dữ kiện (bố cục khối đội hình, bảng sơ đồ, mã vai trò) lấy từ PES-Editor-6 (GPL-3.0),
/// xác minh trong game 03/11 (ghi 3-4-3 A cho MU → radar lúc giao bóng ra 3 hậu vệ). Mã ở đây viết mới.
/// Mỗi mẫu 31 byte: X/Y của 10 cầu thủ ngoài thủ môn (ô 1–10) rồi mã vai trò của 11 ô.
/// </summary>
public static class Formations
{
    public static readonly IReadOnlyList<string> Names =
    [
        "4-5-1 A", "4-5-1 B", "4-4-2 A", "4-4-2 B", "4-4-2 C", "4-3-3 A", "4-3-3 B", "4-3-3 C",
        "3-6-1 A", "3-6-1 B", "3-5-2 A", "3-5-2 B", "3-4-3 A", "3-4-3 B", "3-4-3 C",
        "5-4-1 A", "5-4-1 B", "5-4-1 C", "5-3-2 A", "5-3-2 B", "5-3-2 C",
    ];

    internal const int Length = 31;

    private static readonly byte[][] Presets =
    [
        [9, 63, 9, 41, 11, 85, 11, 19, 18, 52, 26, 75, 26, 29, 34, 64, 34, 40, 43, 52, 0, 7, 1, 9, 8, 12, 23, 22, 28, 24, 38],
        [9, 63, 9, 41, 11, 85, 11, 19, 18, 61, 18, 43, 26, 75, 26, 29, 34, 52, 43, 52, 0, 7, 1, 9, 8, 14, 10, 23, 22, 26, 38],
        [9, 63, 9, 41, 11, 85, 11, 19, 18, 52, 26, 75, 26, 29, 34, 52, 43, 66, 43, 38, 0, 7, 1, 9, 8, 12, 23, 22, 26, 40, 36],
        [9, 63, 9, 41, 11, 85, 11, 19, 18, 61, 18, 43, 30, 70, 30, 34, 43, 66, 43, 38, 0, 7, 1, 9, 8, 14, 10, 28, 24, 40, 36],
        [9, 63, 9, 41, 11, 85, 11, 19, 26, 77, 26, 61, 26, 43, 26, 27, 43, 66, 43, 38, 0, 7, 1, 9, 8, 20, 21, 17, 18, 40, 36],
        [9, 63, 9, 41, 11, 85, 11, 19, 18, 52, 30, 64, 30, 40, 43, 72, 43, 32, 43, 52, 0, 7, 1, 9, 8, 12, 28, 24, 30, 29, 38],
        [9, 63, 9, 41, 11, 85, 11, 19, 18, 61, 18, 43, 30, 52, 43, 72, 43, 32, 43, 52, 0, 7, 1, 9, 8, 14, 10, 26, 30, 29, 38],
        [9, 63, 9, 41, 11, 85, 11, 19, 26, 77, 26, 52, 26, 27, 43, 72, 43, 32, 43, 52, 0, 7, 1, 9, 8, 21, 19, 17, 30, 29, 38],
        [9, 72, 9, 52, 9, 32, 18, 52, 26, 52, 26, 77, 26, 27, 34, 64, 34, 40, 43, 52, 0, 7, 3, 1, 12, 19, 23, 22, 28, 24, 38],
        [9, 72, 9, 52, 9, 32, 18, 61, 18, 43, 26, 77, 26, 27, 34, 64, 34, 40, 43, 52, 0, 7, 3, 1, 14, 10, 23, 22, 28, 24, 38],
        [9, 72, 9, 52, 9, 32, 18, 52, 26, 77, 26, 27, 34, 64, 34, 40, 43, 66, 43, 38, 0, 7, 3, 1, 12, 23, 22, 28, 24, 40, 36],
        [9, 72, 9, 52, 9, 32, 18, 61, 18, 43, 26, 77, 26, 27, 34, 52, 43, 66, 43, 38, 0, 7, 3, 1, 14, 10, 23, 22, 26, 40, 36],
        [9, 72, 9, 52, 9, 32, 18, 52, 26, 77, 26, 27, 34, 52, 43, 72, 43, 32, 43, 52, 0, 7, 3, 1, 12, 23, 22, 26, 30, 29, 38],
        [9, 72, 9, 52, 9, 32, 18, 61, 18, 43, 30, 70, 30, 34, 43, 72, 43, 32, 43, 52, 0, 7, 3, 1, 14, 10, 28, 24, 30, 29, 38],
        [9, 72, 9, 52, 9, 32, 26, 77, 26, 61, 26, 43, 26, 27, 43, 72, 43, 32, 43, 52, 0, 7, 3, 1, 20, 21, 17, 18, 30, 29, 38],
        [9, 72, 9, 52, 9, 32, 12, 87, 12, 17, 18, 52, 26, 75, 26, 29, 34, 52, 43, 52, 0, 7, 3, 1, 9, 8, 12, 23, 22, 26, 38],
        [9, 72, 9, 52, 9, 32, 12, 87, 12, 17, 18, 61, 18, 43, 30, 70, 30, 34, 43, 52, 0, 7, 3, 1, 9, 8, 14, 10, 28, 24, 38],
        [9, 72, 9, 52, 9, 32, 12, 87, 12, 17, 26, 77, 26, 61, 26, 43, 26, 27, 43, 52, 0, 7, 3, 1, 9, 8, 20, 21, 17, 18, 38],
        [9, 72, 9, 52, 9, 32, 12, 87, 12, 17, 18, 52, 34, 64, 34, 40, 43, 66, 43, 38, 0, 7, 3, 1, 9, 8, 12, 28, 24, 40, 36],
        [9, 72, 9, 52, 9, 32, 12, 87, 12, 17, 18, 61, 18, 43, 34, 52, 43, 66, 43, 38, 0, 7, 3, 1, 9, 8, 14, 10, 26, 40, 36],
        [9, 72, 9, 52, 9, 32, 12, 87, 12, 17, 26, 77, 26, 52, 26, 27, 43, 66, 43, 38, 0, 7, 3, 1, 9, 8, 21, 19, 17, 40, 36],
    ];

    internal static ReadOnlySpan<byte> Preset(string name)
    {
        var i = Names.ToList().IndexOf(name);
        if (i < 0) throw new ArgumentException($"Sơ đồ không có: {name}", nameof(name));
        return Presets[i];
    }

    public static FormationLayout Layout(string name) => FormationLayout.From(Preset(name));

    /// <summary>Vai trò của 11 ô (ô 0 = thủ môn) trong sơ đồ mẫu.</summary>
    public static IReadOnlyList<string> Roles(string name) => [.. Preset(name)[20..].ToArray().Select(r => RoleName(r))];

    public static string RoleName(int code) => code switch
    {
        0 => "GK",
        4 => "CWP",
        5 => "ASW",
        < 8 => "CB",
        8 => "LB",
        9 => "RB",
        < 15 => "DM",
        15 => "LWB",
        16 => "RWB",
        < 22 => "CM",
        22 => "LM",
        23 => "RM",
        < 29 => "AM",
        29 => "LW",
        30 => "RW",
        < 36 => "SS",
        _ => "CF",
    };
}
