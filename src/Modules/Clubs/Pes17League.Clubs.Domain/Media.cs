namespace Pes17League.Clubs.Domain;

/// <summary>Giọng họp báo sau trận (FM đợt 9).</summary>
public enum PressTone { Praise, Calm, Demanding }

/// <summary>Kiểu nói chuyện riêng với cầu thủ (FM đợt 9).</summary>
public enum TalkKind { Praise, Criticise, Encourage }

/// <summary>Họp báo sau trận: mỗi CLB 1 lần/trận.</summary>
public class PressConference
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FixtureId { get; set; }
    public Guid ClubId { get; set; }
    public PressTone Tone { get; set; }
    public int MoraleDelta { get; set; }
    public DateTimeOffset At { get; set; }
}

/// <summary>Nói chuyện với cầu thủ: mỗi cầu thủ 1 lần/vòng (RoundKey = vòng chưa chốt sớm nhất).</summary>
public class PlayerTalk
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PlayerId { get; set; }
    public Guid ClubId { get; set; }
    public Guid RoundKey { get; set; }
    public TalkKind Kind { get; set; }
    public int MoraleDelta { get; set; }
    public DateTimeOffset At { get; set; }
}

/// <summary>Luật truyền thông (PO duyệt mặc định 12/11). Tinh thần cả đội / từng cầu thủ, CĐV theo phần nghìn.</summary>
public static class MediaRules
{
    /// <summary>(tinh thần cả đội, CĐV ‰) theo giọng + kết quả (hiệu số bàn của CLB).</summary>
    public static (int Morale, int FansPermille) Press(PressTone tone, int goalDiff) => (tone, Math.Sign(goalDiff)) switch
    {
        (PressTone.Praise, 1) => (3, 2),
        (PressTone.Praise, 0) => (2, 0),
        (PressTone.Praise, _) => (2, -3),      // bênh cầu thủ sau trận thua: đội vui, fan không
        (PressTone.Calm, _) => (1, 0),
        (PressTone.Demanding, 1) => (-2, 1),   // đòi hỏi sau trận thắng: cầu thủ hơi tự ái
        (PressTone.Demanding, 0) => (0, 2),
        (PressTone.Demanding, _) => (-3, 5),   // chê sau trận thua: fan đồng tình, cầu thủ tụt tinh thần
    };

    /// <summary>Tác động nói chuyện, theo điểm trận gần nhất + tinh thần hiện tại.</summary>
    public static int Talk(TalkKind kind, decimal? lastRating, int morale) => kind switch
    {
        TalkKind.Praise => lastRating >= 7 ? 6 : lastRating is null || lastRating > 5 ? 2 : -2,
        TalkKind.Criticise => lastRating <= 5 ? (morale >= 50 ? 4 : -6) : -8,
        _ => morale < 50 ? 4 : 1,
    };

    /// <summary>Tâm trạng CĐV theo tỉ lệ điểm 5 trận gần nhất.</summary>
    public static string FanMood(int points, int games) => games == 0 ? "Chờ đợi" : (points * 100 / (games * 3)) switch
    {
        >= 80 => "Phấn khích",
        >= 60 => "Hài lòng",
        >= 33 => "Bình thường",
        >= 15 => "Thất vọng",
        _ => "Giận dữ",
    };
}
