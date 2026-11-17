namespace Pes17League.Clubs.Domain;

// Liên đoàn nhiều giải — docs/domain-admin-design.md (PO duyệt 05/11).
// Season = mùa bóng chung cho mọi giải (patch, chuyển nhượng, giá, tiến hoá chỉ số); Competition = giải cố định qua các mùa;
// CompetitionEdition = giải X trong mùa Y. Matchday vẫn là khung ngày của mùa, chứa trận của nhiều giải.

public enum CompetitionType { DomesticLeague, DomesticCup, ContinentalCup }
public enum CompetitionStatus { Active, Archived }

public class Competition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public string? ShortName { get; set; }
    /// <summary>Mã nước (ENG, ESP, ITA, GER); null với cúp châu lục; bắt buộc với cúp quốc gia.</summary>
    public string? Country { get; set; }
    /// <summary>Hạng (1, 2…) với giải quốc gia / C1·C2; null với cúp quốc gia.</summary>
    public int? Tier { get; set; }
    public CompetitionType Type { get; set; }
    public string? LogoPath { get; set; }
    /// <summary>Luật hiện hành; chép vào <see cref="CompetitionEdition.RulesSnapshot"/> khi edition chuyển sang Scheduled (luật khoá, A7).</summary>
    public CompetitionRules Rules { get; set; } = new();
    public CompetitionStatus Status { get; set; } = CompetitionStatus.Active;
    /// <summary>Giải hạng trên (đội lên hạng đi tới) / hạng dưới (đội xuống hạng đi tới).</summary>
    public Guid? PromotesToId { get; set; }
    public Guid? RelegatesToId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Luật giải. Giải quốc gia: vòng tròn <see cref="Legs"/> lượt, điểm thắng/hoà/thua, số đội lên/xuống hạng, suất châu Âu.
/// Cúp châu lục league phase (PO chốt 05/11): <see cref="LeaguePhaseMatches"/> trận/đội, hạng 1..<see cref="DirectKnockoutPlaces"/> vào thẳng vòng loại trực tiếp,
/// tới <see cref="PlayoffPlaces"/> đá play-off; loại trực tiếp 2 lượt, chung kết 1 trận.
/// Cúp quốc gia: knockout thuần 1 lượt (<see cref="Legs"/> = 1), không league phase; số đội = CLB hạng 1+2 cùng nước.
/// </summary>
public sealed record CompetitionRules
{
    public int PointsWin { get; init; } = 3;
    public int PointsDraw { get; init; } = 1;
    public int PointsLoss { get; init; }
    public int Legs { get; init; } = 2;
    public int PromotionPlaces { get; init; }
    public int RelegationPlaces { get; init; }
    /// <summary>Suất C1/C2 từ giải quốc gia này (theo thứ hạng).</summary>
    public int ContinentalPlaces { get; init; }
    public int SecondaryContinentalPlaces { get; init; }
    /// <summary>Số đội league phase (N, PO chốt 24).</summary>
    public int? LeaguePhaseTeams { get; init; }
    public int? LeaguePhaseMatches { get; init; }
    public int? DirectKnockoutPlaces { get; init; }
    public int? PlayoffPlaces { get; init; }
    /// <summary>Suất cho CLB châu Âu lẻ (không thuộc giải quốc gia nào): C1 8, C2 15. Còn thiếu so với N = suất thêm theo OVR (PO chọn 05/11).</summary>
    public int? LooseClubPlaces { get; init; }
}

public enum EditionStatus { Draft, Scheduled, Active, Finished }

public class CompetitionEdition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompetitionId { get; set; }
    public Guid SeasonId { get; set; }
    public EditionStatus Status { get; set; } = EditionStatus.Draft;
    /// <summary>Luật đã khoá; null khi còn Draft.</summary>
    public CompetitionRules? RulesSnapshot { get; set; }
}

public class EditionClub
{
    public Guid EditionId { get; set; }
    public Guid ClubId { get; set; }
    /// <summary>Hạt giống (1 = mạnh nhất) — chia hạng/bốc thăm theo OVR mùa đầu.</summary>
    public int? Seed { get; set; }
}

public enum FixtureStage { League, LeaguePhase, KnockoutPlayoff, RoundOf64, RoundOf32, RoundOf16, QuarterFinal, SemiFinal, Final }

/// <summary>Nhật ký thao tác admin: trước/sau dạng JSON.</summary>
public class AdminAuditEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset At { get; set; }
    public required string Actor { get; set; }
    public required string EntityType { get; set; }
    public Guid EntityId { get; set; }
    public required string Action { get; set; }
    public string? Before { get; set; }
    public string? After { get; set; }
}
