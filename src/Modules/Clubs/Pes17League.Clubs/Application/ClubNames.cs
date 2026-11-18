using System.Text;

namespace Pes17League.Clubs.Application;

/// <summary>
/// Chuẩn hoá tên CLB hiển thị (PO 18/11, sửa UI trước thương mại hoá): patch HANO ghi kiểu "Arsenal Fc", "Afc Bournemouth",
/// "Paris Saint-germain", có cả chữ Hy Lạp lẫn vào ("Sunderland Αfc"). Chỉ sửa từ đang ở dạng Hoa-đầu-thường-sau; giữ nguyên tên đã viết đúng.
/// </summary>
public static class ClubNames
{
    static readonly HashSet<string> Acronyms = new(StringComparer.OrdinalIgnoreCase)
    {
        "FC", "AFC", "CF", "CD", "CA", "AC", "SC", "US", "UD", "SD", "KRC", "KVC", "KV", "RFC", "SV", "PSV", "BSC", "EC", "FBC", "CR", "CSD",
        "FK", "SK", "IF", "AD", "AZ", "PEC", "RC", "SCO", "FCO", "CP", "CYD", "KSV", "SFC", "VV", "ESTAC", "AEK", "PAOK", "CSKA", "BK", "IFK",
        "AIK", "OGC", "LOSC", "OSC", "RB", "RCD", "HSV", "LP", "MG", "TSG", "SSC", "AS", "CS", "NK", "HNK", "GNK", "SL", "FCV", "PFC", "SE",
        "SCR", "SG", "ACF", "USL", "UC", "CFR", "TSV", "FSV", "SPAL", "RSC", "KAA", "KAS", "OH", "YB", "AFK", "SCP", "IK", "FF", "BV", "ESC",
    };
    static readonly HashSet<string> Lower = new(StringComparer.Ordinal) { "De", "Da", "Do", "Del", "Y", "Di", "Des", "Du", "Of", "And" };
    static readonly Dictionary<string, string> Special = new(StringComparer.OrdinalIgnoreCase) { ["VfB"] = "VfB", ["VfL"] = "VfL", ["VfR"] = "VfR" };
    // Chữ Hy Lạp/Kirin trông giống Latin, patch gõ nhầm vào tên Latin
    static readonly Dictionary<char, char> Confusable = new()
    {
        ['Α'] = 'A', ['Β'] = 'B', ['Ε'] = 'E', ['Ζ'] = 'Z', ['Η'] = 'H', ['Ι'] = 'I', ['Κ'] = 'K', ['Μ'] = 'M', ['Ν'] = 'N', ['Ο'] = 'O',
        ['Ρ'] = 'P', ['Τ'] = 'T', ['Υ'] = 'Y', ['Χ'] = 'X', ['α'] = 'a', ['ο'] = 'o',
    };

    public static string Pretty(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return name;
        var words = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < words.Length; i++)
        {
            var w = FixScript(words[i]);
            if (w.EndsWith("'S", StringComparison.Ordinal)) w = w[..^1] + "s";   // Newell'S → Newell's
            if (Special.TryGetValue(w, out var sp)) w = sp;
            else if (IsTitle(w))
            {
                if (Acronyms.Contains(w)) w = w.ToUpperInvariant();
                else if (i > 0 && i < words.Length - 1 && Lower.Contains(w)) w = w.ToLowerInvariant();
            }
            words[i] = string.Join('-', w.Split('-').Select((part, k) => k > 0 && part.Length > 1 && char.IsLower(part[0]) ? char.ToUpperInvariant(part[0]) + part[1..] : part));
        }
        return string.Join(' ', words);
    }

    /// <summary>Từ lẫn chữ Latin và chữ giống-Latin (Hy Lạp) → đổi hết sang Latin. Từ thuần Hy Lạp giữ nguyên.</summary>
    static string FixScript(string w)
    {
        if (!w.Any(c => c is >= 'A' and <= 'z') || !w.Any(Confusable.ContainsKey)) return w;
        var sb = new StringBuilder(w.Length);
        foreach (var c in w) sb.Append(Confusable.TryGetValue(c, out var l) ? l : c);
        return sb.ToString();
    }

    static bool IsTitle(string w) => w.Length > 0 && char.IsUpper(w[0]) && w.Skip(1).All(c => !char.IsLetter(c) || char.IsLower(c));
}
