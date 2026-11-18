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
    /// <summary>Người chơi tự đăng ký bằng mã mời (PO 16/11); false = admin tạo.</summary>
    public bool SelfRegistered { get; set; }
    /// <summary>Elo giao hữu xếp hạng (PO 16/11): khởi điểm 1000, giữ suốt đời.</summary>
    public int Elo { get; set; } = FriendlyRules.StartElo;
}

/// <summary>Mã mời đăng ký (PO 16/11, giai đoạn alpha): admin tạo, mỗi mã dùng 1 lần.</summary>
public class InviteCode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Code { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public required string CreatedBy { get; set; }
    public string? Note { get; set; }
    public Guid? UsedByManagerId { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
    public bool Revoked { get; set; }
}
