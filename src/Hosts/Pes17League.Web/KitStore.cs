namespace Pes17League.Web;

/// <summary>Áo đấu CLB dựng sẵn từ texture áo trong game (tools/pes17db/kits.py, PO 20/11): $DATA/kits/{ClubId:N}-{1st|2nd|3rd|gk}.webp,
/// phục vụ tĩnh ở <see cref="RequestPath"/>. CLB game không có áo → danh sách rỗng (web ẩn mục áo, không vẽ áo giả).</summary>
public sealed record KitStore(string Dir)
{
    public const string RequestPath = "/img/ao";

    /// <param name="Css">lớp CSS của ô áo (home/away/third/gk)</param>
    public sealed record Kit(string Css, string Label, string Url);

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
            if (f.Exists) kits.Add(new(css, label, $"{RequestPath}/{f.Name}?v={f.LastWriteTimeUtc.Ticks:x}"));
        }
        return kits;
    }
}
