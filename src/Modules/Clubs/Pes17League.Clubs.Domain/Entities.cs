namespace Pes17League.Clubs.Domain;

// Thực thể theo docs/domain-club-squad-design.md (PO duyệt 01/11). Không phụ thuộc EF/PES6.
// SeasonId/MatchdayId chỉ là Guid — bảng Season/Matchday thuộc M2.2, làm sau.

public enum ClubStatus { Active, Inactive }
public enum ControllerType { Human, Bot }

public class Club
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Tên hiển thị web (có dấu). Tên trong game = bỏ dấu, chỉ đổi đầu mùa (C15).</summary>
    public required string Name { get; set; }
    public string? ShortName { get; set; }
    public ClubStatus Status { get; set; } = ClubStatus.Active;
    /// <summary>Mã nước (ENG, ESP…); null = chưa gán.</summary>
    public string? Country { get; set; }
    /// <summary>Đường dẫn logo tĩnh trên web (chụp từ game hoặc admin tải lên).</summary>
    public string? LogoPath { get; set; }
    /// <summary>Tăng mỗi lần squad đổi — chặn 2 giao dịch đồng thời cùng CLB (optimistic concurrency).</summary>
    public int SquadVersion { get; set; }
}

public class TeamAssignment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClubId { get; set; }
    public Guid? ParticipantId { get; set; }
    public ControllerType ControllerType { get; set; }
    public DateTimeOffset From { get; set; }
    public DateTimeOffset? To { get; set; }
}

public class FootballPlayer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public Position Position { get; set; }
    public int? BirthYear { get; set; }
    public string? Nationality { get; set; }
    /// <summary>CLB gốc = CLB trong patch lúc nhập (Kinh tế đợt 2): hết hợp đồng thì về đây. Null = gốc tự do hoặc CLB không tham gia giải.</summary>
    public Guid? OriginClubId { get; set; }
    /// <summary>Hiện trên thị trường: null = theo luật (cầu thủ tự do thuộc dải ẩn/ML thì ẩn); true/false = admin ép.</summary>
    public bool? MarketVisibility { get; set; }
}

/// <summary>Snapshot chỉ số mỗi mùa, nhập từ OF đầu mùa. Khoá = tên trường (theo PlayerField.Name của module OF).</summary>
public class PlayerAttributes
{
    public Guid PlayerId { get; set; }
    public Guid SeasonId { get; set; }
    public Dictionary<string, int> Values { get; set; } = [];
    public DateTimeOffset CapturedAt { get; set; }
}

/// <summary>Match = tiến hoá sau trận (C19); Admin = admin sửa (bước 3c), có hiệu lực ở OF vòng sau (A7).</summary>
public enum AttributeChangeSource { Match, Admin, Training }

/// <summary>Chỉ số đổi sau 1 trận (C19) hoặc do admin sửa. Chỉ số hiện tại = snapshot đầu mùa (<see cref="PlayerAttributes"/>) + tổng Delta của mùa.</summary>
public class AttributeChange
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SeasonId { get; set; }
    public Guid PlayerId { get; set; }
    /// <summary>Trận gây ra thay đổi; null = admin sửa.</summary>
    public Guid? FixtureId { get; set; }
    public AttributeChangeSource Source { get; set; } = AttributeChangeSource.Match;
    public string Field { get; set; } = "";
    public int Delta { get; set; }
    /// <summary>Lúc ghi; null = dòng trước bước 3c.</summary>
    public DateTimeOffset? At { get; set; }
}

/// <summary>Squad hiện tại = các dòng LeftAt null. SlotOrder 0–10 = đá chính.</summary>
public class SquadMembership
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClubId { get; set; }
    public Guid PlayerId { get; set; }
    public int ShirtNumber { get; set; }
    public int SlotOrder { get; set; }
    public DateTimeOffset JoinedAt { get; set; }
    public DateTimeOffset? LeftAt { get; set; }
}

public enum ContractStatus { Active, Ended }
public enum ContractEndReason { Transferred, Released, Renewed, ReturnedToOrigin, BecameFree, Retired, LoanReturned }

