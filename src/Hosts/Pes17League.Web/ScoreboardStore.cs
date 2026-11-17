using Pes17League.Clubs.Application;

namespace Pes17League.Web;

/// <summary>Ảnh gắn lên bảng tỷ số trong game ($DATA/scoreboards). Trận sau pesmatch.py đọc file này.</summary>
public sealed record ScoreboardStore(string Dir)
{
    public const string RequestPath = "/img/scoreboard";
    public const long MaxBytes = 4 << 20;
    static readonly string[] Exts = [".png", ".jpg", ".webp"];

    public string? WebPath(Guid competitionId, bool board = false)
    {
        var name = FileName(competitionId, board);
        return name is null ? null : $"{RequestPath}/{name}";
    }

    public async Task SaveAsync(Guid competitionId, byte[] data, bool board = false, CancellationToken ct = default)
    {
        var ext = LogoStore.Extension(data) ?? throw new AdminRuleException("Ảnh phải là PNG, JPEG hoặc WebP");
        Directory.CreateDirectory(Dir);
        Clear(competitionId, board);
        await File.WriteAllBytesAsync(Path.Combine(Dir, Stem(competitionId, board) + ext), data, ct);
    }

    public void Clear(Guid competitionId, bool board = false)
    {
        foreach (var ext in Exts)
        {
            var path = Path.Combine(Dir, Stem(competitionId, board) + ext);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    static string Stem(Guid competitionId, bool board) => competitionId.ToString("N") + (board ? "-board" : "");

    string? FileName(Guid competitionId, bool board)
    {
        foreach (var ext in Exts)
        {
            var name = Stem(competitionId, board) + ext;
            if (File.Exists(Path.Combine(Dir, name))) return name;
        }
        return null;
    }
}
