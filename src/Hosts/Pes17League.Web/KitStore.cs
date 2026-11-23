namespace Pes17League.Web;

/// <summary>Áo đấu CLB dựng sẵn từ texture áo trong game (tools/pes17db/kits.py, PO 20/11): $DATA/kits/{ClubId:N}-{1st|2nd|3rd|gk}.webp,
/// phục vụ tĩnh ở <see cref="RequestPath"/>. CLB game không có áo → danh sách rỗng (web ẩn mục áo, không vẽ áo giả).</summary>
public sealed record KitStore(string Dir)
{
    public const string RequestPath = "/img/ao";

    /// <param name="Css">lớp CSS của ô áo (home/away/third/gk)</param>
    /// <param name="File">mã bộ áo như tên file / lựa chọn trận (1st, 2nd, 3rd, gk)</param>
    /// <param name="Color">màu chủ đạo thân áo (#rrggbb, tools/pes17db/kitcolors.py); null = chưa tính</param>
    public sealed record Kit(string Css, string Label, string Url, string File = "", string? Color = null);

    static readonly (string File, string Css, string Label)[] Slots =
        [("1st", "home", "Sân nhà"), ("2nd", "away", "Sân khách"), ("3rd", "third", "Thứ ba"), ("gk", "gk", "Thủ môn")];

    /// <summary>Các bộ áo có ảnh của CLB theo thứ tự sân nhà → khách → thứ ba → thủ môn; URL kèm ?v= theo giờ sửa file (dựng lại → trình duyệt lấy ảnh mới).</summary>
    public IReadOnlyList<Kit> For(Guid clubId)
    {
        var id = clubId.ToString("N");
        var kits = new List<Kit>(Slots.Length);
        foreach (var (file, css, label) in Slots)
        {
            var f = new FileInfo(Path.Combine(Dir, $"{id}-{file}.webp"));
            if (f.Exists) kits.Add(new(css, label, $"{RequestPath}/{f.Name}?v={f.LastWriteTimeUtc.Ticks:x}", file, Colors().GetValueOrDefault($"{id}-{file}")));
        }
        return kits;
    }

    Dictionary<string, string> _colors = [];
    DateTime _colorsAt;

    /// <summary>$DATA/kits/colors.json (kitcolors.py), đọc lại khi file đổi.</summary>
    Dictionary<string, string> Colors()
    {
        var f = new FileInfo(Path.Combine(Dir, "colors.json"));
        if (!f.Exists) return _colors;
        if (f.LastWriteTimeUtc != _colorsAt)
        {
            try { _colors = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(f.FullName)) ?? []; }
            catch (Exception e) when (e is IOException or System.Text.Json.JsonException) { return _colors; }
            _colorsAt = f.LastWriteTimeUtc;
        }
        return _colors;
    }

    /// <summary>2 màu áo gần nhau quá (khoảng cách "redmean" &lt; 130 / 765) → dễ lẫn trên sân.</summary>
    public static bool Clash(string? a, string? b)
    {
        if (a is not { Length: 7 } || b is not { Length: 7 }) return false;
        int R(string c, int i) => Convert.ToInt32(c.Substring(1 + 2 * i, 2), 16);
        double rm = (R(a, 0) + R(b, 0)) / 2.0, dr = R(a, 0) - R(b, 0), dg = R(a, 1) - R(b, 1), db = R(a, 2) - R(b, 2);
        return Math.Sqrt((2 + rm / 256) * dr * dr + 4 * dg * dg + (2 + (255 - rm) / 256) * db * db) < 130;
    }

    /// <summary>Đội máy (BOT) tự chọn áo (PO 23/11): sân nhà 1st→2nd→3rd, sân khách 2nd→3rd→1st, bỏ bộ trùng màu với áo đối thủ.</summary>
    public string? Auto(Guid clubId, bool home, string? otherColor)
    {
        var outfield = For(clubId).Where(k => k.File != "gk").ToList();
        if (outfield.Count == 0) return null;
        string[] order = home ? ["1st", "2nd", "3rd"] : ["2nd", "3rd", "1st"];
        var ranked = order.Select(o => outfield.FirstOrDefault(k => k.File == o)).OfType<Kit>().ToList();
        return (ranked.FirstOrDefault(k => !Clash(k.Color, otherColor)) ?? ranked.FirstOrDefault())?.File;
    }
}
