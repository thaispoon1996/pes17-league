namespace Pes17League.Clubs.Domain;

// Thưởng / phạt của Ban tổ chức (PO duyệt 17/11, docs/domain-sanctions-design.md): 1 quyết định = 1 dòng, huỷ thì ghi RevokedAt (không xoá).

/// <summary>Fine phạt tiền · Bonus thưởng tiền · PointsDeduction trừ điểm BXH · Ban cấm HLV đá N trận (trợ lý đá thay, không tính vắng) · Warning cảnh cáo.</summary>
public enum SanctionKind { Fine, Bonus, PointsDeduction, Ban, Warning }

public class Sanction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public SanctionKind Kind { get; set; }
    public Guid SeasonId { get; set; }
    public Guid? ClubId { get; set; }
    public Guid? ManagerId { get; set; }
    /// <summary>Trừ điểm: giải áp dụng (null = BXH mùa).</summary>
    public Guid? EditionId { get; set; }
    /// <summary>Tiền (Fine/Bonus, dương), điểm (PointsDeduction), số trận (Ban); Warning = 0.</summary>
    public long Amount { get; set; }
    public string Reason { get; set; } = "";
    public Guid? RefFixtureId { get; set; }
    /// <summary>Ban: các trận bị cấm (Id cách nhau dấu phẩy) — trợ lý đá thay.</summary>
    public string? BannedFixtureIds { get; set; }
    public DateTimeOffset At { get; set; }
    public string Actor { get; set; } = "";
    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevokeReason { get; set; }

    public bool Active => RevokedAt is null;
    public IReadOnlyList<Guid> BannedFixtures => string.IsNullOrEmpty(BannedFixtureIds) ? [] : [.. BannedFixtureIds.Split(',').Select(Guid.Parse)];
}

public static class SanctionRules
{
    /// <summary>Thời hạn khiếu nại quyết định qua hòm thư (PO 17/11 theo đề xuất).</summary>
    public static readonly TimeSpan AppealWindow = TimeSpan.FromHours(24);
    public const int MaxPoints = 30, MaxBan = 10;

    public static string Name(SanctionKind k) => k switch
    {
        SanctionKind.Fine => "Phạt tiền", SanctionKind.Bonus => "Thưởng", SanctionKind.PointsDeduction => "Trừ điểm",
        SanctionKind.Ban => "Cấm chỉ đạo", _ => "Cảnh cáo",
    };
}
