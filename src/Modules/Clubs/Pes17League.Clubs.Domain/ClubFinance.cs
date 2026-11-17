namespace Pes17League.Clubs.Domain;

/// <summary>Sân + fan + giá vé của CLB (FM đợt 6). Tạo lần đầu khi chốt vòng, mặc định theo thứ hạng giá trị squad.</summary>
public class ClubFinance
{
    public Guid ClubId { get; set; }
    public int Capacity { get; set; }
    public int Fans { get; set; }
    public long TicketPrice { get; set; } = FinanceRules.DefaultTicketPrice;
    /// <summary>Tài trợ mỗi vòng (chốt theo uy tín khi tạo, đổi đầu mùa).</summary>
    public long SponsorPerRound { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Thu chi mỗi vòng (FM đợt 6, PO duyệt mặc định 12/11 — số liệu chỉnh sau). Quy mô theo quỹ lương ~0,7 tr/vòng:
/// vé (trận sân nhà) + tài trợ + bản quyền TV + thưởng kết quả − bảo trì sân. Fan tăng khi thắng, giảm khi thua.
/// </summary>
public static class FinanceRules
{
    public const long DefaultTicketPrice = 12, MinTicketPrice = 5, MaxTicketPrice = 40;
    public const int MinCapacity = 15_000, MaxCapacity = 80_000, ExpandSeats = 5_000;
    public const long ExpandCost = 5_000_000;
    public const long TvPerRound = 200_000, WinPrize = 150_000, DrawPrize = 50_000;
    public const long SponsorBase = 250_000;
    /// <summary>Bảo trì sân mỗi vòng = sức chứa × 2.</summary>
    public const long UpkeepPerSeat = 2;

    /// <summary>rank 0 = squad giá trị nhất trong n CLB → hệ số uy tín 1,4; cuối → 0,6.</summary>
    public static double Reputation(int rank, int n) => n <= 1 ? 1 : 1.4 - 0.8 * rank / (n - 1);
    public static int DefaultCapacity(double reputation) => (int)Math.Round((MinCapacity + (MaxCapacity - MinCapacity) * (reputation - 0.6) / 0.8 * 0.6) / 1000) * 1000;
    public static long Sponsor(double reputation) => (long)Math.Round(SponsorBase * reputation / 1000) * 1000;

    /// <summary>Khán giả: fan × cầu theo giá vé (vé 12 → 100% fan), tối đa sức chứa.</summary>
    public static int Attendance(int capacity, int fans, long ticketPrice) =>
        Math.Min(capacity, (int)(fans * Math.Clamp(1.3 - ticketPrice / 40.0, 0.3, 1.2)));

    public static int FansAfter(int fans, int capacity, int goalDiff) =>
        Math.Clamp((int)(fans * (goalDiff > 0 ? 1.01 : goalDiff < 0 ? 0.995 : 1.0)), 1_000, capacity * 3 / 2);
}
