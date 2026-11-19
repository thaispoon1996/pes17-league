namespace Pes17League.Clubs.Domain;

/// <summary>
/// Người chơi career đã xem gì (UI đợt 1, PO 19/11): "intro:{tính năng}" = đã hiện thẻ mở khoá, "visit:{tính năng}" = đã vào (bỏ nhãn MỚI),
/// "result:{fixtureId}" = đã xem màn kết quả trận.
/// </summary>
public class CareerSeen
{
    public Guid ParticipantId { get; set; }
    public required string Key { get; set; }
    public DateTimeOffset At { get; set; }
}

/// <param name="Started">Đã cầm CLB trong mùa đang đá / vừa xong.</param>
/// <param name="Slot">Khung đang mở (0 = chưa mở / hết mùa).</param>
/// <param name="MyPlayed">Số trận CLB người chơi đã đá mùa này.</param>
/// <param name="HadOffer">Đã từng có đề nghị mua cầu thủ của người chơi (hoặc người chơi đã gửi đề nghị).</param>
/// <param name="ContractsExpiring">Có hợp đồng còn ≤ <see cref="CareerUnlocks.ContractWarn"/> trận.</param>
public sealed record CareerState(bool Started, bool Finished, int Slot, int Slots, int MyPlayed, bool MarketOpen, bool HadOffer, bool ContractsExpiring);

/// <summary>Tính năng career mở dần theo tiến trình mùa (bảng PO duyệt 19/11). Tính lại mỗi lần từ dữ liệu, không lưu.</summary>
public static class CareerUnlocks
{
    public const string Home = "home", Squad = "squad", League = "league", Stats = "stats", Club = "club", Finance = "finance", Board = "board",
        Transfer = "transfer", Offers = "offers", Training = "training", Contracts = "contracts", Academy = "academy", SeasonEnd = "season-end";

    public const int TrainingAfter = 3, ContractWarn = 10;

    public static IReadOnlySet<string> Unlocked(CareerState s)
    {
        var on = new HashSet<string>();
        if (!s.Started) return on;
        on.UnionWith([Home, Squad, League]);
        if (s.MyPlayed >= 1 || s.Finished) on.UnionWith([Stats, Club, Finance, Board]);
        if (s.MarketOpen) on.Add(Transfer);
        if (s.MarketOpen && s.HadOffer) on.Add(Offers);
        if (s.MyPlayed >= TrainingAfter || s.Finished) on.Add(Training);
        if (s.ContractsExpiring) on.Add(Contracts);
        if (s.Finished || (s.Slots > 0 && s.Slot >= Pes17TransferWindows.WinterStart(s.Slots))) on.Add(Academy);
        if (s.Finished) on.Add(SeasonEnd);
        return on;
    }

    /// <summary>Tính năng có thẻ giới thiệu khi mở (các mục gốc lúc bắt đầu mùa thì không).</summary>
    public static readonly IReadOnlyList<string> Introduced = [Transfer, Stats, Finance, Board, Offers, Training, Contracts, Academy];

    /// <summary>Khung đầu tiên chợ mở lại sau <paramref name="slot"/> (null = không còn trong mùa).</summary>
    public static int? NextMarketSlot(int slot, int slots)
    {
        for (var n = Math.Max(1, slot); n <= slots; n++)
            if (Pes17TransferWindows.IsOpen(n, slots)) return n;
        return null;
    }

    /// <summary>Số khung chợ còn mở kể cả khung <paramref name="slot"/> (0 = đang đóng).</summary>
    public static int MarketSlotsLeft(int slot, int slots)
    {
        var n = 0;
        while (slot + n <= slots && Pes17TransferWindows.IsOpen(slot + n, slots)) n++;
        return n;
    }
}
