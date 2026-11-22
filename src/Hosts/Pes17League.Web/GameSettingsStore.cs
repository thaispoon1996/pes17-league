using System.Text.Json;

namespace Pes17League.Web;

/// <summary>Cài đặt máy game dùng chung (PO 22/11): độ khó máy (Match Level) + đồ hoạ (settings.dat Quality).
/// Lưu ở $DATA/pes17-deploy/game-settings.json; bộ chạy trận (tools/match17/pesmatch17.py game_settings()) đọc đầu mỗi trận.</summary>
public sealed class GameSettingsStore(string path)
{
    public sealed record Settings(string Level = "Regular", string Quality = "high");

    /// <summary>Tên đúng như trong game (bộ chạy OCR so tên) → nhãn tiếng Việt.</summary>
    public static readonly (string Key, string Label)[] Levels =
    [
        ("Beginner", "Mới chơi"), ("Amateur", "Nghiệp dư"), ("Regular", "Bình thường"),
        ("Professional", "Chuyên nghiệp"), ("Top Player", "Ngôi sao"), ("Superstar", "Siêu sao"),
    ];

    public static readonly (string Key, string Label, string Note)[] Qualities =
    [
        ("low", "Thấp", "Nhẹ máy nhất"), ("medium", "Trung bình", "Cân bằng"), ("high", "Cao", "Đẹp nhất"),
    ];

    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    readonly object gate = new();

    public Settings Get()
    {
        lock (gate)
        {
            try
            {
                var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), Json) ?? new();
                return s with
                {
                    Level = Levels.Any(l => l.Key == s.Level) ? s.Level : "Regular",
                    Quality = Qualities.Any(q => q.Key == s.Quality) ? s.Quality : "high",
                };
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
            {
                return new();
            }
        }
    }

    public void Save(Settings s)
    {
        if (!Levels.Any(l => l.Key == s.Level) || !Qualities.Any(q => q.Key == s.Quality))
            throw new ArgumentException("cài đặt game không hợp lệ");
        lock (gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(s, Json));
            File.Move(tmp, path, true);
        }
    }
}
