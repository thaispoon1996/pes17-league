namespace Pes17League.Clubs.Domain;

/// <summary>Tài khoản HLV người thật (lộ trình FM đợt 2, PO ok 12/11). Admin tạo + giao CLB (<see cref="TeamAssignment.ParticipantId"/>).
/// Admin vẫn đăng nhập bằng mật khẩu chung; phân công Human có ParticipantId null = CLB của admin (PO).</summary>
public class Manager
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Username { get; set; }
    public required string DisplayName { get; set; }
    /// <summary>PBKDF2-SHA256: "v1.{vòng lặp}.{muối base64}.{hash base64}".</summary>
    public required string PasswordHash { get; set; }
    public bool Disabled { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
}