/// <summary>Hợp đồng theo số trận (C16: 10–60). Wage = lương mỗi vòng; null = chưa đặt (hợp đồng nhập từ patch, đặt khi khởi động mùa).</summary>
public class PlayerContract
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PlayerId { get; set; }
    public Guid ClubId { get; set; }
    public int? MatchesTotal { get; set; }
    public int MatchesPlayed { get; set; }
    public long? Wage { get; set; }
    public ContractStatus Status { get; set; } = ContractStatus.Active;
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public ContractEndReason? EndReason { get; set; }
    /// <summary>Phí giải phóng (FM đợt 3): đề nghị mua ≥ mức này tự hoàn tất, không cần CLB bán đồng ý. Null = không có.</summary>
    public long? ReleaseClause { get; set; }
    /// <summary>Hợp đồng mượn: CLB chủ quản; hết hạn mượn thì về CLB này với hợp đồng cũ (số trận còn lại + lương cũ).</summary>
    public Guid? LoanFromClubId { get; set; }
    public int? LoanParentMatchesLeft { get; set; }
    public long? LoanParentWage { get; set; }
}

public enum RenewalStatus { Pending, Completed, Rejected, Cancelled }

/// <summary>
/// Gia hạn hợp đồng (Kinh tế đợt 2): CLB hiện tại đề nghị số trận + phí trả CLB gốc (0–100% giá); lương = tỷ lệ admin × giá.
/// Cần chủ CLB gốc đồng ý; CLB gốc là BOT / không tham gia giải / gốc tự do / chính CLB hiện tại → tự đồng ý.
/// </summary>
public class ContractRenewal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SeasonId { get; set; }
    public Guid ContractId { get; set; }
    public Guid PlayerId { get; set; }
    public Guid ClubId { get; set; }
    /// <summary>CLB nhận phí (CLB gốc); null = không cần ai đồng ý, phí phải 0.</summary>
    public Guid? OriginClubId { get; set; }
    public int Matches { get; set; }
    public long Fee { get; set; }
    public long Wage { get; set; }
    public RenewalStatus Status { get; set; }
    public bool AutoApproved { get; set; }
    public string? RejectReason { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
}

public enum TransferWindowStatus { Planned, Open, Closed }

public class TransferWindow
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SeasonId { get; set; }
    /// <summary>C12: kỳ mở/đóng theo vòng — vòng mà giao dịch trong kỳ này sẽ có hiệu lực trong game (OF sinh khi chốt vòng).</summary>
    public Guid? MatchdayId { get; set; }
    public DateTimeOffset OpensAt { get; set; }
    public DateTimeOffset ClosesAt { get; set; }
    public TransferWindowStatus Status { get; set; } = TransferWindowStatus.Planned;

    public bool IsOpenAt(DateTimeOffset t) => Status == TransferWindowStatus.Open && t >= OpensAt && t < ClosesAt;
}

public enum TransferKind { Buy, Release, Loan, Swap }
public enum TransferStatus { Completed, Rejected }

/// <summary>From null = cầu thủ tự do; To null = thanh lý. Fee: tự do / CLB BOT = đúng giá; giữa 2 CLB có chủ 80–200% giá.</summary>
public class Transfer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TransferWindowId { get; set; }
    public Guid PlayerId { get; set; }
    public Guid? FromClubId { get; set; }
    public Guid? ToClubId { get; set; }
    public long Fee { get; set; }
    /// <summary>Hợp đồng mới khi mua (C16: 10–60 trận); lương mỗi vòng.</summary>
    public int? ContractMatches { get; set; }
    public long? Wage { get; set; }
    public TransferKind Kind { get; set; }
    public TransferStatus Status { get; set; }
    public string? RejectReason { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    /// <summary>Bán cầu thủ đá chính mà không có dự bị cùng vị trí → CLB bán cần xếp lại đội hình.</summary>
    public bool ResultingLineupReview { get; set; }
    /// <summary>Trả góp (FM đợt 3): phí chia đều N kỳ, kỳ 1 trả ngay, các kỳ sau trừ khi chốt vòng.</summary>
    public int Instalments { get; set; } = 1;
    public int InstalmentsPaid { get; set; } = 1;
    /// <summary>% bán lại: khi CLB mua bán tiếp cầu thủ này, CLB bán (FromClubId) nhận % phí.</summary>
    public int SellOnPercent { get; set; }
    public Guid? OfferId { get; set; }
}

