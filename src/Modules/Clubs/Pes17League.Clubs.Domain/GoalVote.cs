namespace Pes17League.Clubs.Domain;

/// <summary>Bình chọn bàn thắng đẹp nhất 1 vòng của 1 giải (PO duyệt 12/11, docs/domain-goal-vote-design.md).
/// Mở khi clip vòng dựng xong; ứng viên = các bàn trong clip. Đóng khi vòng sau khoá, hoặc tới <see cref="ClosesAt"/>
/// (vòng cuối mùa, hay vòng sau đã khoá sẵn lúc mở). <see cref="CompetitionEditionId"/> null = mùa alpha 1 giải.</summary>
public class GoalPoll
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SeasonId { get; set; }
    public Guid MatchdayId { get; set; }
    public Guid? CompetitionEditionId { get; set; }
    public DateTimeOffset OpenedAt { get; set; }
    /// <summary>Hạn đóng cố định; null = đóng khi vòng sau khoá.</summary>
    public DateTimeOffset? ClosesAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public Guid? WinnerGoalId { get; set; }
}

/// <summary>1 phiếu / thiết bị / cuộc (<see cref="VoterKey"/> = id ngẫu nhiên trong cookie); đổi phiếu = ghi đè.
/// <see cref="IpHash"/> = SHA-256(IP + muối), chỉ để chặn spam.</summary>
public class GoalVote
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PollId { get; set; }
    public Guid GoalId { get; set; }
    public required string VoterKey { get; set; }
    public DateTimeOffset CastAt { get; set; }
    public required string IpHash { get; set; }
}
