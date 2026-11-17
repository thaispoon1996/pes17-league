using Pes17League.OptionFile;

namespace Pes17League.Clubs.Application;

/// <summary>Cầu thủ đá chính dùng để chọn vai: Index = ô trong squad (0–10), chỉ số hiện tại của mùa.</summary>
public sealed record StarterInfo(Guid PlayerId, int Index, Position Position, int Overall, IReadOnlyDictionary<string, int> Attributes);

/// <summary>
/// Đội trưởng + người đá phạt (PO duyệt 05/11): CLB chọn cầu thủ đang đá chính thì dùng; bỏ trống hoặc người đó xuống dự bị →
/// tự chọn người tốt nhất trong 11 người. Áp dụng cho mọi CLB (kể cả BOT) vì giá trị trong patch phần lớn đã lỗi thời.
/// Tự chọn: đội trưởng = OVR cao nhất; các vai đá phạt bỏ thủ môn, chấm theo chỉ số liên quan (hoà → OVR, rồi ô nhỏ hơn).
/// </summary>
public static class TeamJobPicker
{
    private static readonly Dictionary<TeamJob, (string Field, int Weight)[]> Scores = new()
    {
        [TeamJob.Penalty] = [("SHOT ACCURACY", 2), ("SHOT TECHNIQUE", 1), ("MENTALITY", 1)],
        [TeamJob.LongFreeKick] = [("FREE KICK ACCURACY", 2), ("SHOT POWER", 1), ("SWERVE", 1)],
        [TeamJob.ShortFreeKick] = [("FREE KICK ACCURACY", 2), ("SHOT ACCURACY", 1), ("SWERVE", 1)],
        [TeamJob.LeftCorner] = [("LONG PASS ACCURACY", 2), ("SWERVE", 1), ("FREE KICK ACCURACY", 1)],
        [TeamJob.RightCorner] = [("LONG PASS ACCURACY", 2), ("SWERVE", 1), ("FREE KICK ACCURACY", 1)],
    };

    /// <summary>Người tự chọn cho vai <paramref name="job"/>; null nếu không có ai đá chính.</summary>
    public static StarterInfo? Auto(TeamJob job, IReadOnlyCollection<StarterInfo> starters)
    {
        var pool = job == TeamJob.Captain ? starters : starters.Where(s => s.Position != Position.GK).ToList();
        if (pool.Count == 0) pool = starters;
        return pool.OrderByDescending(s => job == TeamJob.Captain ? s.Overall : Scores[job].Sum(f => s.Attributes.GetValueOrDefault(f.Field) * f.Weight))
            .ThenByDescending(s => s.Overall).ThenBy(s => s.Index).FirstOrDefault();
    }

    /// <summary>Người giữ vai: cầu thủ CLB chọn nếu đang đá chính, không thì tự chọn.</summary>
    public static StarterInfo? Resolve(TeamJob job, Guid? chosen, IReadOnlyCollection<StarterInfo> starters) =>
        starters.FirstOrDefault(s => s.PlayerId == chosen) ?? Auto(job, starters);
}
