using System.Text.Json;

namespace Pes17League.Web;

/// <summary>Cài đặt máy game dùng chung (PO 22/11): độ khó máy (Match Level) + đồ hoạ (settings.dat Quality) + camera (PO 25/11).
/// Lưu ở $DATA/pes17-deploy/game-settings.json; bộ chạy trận (tools/match17/pesmatch17.py game_settings()) đọc đầu mỗi trận.</summary>
public sealed class GameSettingsStore(string path)
{
    /// <summary>Camera Custom trong trận: Zoom luôn 0 (xa nhất); Height/Angle 0–<see cref="CamMax"/> (PES2017.exe đã vá thanh 10→50).
    /// Bộ chạy đặt qua Pause Menu lúc giao bóng, chỉ khi khác lần trước (tools/match17/pesmatch17.py ensure_camera).</summary>
    public sealed record Settings(string Level = "Regular", string Quality = "high", int CamHeight = CamHeightDefault, int CamAngle = CamAngleDefault);

    public const int CamMax = 50, CamHeightDefault = 15, CamAngleDefault = 10;
    static bool CamOk(int v) => v is >= 0 and <= CamMax;

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
                    CamHeight = CamOk(s.CamHeight) ? s.CamHeight : CamHeightDefault,
                    CamAngle = CamOk(s.CamAngle) ? s.CamAngle : CamAngleDefault,
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
        if (!Levels.Any(l => l.Key == s.Level) || !Qualities.Any(q => q.Key == s.Quality) || !CamOk(s.CamHeight) || !CamOk(s.CamAngle))
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
