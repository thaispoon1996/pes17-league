namespace Pes17League.Clubs.Domain;

public enum OfferKind { Permanent, Loan }
public enum OfferStatus { Pending, Countered, Completed, Rejected, Withdrawn, Expired, Failed }

/// <summary>
/// Đề nghị chuyển nhượng giữa 2 CLB có HLV người (FM đợt 3). CLB bán: chấp nhận / từ chối / trả giá (CounterFee);
/// CLB mua: chấp nhận giá trả / rút. Mua đứt có thể kèm cầu thủ trao đổi, % bán lại, trả góp; mượn có phí mượn + số trận.
/// Kỳ chuyển nhượng đóng thì đề nghị còn treo → Expired.
/// </summary>
public class TransferOffer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TransferWindowId { get; set; }
    public Guid PlayerId { get; set; }
    public Guid BuyerClubId { get; set; }
    public Guid SellerClubId { get; set; }
    public OfferKind Kind { get; set; }
    public long Fee { get; set; }
    public long? CounterFee { get; set; }
    /// <summary>Cầu thủ của CLB mua đưa sang CLB bán (trao đổi).</summary>
    public Guid? SwapPlayerId { get; set; }
    public int SellOnPercent { get; set; }
    public int Instalments { get; set; } = 1;
    /// <summary>Mua đứt: độ dài hợp đồng mới; mượn: số trận mượn.</summary>
    public int ContractMatches { get; set; }
    public long Wage { get; set; }
    public OfferStatus Status { get; set; }
    public string? Note { get; set; }
    public string? FailReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RespondedAt { get; set; }
    public Guid? TransferId { get; set; }

    public long AgreedFee => CounterFee ?? Fee;
    public bool IsOpen => Status is OfferStatus.Pending or OfferStatus.Countered;
}