public enum LedgerReason { InitialBudget, TransferIn, TransferOut, Adjustment, Wage, LuxuryTax, RenewalFee, SellOn, Ticket, Sponsor, TvRights, Prize, Facility, Staff, Upgrade }

/// <summary>Sổ tiền ảo của CLB. Số dư = tổng Amount. Chỉ ghi khi Transfer hoàn tất (hoặc admin điều chỉnh).</summary>
public class ClubLedgerEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClubId { get; set; }
    public long Amount { get; set; }
    public LedgerReason Reason { get; set; }
    public string? RefType { get; set; }
    public Guid? RefId { get; set; }
    public DateTimeOffset At { get; set; }
}

public class PatchVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public required string BaseOptionFileSha256 { get; set; }
    /// <summary>Đường dẫn OF gốc trong kho file (không lưu file trong DB).</summary>
    public required string BaseOptionFilePath { get; set; }
    public DateTimeOffset ImportedAt { get; set; }
}

public class ClubPesMapping
{
    public Guid ClubId { get; set; }
    public Guid PatchVersionId { get; set; }
    public int PesClubIndex { get; set; }
}

public class PlayerPesMapping
{
    public Guid PlayerId { get; set; }
    public Guid PatchVersionId { get; set; }
    public int PesPlayerId { get; set; }
    /// <summary>CLB trong OF lúc nhập (0–139, kể cả CLB không tham gia giải); null = tự do trong patch.</summary>
    public int? OriginPesClubIndex { get; set; }
}

public enum OptionFileBuildStatus { Built, Failed }

public class OptionFileBuild
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PatchVersionId { get; set; }
    public Guid? MatchdayId { get; set; }
    public string? Sha256 { get; set; }
    public string? StoragePath { get; set; }
    public DateTimeOffset BuiltAt { get; set; }
    public OptionFileBuildStatus Status { get; set; }
    /// <summary>JSON: squad từng CLB (PES club index → [PES player id, số áo]) đã ghi; hoặc danh sách lỗi khi Failed.</summary>
    public required string SquadSnapshot { get; set; }
    public string? Errors { get; set; }
}

/// <summary>
/// Sơ đồ + thiết lập đội CLB chọn trên web (PO duyệt 03/11). Không có dòng / trường null = giữ như OF gốc của patch.
/// Formation = tên sơ đồ mẫu PES6 ("4-4-2 A"…); thiết lập 0/1/2 = A/B/C. Ghi vào OF lúc khoá vòng; trận của PO lấy bản lúc vào trận.
/// </summary>
public class ClubTactics
{
    public Guid ClubId { get; set; }
    public string? Formation { get; set; }
    public int? BackLine { get; set; }
    public int? Pressure { get; set; }
    public int? OffsideTrap { get; set; }
    public int? CounterAttack { get; set; }
    /// <summary>Vị trí tự do ô 1–10 (PO duyệt 05/11): 20 số X1,Y1..X10,Y10 theo toạ độ OF; null = theo sơ đồ. Đổi sơ đồ → xoá.</summary>
    public int[]? Positions { get; set; }
    /// <summary>4 chiến thuật phím L2 (○ ✕ Select □), mỗi giá trị 0..11 như menu game; null = theo patch.</summary>
    public int[]? Strategies { get; set; }
    // Đội trưởng + người đá phạt (PO duyệt 05/11): null hoặc cầu thủ không đá chính → tự chọn người tốt nhất trong 11 người (TeamJobPicker).
    public Guid? CaptainId { get; set; }
    public Guid? PenaltyTakerId { get; set; }
    public Guid? LongFreeKickTakerId { get; set; }
    public Guid? ShortFreeKickTakerId { get; set; }
    public Guid? LeftCornerTakerId { get; set; }
    public Guid? RightCornerTakerId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
