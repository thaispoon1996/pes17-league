namespace Pes17League.Clubs.Domain;

// Giải U19 (PO 25/11, giai đoạn 1): đội trẻ mỗi CLB trong giải quốc gia có CLB người chơi, đá vòng tròn 2 lượt song song giải chính,
// mỗi lần chốt một vòng giải chính thì đá một vòng U19 — kết quả MÔ PHỎNG bằng MatchSim (không đá trên máy PES).

/// <summary>Cầu thủ đội U19 (không phải cầu thủ PES; không có trong option file). Giai đoạn 2 mới cho lên đội một.</summary>
public class YouthPlayer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClubId { get; set; }
    public required string Name { get; set; }
    public Position Position { get; set; }
    /// <summary>Tuổi lúc vào đội (mùa <see cref="JoinedSeasonId"/>).</summary>
    public int Age { get; set; }
    public int Overall { get; set; }
    /// <summary>Trần phát triển (giai đoạn 2); chưa hiện cho người chơi.</summary>
    public int Potential { get; set; }
    /// <summary>Thứ tự trong đội: 1–11 đá chính (4-3-3), còn lại dự bị.</summary>
    public int Slot { get; set; }
    public Guid JoinedSeasonId { get; set; }
    public DateTimeOffset? LeftAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Trận U19: một vòng (<see cref="Round"/>) của giải U19 theo giải quốc gia <see cref="EditionId"/>.</summary>
public class YouthFixture
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EditionId { get; set; }
    public Guid SeasonId { get; set; }
    public int Round { get; set; }
    public Guid HomeClubId { get; set; }
    public Guid AwayClubId { get; set; }
    public int? HomeGoals { get; set; }
    public int? AwayGoals { get; set; }
    /// <summary>Khung giải chính lúc chốt thì vòng này được đá.</summary>
    public Guid? MatchdayId { get; set; }
    public DateTimeOffset? PlayedAt { get; set; }
}

/// <summary>Cầu thủ U19 ra sân một trận (đá chính hoặc vào sân), điểm + bàn thắng từ mô phỏng.</summary>
public class YouthAppearance
{
    public Guid FixtureId { get; set; }
    public Guid PlayerId { get; set; }
    public Guid ClubId { get; set; }
    public decimal Rating { get; set; }
    public int Goals { get; set; }
    public bool Started { get; set; }
}

public static class YouthLeagueRules
{
    /// <summary>Đội 18 người: 11 đá chính 4-3-3 rồi 7 dự bị.</summary>
    public static readonly Position[] Template =
    [
        Position.GK, Position.SB, Position.CB, Position.CB, Position.SB, Position.DMF, Position.CMF, Position.AMF, Position.WF, Position.CF, Position.WF,
        Position.GK, Position.CB, Position.SB, Position.CMF, Position.SMF, Position.SS, Position.CF,
    ];

    /// <summary>OVR nền đội U19 = OVR trung bình 18 người tốt nhất đội một trừ 17.</summary>
    public static int Base(double firstTeamAverage) => (int)Math.Round(firstTeamAverage) - 17;

    /// <summary>OVR cầu thủ: đá chính nhỉnh hơn dự bị, lệch ngẫu nhiên ±5; giới hạn 40–80.</summary>
    public static int Overall(int @base, bool starter, Random rng) => Math.Clamp(@base + (starter ? 2 : -3) + rng.Next(-5, 6), 40, 80);

    public static int Potential(int overall, Random rng) => Math.Clamp(overall + 6 + rng.Next(0, 21), overall, 94);

    public static int Age(Random rng) => 16 + rng.Next(0, 3);

    static readonly string[] First = ["Minh", "Hoàng", "Tuấn", "Quang", "Đức", "Long", "Huy", "Khánh", "Nam", "Phúc", "Bảo", "Sơn", "Thành", "Việt", "An", "Trung",
        "Hiếu", "Dũng", "Kiên", "Lâm", "Tùng", "Hải", "Vinh", "Toàn"];
    static readonly string[] Last = ["Nguyễn", "Trần", "Lê", "Phạm", "Hoàng", "Vũ", "Đặng", "Bùi", "Đỗ", "Hồ", "Ngô", "Dương", "Lý", "Mai", "Trịnh", "Phan"];
    public static string Name(Random rng) => $"{Last[rng.Next(Last.Length)]} {First[rng.Next(First.Length)]}";
}
