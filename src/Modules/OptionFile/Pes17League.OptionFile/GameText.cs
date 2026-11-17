using System.Globalization;
using System.Text;

namespace Pes17League.OptionFile;

/// <summary>
/// Font tên cầu thủ/CLB trong game chỉ hiện ký tự Latin-1 (é, à, ö OK; ễ, ă, Đ, ộ bị trống — kiểm trong game 01/11).
/// PO chốt 01/11 (phương án a): tự bỏ dấu khi ghi OF, web vẫn giữ tên có dấu.
/// </summary>
public static class GameText
{
    public static bool IsDisplayable(string s) => s.All(ch => ch <= 'ÿ');

    /// <summary>
    /// Chuỗi đã hiển thị được thì giữ nguyên (tên gốc như "Hincapié" không đổi).
    /// Ngược lại bỏ dấu cả chuỗi cho đồng nhất ("Hà Nội FC" → "Ha Noi FC"); ký tự vẫn không hiển thị được → '?'.
    /// </summary>
    public static string ToDisplayable(string s)
    {
        if (IsDisplayable(s)) return s;
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s.Replace('Đ', 'D').Replace('đ', 'd').Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(ch <= 'ÿ' ? ch : '?');
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>
    /// Tên in áo tự sinh cho cầu thủ tạo mới (ô mẫu không có): từ cuối của tên, bỏ dấu, chữ hoa, chỉ giữ A–Z . - ' (tối đa 15).
    /// "Nguyễn Văn Đức" → "DUC"; tên không còn chữ nào → chuỗi rỗng (áo trống như ô mẫu).
    /// </summary>
    public static string ShirtName(string name)
    {
        var plain = new StringBuilder();
        foreach (var ch in name.Replace('Đ', 'D').Replace('đ', 'd').Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) plain.Append(char.ToUpperInvariant(ch));
        var word = (plain.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "")
            .Replace("Æ", "AE").Replace("Œ", "OE").Replace("Ø", "O").Replace("ß", "SS").Replace("Ł", "L");   // chữ không tách dấu được
        var s = new string([.. word.Where(ch => ch is >= 'A' and <= 'Z' or '.' or '-' or '\'')]);
        return s.Length > 15 ? s[..15] : s;
    }

    /// <summary>
    /// Mã quốc tịch theo thứ tự màn chọn quốc tịch của PES6VN 25-26 (chụp trong game 06/11, 3 trang): chỉ số = giá trị <see cref="PlayerField.Nationality"/>.
    /// Patch đổi nhãn so với PES6 gốc (vd 54 = VIE, gốc Saudi Arabia); mã trùng (PER, PAN, ECU) lấy chỉ số đầu.
    /// </summary>
    public static readonly IReadOnlyList<string> Nations =
    [
        "AUT", "BEL", "BRG", "CRO", "CZE", "DNK", "ENG", "FIN", "FRA", "DEU", "GRC", "HUN", "IRL", "ITA", "ISL", "NLD", "NIR", "NOR", "POL", "PRT",
        "ROU", "RUS", "SCO", "SRB", "SVK", "SVN", "ESP", "SWE", "SWI", "TUR", "URK", "WAL", "EGY", "CMR", "CIV", "GHA", "NGA", "SAF", "MAR", "TUN",
        "COS", "MEX", "PAN", "USA", "ARG", "BRA", "CHL", "COL", "ECU", "PAR", "PER", "URY", "IRN", "JPN", "VIE", "KOR", "AUS", "BIH", "MNE", "ISR",
        "SGP", "PHL", "LAO", "BOL", "VEN", "CHN", "UZB", "ALB", "CYP", "PER", "MKD", "ARM", "BLR", "GEO", "EST", "LTU", "DZA", "BEN", "BFA", "CPV",
        "AGO", "COD", "TCD", "GNQ", "GAB", "GMB", "GIN", "ZWE", "KEN", "ZAM", "UGA", "MLI", "TGO", "BDI", "ZAF", "COG", "HND", "CUW", "CAN", "PAN",
        "TTO", "CRI", "ECU", "IRQ", "NZL",
    ];

    /// <summary>Giá trị "Tự do" (không quốc tịch) — ô mẫu 32768+ mặc định.</summary>
    public const int FreeNationality = 105;

    /// <summary>Mã → giá trị trường quốc tịch; mã trống/không có trong game → Tự do (PO chọn 06/11).</summary>
    public static int NationIndex(string? code)
    {
        var i = code is null ? -1 : Nations.ToList().IndexOf(code.Trim().ToUpperInvariant());
        return i < 0 ? FreeNationality : i;
    }
}
