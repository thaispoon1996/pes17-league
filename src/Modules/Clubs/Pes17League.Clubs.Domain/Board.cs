namespace Pes17League.Clubs.Domain;

/// <summary>Độ tin cậy của ban lãnh đạo với CLB trong mùa (FM đợt 7). Mục tiêu = hạng kỳ vọng theo giá trị đội trong giải quốc nội.</summary>
public class BoardConfidence
{
    public Guid ClubId { get; set; }
    public Guid SeasonId { get; set; }
    public int ExpectedPosition { get; set; }
    public int Confidence { get; set; } = BoardRules.StartConfidence;
    public Guid? LastMatchdayId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public enum StaffRole { Assistant, Coach, Physio, Scout }

/// <summary>Nhân sự của CLB, cấp 1–5. Không có dòng = cấp 1 (miễn phí).</summary>
public class ClubStaff
{
    public Guid ClubId { get; set; }
    public StaffRole Role { get; set; }
    public int Level { get; set; } = 1;
    public DateTimeOffset HiredAt { get; set; }
}

/// <summary>Luật ban lãnh đạo (PO duyệt mặc định 12/11: tin cậy thấp → admin xem xét, không tự sa thải).</summary>
public static class BoardRules
{
    public const int StartConfidence = 60, ReviewBelow = 20;

    public static string Objective(int expected, int teams) =>
        expected <= 1 ? "Vô địch" : expected <= Math.Max(2, teams / 5) ? "Top " + Math.Max(2, teams / 5) : expected <= teams / 2 ? "Nửa trên BXH" : "Trụ hạng";

    /// <summary>Mỗi vòng: hạng ≤ kỳ vọng +2, kém hơn kỳ vọng 3 bậc trở lên −2; thắng +1, thua −1.</summary>
    public static int Delta(int position, int expected, int goalDiff) =>
        (position <= expected ? 2 : position >= expected + 3 ? -2 : 0) + Math.Sign(goalDiff);

    public static string Verdict(int confidence) => confidence switch
    {
        >= 80 => "Rất hài lòng",
        >= 60 => "Hài lòng",
        >= 40 => "Chấp nhận được",
        >= ReviewBelow => "Lo ngại",
        _ => "Đang xem xét vị trí HLV",
    };
}

/// <summary>Luật nhân sự. Phí ký = cấp × 1 tr; lương/vòng = (cấp−1)² × 25k.</summary>
public static class StaffRules
{
    public const int MaxLevel = 5;
    public static long SigningFee(int level) => level * 1_000_000L;
    public static long Wage(int level) => (long)(level - 1) * (level - 1) * 25_000;

    /// <summary>HLV cấp ≥ 4: thêm 1 chỉ số tăng mỗi buổi tập (khi đang tăng).</summary>
    public static int CoachGrowthBonus(int level) => level >= 4 ? 1 : 0;
    /// <summary>Y tế: hồi thể lực thêm 5/cấp, giảm rủi ro chấn thương 3‰/cấp.</summary>
    public static int PhysioRecovery(int level) => 5 * (level - 1);
    public static int PhysioInjuryCut(int level) => 3 * (level - 1);
    /// <summary>Trợ lý: cầu thủ tinh thần &lt; 50 được +1/cấp trên 1 mỗi vòng.</summary>
    public static int AssistantMorale(int level) => level - 1;

    public static string Label(StaffRole r) => r switch
    {
        StaffRole.Assistant => "Trợ lý HLV", StaffRole.Coach => "HLV tập luyện", StaffRole.Physio => "Bác sĩ / y tế", _ => "Trinh sát",
    };
    public static string Effect(StaffRole r) => r switch
    {
        StaffRole.Assistant => "Vực tinh thần cầu thủ đang buồn mỗi vòng",
        StaffRole.Coach => "Cấp 4+: thêm 1 chỉ số tăng mỗi buổi tập",
        StaffRole.Physio => "Hồi thể lực nhanh hơn, ít chấn thương hơn",
        _ => "Mở chỉ số cầu thủ CLB khác nhanh hơn (tuyển trạch)",
    };
}
