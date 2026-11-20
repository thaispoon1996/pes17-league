using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Components;
using Pes17League.Clubs.Domain;

namespace Pes17League.Web;

/// <summary>Hiển thị hồ sơ HLV career (PO 19/11): tiền ví, mốc vòng, radar năng lực, nhãn thư.</summary>
public static class CoachUi
{
    /// <summary>Tiền ví HLV (lương / chi tiêu vài chục nghìn mỗi vòng → "€35K"); cùng cách ghi với Fmt.Money.</summary>
    public static string Cash(long v) => MoneyText.Format(v);

    public static string Signed(long v) => (v > 0 ? "+" : "") + Cash(v);

    public static string Signed(int v) => v > 0 ? $"+{v}" : v.ToString(CultureInfo.InvariantCulture);

    /// <summary>Mốc = mùa × 100 + vòng đã chốt.</summary>
    public static string Tick(int tick) => tick <= 0 ? "" : tick % 100 == 0 ? $"Đầu mùa {tick / 100}" : $"Mùa {tick / 100} · vòng {tick % 100}";

    public static string? FaceUrl(Coach c) => c.Face is null ? null : $"hlv/anh/mat/{c.Id}?v={c.UpdatedAt.UtcTicks}";
    /// <summary>Ảnh mặt đã tách nền (lưu PNG trong suốt, PO 20/11).</summary>
    public static bool FaceCut(Coach c) => c.Face is { Length: > 4 } f && f[0] == 0x89 && f[1] == 0x50;
    /// <summary>Kiểu ảnh theo byte đầu (PNG / JPEG).</summary>
    public static string ImageType(byte[] b) => b is { Length: > 4 } && b[0] == 0x89 && b[1] == 0x50 ? "image/png" : "image/jpeg";

    public static string License(int l) => l <= 0 ? "Chưa có bằng" : "Bằng " + CoachRules.Licenses[Math.Min(l, CoachRules.Licenses.Length - 1)];

    public static string TierName(string card) => card switch { "legend" => "Huyền thoại", "gold" => "Vàng", "silver" => "Bạc", _ => "Đồng" };

    static string F(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);

    /// <summary>Radar 7 năng lực (thang 20): vòng 25/50/75/100%, đa giác gốc (nét đứt cyan) và hiện tại (vàng).</summary>
    public static MarkupString Radar(int[] skills, int[]? baseSkills = null)
    {
        const double R = 64;
        var n = CoachRules.SkillCount;
        (double X, double Y) P(int i, double r) { var a = -Math.PI / 2 + i * 2 * Math.PI / n; return (Math.Cos(a) * r, Math.Sin(a) * r); }
        string Poly(Func<int, double> r) => string.Join(' ', Enumerable.Range(0, n).Select(i => { var (x, y) = P(i, r(i)); return $"{F(x)},{F(y)}"; }));
        var sb = new StringBuilder("<svg class=\"hlv-radar\" viewBox=\"-150 -98 300 196\" role=\"img\" aria-label=\"Biểu đồ năng lực\">");
        foreach (var k in new[] { .25, .5, .75, 1 })
            sb.Append($"<polygon points=\"{Poly(_ => R * k)}\" fill=\"none\" stroke=\"rgba(255,255,255,{(k == 1 ? ".22" : ".08")})\"/>");
        for (var i = 0; i < n; i++) { var (x, y) = P(i, R); sb.Append($"<line x1=\"0\" y1=\"0\" x2=\"{F(x)}\" y2=\"{F(y)}\" stroke=\"rgba(255,255,255,.08)\"/>"); }
        if (baseSkills is { Length: > 0 } && !baseSkills.SequenceEqual(skills))
            sb.Append($"<polygon points=\"{Poly(i => R * Math.Clamp(baseSkills[i], 0, 20) / 20.0)}\" fill=\"rgba(54,227,255,.08)\" stroke=\"#36E3FF\" stroke-dasharray=\"3 3\"/>");
        sb.Append($"<polygon points=\"{Poly(i => R * Math.Clamp(skills[i], 0, 20) / 20.0)}\" fill=\"rgba(255,201,74,.28)\" stroke=\"#FFC94A\" stroke-width=\"2\" stroke-linejoin=\"round\"/>");
        for (var i = 0; i < n; i++)
        {
            var (x, y) = P(i, R * 1.28);
            var anchor = Math.Abs(x) < 8 ? "middle" : x > 0 ? "start" : "end";
            sb.Append($"<text x=\"{F(x)}\" y=\"{F(y - 2)}\" text-anchor=\"{anchor}\">{CoachRules.SkillDefs[i].Short}</text>");
            sb.Append($"<text class=\"v\" x=\"{F(x)}\" y=\"{F(y + 12)}\" text-anchor=\"{anchor}\">{skills[i]}</text>");
        }
        return new MarkupString(sb.Append("</svg>").ToString());
    }

    public static string Icon(LetterFrom f) => f switch
    {
        LetterFrom.Board => "account_balance",
        LetterFrom.Player => "person",
        LetterFrom.Federation => "verified",
        LetterFrom.Press => "newspaper",
        LetterFrom.Agent => "badge",
        LetterFrom.Family => "family_restroom",
        LetterFrom.Fans => "groups",
        LetterFrom.Sponsor => "storefront",
        LetterFrom.Staff => "sports",
        LetterFrom.Club => "shield",
        _ => "send",
    };

    public static string KindName(LetterKind k) => k switch
    {
        LetterKind.Official => "Chính thức",
        LetterKind.Decision => "Cần quyết định",
        LetterKind.Request => "Yêu cầu đã gửi",
        LetterKind.Reply => "Hồi âm",
        _ => "Thông tin",
    };

    public static string Money(CoachMoney k) => k switch
    {
        CoachMoney.Wage => "Lương",
        CoachMoney.Bonus => "Thưởng",
        CoachMoney.Severance => "Bồi thường hợp đồng",
        CoachMoney.Living => "Sinh hoạt",
        CoachMoney.Asset => "Mua tài sản",
        CoachMoney.AssetSale => "Bán tài sản",
        CoachMoney.Course => "Học phí",
        CoachMoney.Event => "Sự kiện",
        CoachMoney.Investment => "Đầu tư",
        CoachMoney.Upkeep => "Bảo dưỡng tài sản",
        _ => "Tài trợ",
    };

    public static readonly string[] HairColors = ["#2a1d14", "#0e0c0b", "#5a3a22", "#8f7a5c", "#bfbfbf"];
    public static readonly string[] SkinTones = ["#f1c9a5", "#e0ac84", "#c99a78", "#a5735a", "#7a4f37", "#4f3326"];
    public static readonly string[] Nations = ["Việt Nam", "Anh", "Tây Ban Nha", "Ý", "Đức", "Pháp", "Bồ Đào Nha", "Hà Lan", "Brazil", "Argentina"];

    public static string HairName(string h) => h switch { "quiff" => "Vuốt cao", "buzz" => "Cắt sát", "bald" => "Hói", _ => "Ngắn" };
    public static string OutfitName(string o) => o switch { "track" => "Đồ tập", "polo" => "Áo polo", _ => "Com-lê" };
}
