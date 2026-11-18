namespace Pes17League.Clubs.Domain;

/// <summary>Đơn đăng ký đá giải (PO 16/11): người chơi chưa có CLB xin 1 CLB (đang do BOT cầm) → admin duyệt (giao CLB, có thể
/// khác CLB xin) hoặc từ chối. Mỗi người tối đa 1 đơn đang chờ.</summary>
public enum LeagueApplicationStatus { Pending, Approved, Rejected, Withdrawn }

public class LeagueApplication
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ManagerId { get; set; }
    /// <summary>CLB người chơi muốn cầm (null = để admin chọn).</summary>
    public Guid? PreferredClubId { get; set; }
    public string? Note { get; set; }
    public LeagueApplicationStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecidedBy { get; set; }
    /// <summary>CLB admin đã giao khi duyệt.</summary>
    public Guid? ClubId { get; set; }
    /// <summary>Lý do từ chối.</summary>
    public string? Reason { get; set; }
}
