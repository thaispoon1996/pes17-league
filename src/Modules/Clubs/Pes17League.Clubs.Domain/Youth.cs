namespace Pes17League.Clubs.Domain;

/// <summary>Cầu thủ trẻ CLB đôn lên từ học viện (FM đợt 8). Dùng ô tạo cầu thủ 32768+ của OF (tối đa 184).</summary>
public class YouthIntake
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SeasonId { get; set; }
    public Guid ClubId { get; set; }
    public Guid PlayerId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Báo cáo trinh sát: CLB biết chính xác chỉ số cầu thủ khi RoundsLeft = 0.</summary>
public class ScoutReport
{
    public Guid ClubId { get; set; }
    public Guid PlayerId { get; set; }
    public int RoundsLeft { get; set; }
    public Guid? LastMatchdayId { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
}

/// <summary>Luật học viện (PO duyệt mặc định 12/11: mỗi CLB tối đa N/mùa trong giới hạn 184 ô/OF; N = 3).</summary>
public static class YouthRules
{
    public const int PerSeason = 3, ReserveSlots = 10, ContractMatches = 60;
    public const long Wage = 5_000;
    /// <summary>Chỉ số chính = mẫu trung vị vị trí trừ 8–17 (ngẫu nhiên ổn định theo seed).</summary>
    public static int Attribute(int template, int seed) => Math.Clamp(template - 8 - (int)((uint)seed % 10), 20, 99);
    public static int Age(int seed) => 16 + (int)((uint)seed % 2);

    static readonly string[] First = ["Minh", "Hoàng", "Tuấn", "Quang", "Đức", "Long", "Huy", "Khánh", "Nam", "Phúc", "Bảo", "Sơn", "Thành", "Việt", "An", "Trung"];
    static readonly string[] Last = ["Nguyễn", "Trần", "Lê", "Phạm", "Hoàng", "Vũ", "Đặng", "Bùi", "Đỗ", "Hồ", "Ngô", "Dương"];
    public static string Name(int seed) => $"{Last[(uint)seed % Last.Length]} {First[(uint)(seed / 7) % First.Length]}";
}

/// <summary>Luật trinh sát: phí 50k/báo cáo; Trinh sát cấp 1 mất 2 vòng, cấp 3–4 mất 1, cấp 5 có ngay; đồng thời tối đa 1 + cấp báo cáo đang chờ.</summary>
public static class ScoutRules
{
    public const long Fee = 50_000;
    public static int Rounds(int level) => level >= 5 ? 0 : level >= 3 ? 1 : 2;
    public static int MaxPending(int level) => 1 + level;
    /// <summary>Chỉ số chưa trinh sát: hiện khoảng 10 (vd 70–79).</summary>
    public static string Mask(int value) => value >= 90 ? "90+" : $"{value / 10 * 10}–{value / 10 * 10 + 9}";
}
