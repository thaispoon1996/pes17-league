namespace Pes17League.Clubs.Domain;

// Hòm thư (PO 17/11 duyệt): thư chính thức giữa người chơi / CLB / Ban tổ chức, có form mẫu; thư hệ thống cũng vào đây.

/// <summary>1 thư. <see cref="FromManagerId"/> null = Ban tổ chức (admin) hoặc hệ thống (<see cref="FromSystem"/>).
/// <see cref="ThreadId"/> = Id thư đầu chuỗi (trả lời giữ chung chuỗi).</summary>
public class MailMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ThreadId { get; set; }
    public Guid? FromManagerId { get; set; }
    /// <summary>CLB người gửi đang dẫn lúc gửi (ký tên thư).</summary>
    public Guid? FromClubId { get; set; }
    public bool FromSystem { get; set; }
    public required string Subject { get; set; }
    public required string Body { get; set; }
    /// <summary>Khoá form mẫu đã dùng (<see cref="MailTemplates"/>), null = thư tự soạn.</summary>
    public string? Template { get; set; }
    public Guid? PlayerId { get; set; }
    public Guid? FixtureId { get; set; }
    public DateTimeOffset At { get; set; }
    /// <summary>Người gửi cất thư khỏi mục Đã gửi.</summary>
    public bool SenderArchived { get; set; }
}

/// <summary>Người nhận 1 thư. <see cref="ManagerId"/> null = hòm Ban tổ chức. <see cref="ClubId"/>: thư gửi tới CLB (đến HLV đang dẫn CLB lúc gửi).</summary>
public class MailRecipient
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MessageId { get; set; }
    public Guid? ManagerId { get; set; }
    public Guid? ClubId { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
    public bool Archived { get; set; }
}

/// <summary>Người chơi chặn thư của người khác.</summary>
public class MailBlock
{
    public Guid ManagerId { get; set; }
    public Guid BlockedManagerId { get; set; }
    public DateTimeOffset At { get; set; }
}

public sealed record MailTemplate(string Key, string Name, string Subject, string Body, bool NeedsPlayer = false, bool NeedsFee = false,
    bool NeedsFixture = false, bool ToAdmin = false);

/// <summary>Form mẫu gửi nhanh. Chỗ trống: {to} người nhận, {me} người gửi, {club} CLB người gửi, {player} cầu thủ, {fee} giá, {fixture} trận.</summary>
public static class MailTemplates
{
    public const int SubjectMax = 150, BodyMax = 4000, PerHour = 30;

    public static readonly IReadOnlyList<MailTemplate> All =
    [
        new("hoi-mua", "Hỏi mua cầu thủ", "Đề nghị mua {player}",
            "Kính gửi {to},\n\nCLB {club} quan tâm tới cầu thủ {player} và muốn đề nghị mức phí {fee}.\nMong anh/chị phản hồi để hai bên trao đổi các điều khoản.\n\nTrân trọng,\n{me} — HLV {club}",
            NeedsPlayer: true, NeedsFee: true),
        new("trao-doi", "Đề nghị trao đổi cầu thủ", "Đề nghị trao đổi liên quan tới {player}",
            "Kính gửi {to},\n\nCLB {club} muốn đề nghị một thương vụ trao đổi liên quan tới cầu thủ {player}.\nChi tiết đề xuất: …\n\nTrân trọng,\n{me} — HLV {club}",
            NeedsPlayer: true),
        new("giao-huu", "Mời giao hữu", "Lời mời giao hữu từ {club}",
            "Kính gửi {to},\n\nCLB {club} trân trọng mời anh/chị một trận giao hữu. Thời gian dự kiến: …\nNếu đồng ý, mời anh/chị vào Sảnh giao hữu để nhận lời thách đấu.\n\nTrân trọng,\n{me} — HLV {club}"),
        new("hen-gio", "Hẹn giờ đá trận giải", "Hẹn giờ trận {fixture}",
            "Kính gửi {to},\n\nVề trận {fixture}, tôi đề xuất đá vào lúc: …\nMong anh/chị xác nhận hoặc đề xuất giờ khác trước hạn đá của vòng.\n\nTrân trọng,\n{me} — HLV {club}",
            NeedsFixture: true),
        new("chuc-mung", "Chúc mừng sau trận", "Chúc mừng chiến thắng",
            "Kính gửi {to},\n\nThay mặt CLB {club}, xin chúc mừng chiến thắng của đội anh/chị. Một trận đấu hay!\nHẹn gặp lại ở lượt sau.\n\nTrân trọng,\n{me} — HLV {club}"),
        new("chia-buon", "Chia sẻ sau trận", "Gửi lời chia sẻ sau trận",
            "Kính gửi {to},\n\nKết quả hôm nay chưa như ý, nhưng đội anh/chị đã chơi rất nỗ lực. Chúc các trận tới thành công.\n\nTrân trọng,\n{me} — HLV {club}"),
        new("khieu-nai", "Khiếu nại gửi Ban tổ chức", "Khiếu nại trận {fixture}",
            "Kính gửi Ban tổ chức,\n\nTôi xin khiếu nại về trận {fixture}.\nNội dung sự việc: …\nĐề nghị xem xét: …\n\nTrân trọng,\n{me} — HLV {club}",
            NeedsFixture: true, ToAdmin: true),
    ];

    public static MailTemplate? Find(string? key) => All.FirstOrDefault(t => t.Key == key);

    public static string Fill(string text, IReadOnlyDictionary<string, string?> values) =>
        values.Aggregate(text, (s, kv) => s.Replace("{" + kv.Key + "}", kv.Value is { Length: > 0 } v ? v : "…"));
}
