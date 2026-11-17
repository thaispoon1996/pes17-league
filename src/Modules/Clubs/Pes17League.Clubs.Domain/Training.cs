namespace Pes17League.Clubs.Domain;

public enum TrainingFocus { Balanced, Attack, Technique, Defence, Physical, Goalkeeping }
public enum TrainingIntensity { Light, Normal, Hard }

/// <summary>Giáo án tập luyện của CLB (FM đợt 4). CLB chưa đặt = Balanced/Normal.</summary>
public class ClubTraining
{
    public Guid ClubId { get; set; }
    public TrainingFocus Focus { get; set; }
    public TrainingIntensity Intensity { get; set; } = TrainingIntensity.Normal;
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Phát triển cầu thủ (FM đợt 4, PO duyệt mặc định 12/11): cứ <see cref="EveryRounds"/> vòng tập luyện 1 lần khi chốt vòng.
/// Trẻ (≤23) còn dưới tiềm năng → +1 ở 1–3 chỉ số trọng tâm (theo cường độ); 24–29 còn dưới tiềm năng → +1 ở 1 chỉ số (trừ Light);
/// ≥31 → −1 chỉ số thể lực (trọng tâm Thể lực thì không giảm; Hard thì giảm 2). Chung trần ±3/mùa với tiến hoá theo trận.
/// Tiềm năng ẩn: chỉ số tổng đầu mùa + phần dư theo tuổi, cố định theo Id cầu thủ.
/// </summary>
public static class TrainingRules
{
    public const int EveryRounds = 5;
    public const int YouthMaxAge = 23, PrimeMaxAge = 29, DeclineAge = 31;

    public static readonly string[] PhysicalFields = ["TOP SPEED", "ACCELERATION", "STAMINA"];

    public static IReadOnlyList<string> Fields(TrainingFocus focus, IEnumerable<string> roleFields) => focus switch
    {
        TrainingFocus.Attack => ["ATTACK", "SHOT ACCURACY", "SHOT POWER", "SHOT TECHNIQUE"],
        TrainingFocus.Technique => ["TECHNIQUE", "DRIBBLE ACCURACY", "SHORT PASS ACCURACY", "LONG PASS ACCURACY"],
        TrainingFocus.Defence => ["DEFENSE", "BALANCE", "HEADING", "RESPONSE"],
        TrainingFocus.Physical => ["TOP SPEED", "ACCELERATION", "STAMINA", "JUMP"],
        TrainingFocus.Goalkeeping => ["GOAL KEEPING", "RESPONSE", "AGILITY", "JUMP"],
        _ => roleFields.ToList(),
    };

    public static int GrowthFields(int age, TrainingIntensity intensity) =>
        age <= YouthMaxAge ? (int)intensity + 1 : age <= PrimeMaxAge && intensity != TrainingIntensity.Light ? 1 : 0;

    public static int DeclineFields(int age, TrainingFocus focus, TrainingIntensity intensity) =>
        age < DeclineAge || focus == TrainingFocus.Physical ? 0 : intensity == TrainingIntensity.Hard ? 2 : 1;

    /// <summary>Tiềm năng ẩn (0–99): ≤21 thêm 5–14, 22–24 thêm 2–7, 25–28 thêm 0–2, còn lại 0.</summary>
    public static int Potential(Guid playerId, int startOverall, int age)
    {
        var h = Hash(playerId);
        var extra = age <= 21 ? 5 + h % 10 : age <= 24 ? 2 + h % 6 : age <= 28 ? h % 3 : 0;
        return Math.Min(99, startOverall + extra);
    }

    /// <summary>Đánh giá tiềm năng hiển thị (1–5 sao, nửa sao) — che số thật.</summary>
    public static double Stars(int potential) => Math.Clamp(Math.Round((potential - 55) / 8.0 * 2, MidpointRounding.AwayFromZero) / 2, 0.5, 5);

    /// <summary>Chọn <paramref name="count"/> chỉ số xoay vòng theo lần tập (ổn định, không ngẫu nhiên).</summary>
    public static IEnumerable<string> Pick(IReadOnlyList<string> fields, int count, Guid playerId, int session)
    {
        if (fields.Count == 0) yield break;
        var start = (Hash(playerId) + session) % fields.Count;
        for (var i = 0; i < Math.Min(count, fields.Count); i++) yield return fields[(start + i) % fields.Count];
    }

    private static int Hash(Guid id)
    {
        var b = id.ToByteArray();
        return (int)((uint)(b[0] | b[3] << 8 | b[7] << 16 | b[11] << 24) % 1_000_003);
    }
}
