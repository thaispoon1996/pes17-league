using Pes17League.Clubs.Domain;

namespace Pes17League.Clubs.Application;

/// <summary>Một mục thông số chiến thuật PES17: <see cref="Options"/> = 2 phương án (giá trị 0/1), null = thanh mức 1..10.</summary>
public sealed record Pes17PlanItem(string Key, string Label, string Game, string[]? Options, bool Attack)
{
    public bool IsSlider => Options is null;
    public bool Valid(int v) => IsSlider ? v is >= 1 and <= 10 : v is 0 or 1;
}

/// <summary>
/// Thông số chiến thuật PES17 trên web (PO chọn 21/11: mùa PES17 thay 4 thiết lập A/B/C kiểu PES6 bằng đúng mục của game).
/// Thứ tự = cột <see cref="ClubTactics.Pes17Plan"/>; Key = khoá "_plan" của tools/pes17db/pesdb.py apply (PLAN_SLIDERS/PLAN_OPTIONS).
/// Build Up / Attacking Zone / Pressure chưa tìm ra trong dữ liệu game → không có trên web (game giữ của đội).
/// </summary>
public static class Pes17Plan
{
    public const int Unset = -1;

    public static readonly IReadOnlyList<Pes17PlanItem> Items =
    [
        new("attackStyle", "Kiểu tấn công", "Attacking Style", ["Phản công", "Kiểm soát bóng"], true),
        new("positioning", "Vị trí khi tấn công", "Positioning", ["Giữ đội hình", "Linh hoạt"], true),
        new("defenceStyle", "Kiểu phòng ngự", "Defensive Style", ["Pressing tầm cao", "Lùi về phòng ngự"], false),
        new("containment", "Vùng ép bóng", "Containment Area", ["Trung lộ", "Hai biên"], false),
        new("supportRange", "Khoảng cách hỗ trợ", "Support Range", null, true),
        new("defensiveLine", "Hàng thủ dâng cao", "Defensive Line", null, false),
        new("compactness", "Độ co cụm", "Compactness", null, false),
    ];

    /// <summary>7 giá trị (null = theo game) từ cột đã lưu.</summary>
    public static int?[] Of(ClubTactics? t) =>
        t?.Pes17Plan is { } p && p.Length == Items.Count ? [.. p.Select(v => v == Unset ? (int?)null : v)] : new int?[Items.Count];

    /// <summary>Khoá "_plan" cho pesdb.py: chỉ các mục CLB đã chọn; null nếu không chọn mục nào.</summary>
    public static Dictionary<string, int>? ToGame(ClubTactics? t)
    {
        var v = Of(t);
        var d = Items.Select((it, i) => (it.Key, V: v[i])).Where(x => x.V is not null).ToDictionary(x => x.Key, x => x.V!.Value);
        return d.Count == 0 ? null : d;
    }
}
