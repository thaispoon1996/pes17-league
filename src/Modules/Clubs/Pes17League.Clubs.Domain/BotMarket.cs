namespace Pes17League.Clubs.Domain;

/// <summary>Cầu thủ trên chợ BOT: OVR / giá mùa, tuổi, CLB (null = tự do).</summary>
public sealed record MarketPlayer(Guid Id, Position Position, int Ovr, int Age, long Price, Guid? ClubId);

/// <summary>Đội hình chính theo tuyến của 1 CLB: <paramref name="Level"/> = OVR TB 11 người đá chính (1 TM, 4 HV, 4 TV, 2 TĐ);
/// <paramref name="Group"/> = tuyến có người đá chính yếu nhất (<paramref name="Weakest"/>, thiếu người = 0).</summary>
public sealed record SquadNeed(double Level, PositionGroup Group, int Weakest);

/// <summary>
/// Chợ chuyển nhượng BOT career (PO 19/11 mục 4, tham số đề xuất — chỉnh được). Chỉ chạy khi chợ PES17 mở (2 kỳ CN), mỗi khung 1 lần:
/// <list type="bullet">
/// <item>BOT ↔ BOT: tối đa <see cref="DealsPerSlot"/> CLB BOT mua 1 người vá tuyến yếu nhất: hơn người đá chính yếu nhất tuyến đó, không quá
/// trình độ đội + <see cref="LevelMargin"/>, ≤ <see cref="MaxAge"/> tuổi, giá ≤ <see cref="MaxBudgetPercent"/>% tiền đang có; phí = đúng giá (luật BOT).
/// CLB bán là BOT còn ≥ <see cref="SellerKeeps"/> người, chỉ bán người đá chính cho CLB mạnh hơn mình. Squad mua > <see cref="TrimAbove"/> → thanh lý người yếu nhất.</item>
/// <item>BOT hỏi mua cầu thủ người chơi: mỗi khung <see cref="OfferChancePercent"/>% có 1 đề nghị (tối đa <see cref="MaxOpenOffersToHuman"/> đề nghị đang chờ),
/// giá <see cref="MinOfferPercent"/>–<see cref="MaxOfferPercent"/>% giá mô hình. Người chơi trả giá → BOT đồng ý nếu ≤ mức trần riêng của đề nghị
/// (<see cref="MinWalkAwayPercent"/>–<see cref="MaxWalkAwayPercent"/>%), không thì rút.</item>
/// </list>
/// </summary>
public static class BotMarketRules
{
    public const int DealsPerSlot = 30, MaxAge = 30, SellerKeeps = 18, BuyerMaxSquad = 30, TrimAbove = 28, LevelMargin = 4, MaxBudgetPercent = 60, KeepGoalkeepers = 2;
    public const int OfferChancePercent = 60, MaxOpenOffersToHuman = 3, MinOfferPercent = 100, MaxOfferPercent = 130;
    public const int MinWalkAwayPercent = 130, MaxWalkAwayPercent = 170;

    static readonly (PositionGroup Group, int Count)[] Shape =
        [(PositionGroup.Goalkeeper, 1), (PositionGroup.Defender, 4), (PositionGroup.Midfielder, 4), (PositionGroup.Forward, 2)];

    public static SquadNeed Need(IEnumerable<(PositionGroup Group, int Ovr)> squad)
    {
        var byGroup = squad.GroupBy(p => p.Group).ToDictionary(g => g.Key, g => g.Select(p => p.Ovr).OrderDescending().ToList());
        var starters = new List<int>();
        (PositionGroup Group, int Weakest)? need = null;
        foreach (var (g, n) in Shape)
        {
            var top = byGroup.GetValueOrDefault(g, []).Take(n).ToList();
            starters.AddRange(top);
            var weakest = top.Count < n ? 0 : top[^1];
            if (need is null || weakest < need.Value.Weakest) need = (g, weakest);
        }
        return new(starters.Count == 0 ? 0 : starters.Average(), need!.Value.Group, need.Value.Weakest);
    }

    /// <summary>11 người đá chính theo <see cref="Shape"/> (OVR cao nhất mỗi tuyến).</summary>
    public static HashSet<Guid> Starters(IEnumerable<MarketPlayer> squad) =>
        squad.GroupBy(p => p.Position.Group())
            .SelectMany(g => g.OrderByDescending(p => p.Ovr).ThenBy(p => p.Id).Take(Shape.FirstOrDefault(s => s.Group == g.Key).Count))
            .Select(p => p.Id).ToHashSet();

    /// <summary>Người thanh lý khi squad quá đông: không đá chính, không nằm trong <paramref name="keep"/>, giữ ≥ <see cref="KeepGoalkeepers"/> thủ môn;
    /// OVR thấp nhất, rồi già nhất.</summary>
    public static MarketPlayer? TrimPick(IReadOnlyCollection<MarketPlayer> squad, ISet<Guid> starters, ISet<Guid> keep)
    {
        var gk = squad.Count(p => p.Position.Group() == PositionGroup.Goalkeeper);
        return squad.Where(p => !starters.Contains(p.Id) && !keep.Contains(p.Id)
                && (p.Position.Group() != PositionGroup.Goalkeeper || gk > KeepGoalkeepers))
            .OrderBy(p => p.Ovr).ThenByDescending(p => p.Age).ThenBy(p => p.Id).FirstOrDefault();
    }

    /// <summary>Người BOT muốn mua: đúng tuyến cần, hơn người yếu nhất, không quá trình độ đội, đủ trẻ, đủ tiền.</summary>
    public static bool Fits(MarketPlayer p, SquadNeed need, long balance) =>
        p.Position.Group() == need.Group && p.Ovr > need.Weakest && p.Ovr <= need.Level + LevelMargin && p.Age <= MaxAge
        && p.Price > 0 && p.Price * 100 <= balance * MaxBudgetPercent;

    /// <summary>CLB bán BOT nhả người: còn đủ người sau khi bán; người đá chính chỉ sang CLB mạnh hơn.</summary>
    public static bool SellerLets(int sellerSquad, bool starter, double sellerLevel, double buyerLevel) =>
        sellerSquad > SellerKeeps && (!starter || buyerLevel > sellerLevel);

    /// <summary>Chọn người tốt nhất (OVR cao, trẻ hơn, rẻ hơn) trong danh sách hợp.</summary>
    public static MarketPlayer? Best(IEnumerable<MarketPlayer> fits) =>
        fits.OrderByDescending(p => p.Ovr).ThenBy(p => p.Age).ThenBy(p => p.Price).FirstOrDefault();

    /// <summary>Phí BOT hỏi mua cầu thủ người chơi, làm tròn 10.000.</summary>
    public static long OfferFee(long price, int roll) => Round(price * (MinOfferPercent + roll % (MaxOfferPercent - MinOfferPercent + 1)) / 100);

    /// <summary>Mức trần BOT chấp nhận khi bị trả giá (ổn định theo đề nghị).</summary>
    public static long WalkAway(long price, Guid offerId) =>
        Round(price * (MinWalkAwayPercent + (int)(BitConverter.ToUInt32(offerId.ToByteArray(), 0) % (MaxWalkAwayPercent - MinWalkAwayPercent + 1))) / 100);

    static long Round(long v) => Math.Max(10_000, v / 10_000 * 10_000);
}
