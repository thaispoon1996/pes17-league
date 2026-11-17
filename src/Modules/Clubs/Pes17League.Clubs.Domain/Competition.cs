namespace Pes17League.Clubs.Domain;

// M2.2 mùa/lịch/chốt vòng — docs/domain-competition-design.md (PO duyệt 01/11).

public enum SeasonStatus { Planned, Active, Finished }

public class Season
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public int Number { get; set; }
    /// <summary>Patch nhập cho mùa; null khi chưa nhập.</summary>
    public Guid? PatchVersionId { get; set; }
    public SeasonStatus Status { get; set; } = SeasonStatus.Planned;
    public DateTimeOffset CreatedAt { get; set; }
}

public class SeasonClub
{
    public Guid SeasonId { get; set; }
    public Guid ClubId { get; set; }
}

/// <summary>Planned → WindowOpen (kỳ CN) → Locked (đã sinh OF) → Playing → Completed (đủ trận) → Settled (đã chốt vòng).</summary>
public enum MatchdayStatus { Planned, WindowOpen, Locked, Playing, Completed, Settled }

public class Matchday
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SeasonId { get; set; }
    public int Number { get; set; }
    public MatchdayStatus Status { get; set; } = MatchdayStatus.Planned;
    public Guid? TransferWindowId { get; set; }
    /// <summary>OF dùng cho các trận của vòng.</summary>
    public Guid? OptionFileBuildId { get; set; }
    /// <summary>Khung ngày của cúp châu lục (league phase / vòng loại trực tiếp); null = khung quốc nội (PO chọn 06/11, bước 6).</summary>
    public FixtureStage? Stage { get; set; }
    /// <summary>Lượt 1/2 của vòng loại trực tiếp; null với league phase, chung kết và khung quốc nội.</summary>
    public int? Leg { get; set; }
}

public enum FixtureStatus { Scheduled, Playing, Played }
public enum ResultSource { Memory, Admin }

public class Fixture
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MatchdayId { get; set; }
    public Guid HomeClubId { get; set; }
    public Guid AwayClubId { get; set; }
    public FixtureStatus Status { get; set; } = FixtureStatus.Scheduled;
    public int? HomeGoals { get; set; }
    public int? AwayGoals { get; set; }
    public ResultSource? ResultSource { get; set; }
    public DateTimeOffset? PlayedAt { get; set; }
    /// <summary>Giải của trận (liên đoàn nhiều giải, 05/11); null = trận cũ trước khi chuyển (migration đã gán "Giải thử nghiệm").</summary>
    public Guid? CompetitionEditionId { get; set; }
    public FixtureStage Stage { get; set; } = FixtureStage.League;
    /// <summary>Lượt 1/2 của cặp loại trực tiếp; null = trận đơn.</summary>
    public int? Leg { get; set; }
    /// <summary>Gộp 2 lượt của cùng một cặp loại trực tiếp.</summary>
    public Guid? TieId { get; set; }
    /// <summary>Loạt penalty trong game (chỉ chung kết hoà sau 120', PO 06/11); null = không đá hoặc chưa nhập. HomeGoals/AwayGoals = tỷ số sau 120'.</summary>
    public int? HomePenalties { get; set; }
    public int? AwayPenalties { get; set; }
    /// <summary>HLV người chọn để trợ lý đá thay (COM vs COM, giữ chiến thuật của HLV) — lộ trình FM đợt 2.</summary>
    public bool AssistantPlays { get; set; }

    /// <summary>Tỷ số loạt penalty ghi được: chỉ khi hoà sau 120', không âm, không hoà (loạt penalty luôn có đội thắng).</summary>
    public static bool PenaltiesValid(int homeGoals, int awayGoals, int home, int away) =>
        homeGoals == awayGoals && home >= 0 && away >= 0 && home != away;

    /// <summary>Đội thắng trận đã đá: hơn bàn → đội đó; hoà có loạt penalty → đội thắng penalty;
    /// null = chưa đá, hoà (giải VĐQG) hoặc chung kết hoà chờ admin nhập penalty.</summary>
    public Guid? WinnerClubId() => (HomeGoals, AwayGoals, HomePenalties, AwayPenalties) switch
    {
        ({ } h, { } a, _, _) when h != a => h > a ? HomeClubId : AwayClubId,
        (not null, not null, { } ph, { } pa) when ph != pa => ph > pa ? HomeClubId : AwayClubId,
        _ => null,
    };
}

public enum MatchAttemptOutcome { Running, Finished, Crashed, Aborted }

/// <summary>Mỗi lần chạy container trận (pes6 có thể crash lúc tải trận → thử lại).</summary>
public class MatchAttempt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FixtureId { get; set; }
    public string? ContainerName { get; set; }
    public string? RecordingPath { get; set; }
    public MatchAttemptOutcome Outcome { get; set; } = MatchAttemptOutcome.Running;
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
}

/// <summary>Bàn thắng đọc từ bộ nhớ game; VideoOffsetSec = vị trí trong bản ghi hình (cắt highlight).
/// ScorerPlayerId: người ghi bàn (PO duyệt 04/11, đọc bộ nhớ); null = không xác định (phản lưới, nhập tay, trận cũ).</summary>
public class Goal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FixtureId { get; set; }
    public Guid ClubId { get; set; }
    public int Half { get; set; }
    public int Minute { get; set; }
    public double? VideoOffsetSec { get; set; }
    public Guid? ScorerPlayerId { get; set; }
}

/// <summary>
/// Điểm cầu thủ sau trận (C18, PO duyệt 04/11), đọc từ bộ nhớ game (dự phòng: chụp màn trận BOT). Slot 0–10 đá chính theo đội hình,
/// 11+ dự bị theo thứ tự vào sân. Rating 5.0–8.5 bước 0,5; null = không có điểm. Lưu ý: dự bị vào quá ít phút (≲10') bộ nhớ vẫn 6.0
/// trong khi màn ghi "–" (spike-log 04/11).
/// </summary>
public class MatchRating
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FixtureId { get; set; }
    public Guid ClubId { get; set; }
    public Guid PlayerId { get; set; }
    public int Slot { get; set; }
    public decimal? Rating { get; set; }
    // Thống kê cá nhân (B3, PO duyệt 07/11): null = trận cũ/không đọc được. Phạm lỗi gồm cả việt vị (cách game đếm).
    public int? Goals { get; set; }
    public int? Shots { get; set; }
    public int? ShotsOnTarget { get; set; }
    public int? Fouls { get; set; }
    public int? Offsides { get; set; }
    public int? Corners { get; set; }
    public int? YellowCards { get; set; }   // chưa giải mã được → null, điền lại từ file thô
    public int? RedCards { get; set; }
    public int? InMinute { get; set; }      // dự bị vào sân
    public int? OutMinute { get; set; }     // bị thay ra
}

/// <summary>Thống kê đội 1 trận (B3, PO duyệt 07/11), đọc bộ nhớ lúc hết giờ, khớp các trang "Thông số trận đấu" của game.
/// Phạm lỗi gồm cả việt vị (cách game đếm). Thẻ null = chưa giải mã được.</summary>
public class MatchTeamStats
{
    public Guid FixtureId { get; set; }
    public Guid ClubId { get; set; }
    public bool IsHome { get; set; }
    public int PossessionPct { get; set; }
    public int Shots { get; set; }
    public int ShotsOnTarget { get; set; }
    public int ShotsSaved { get; set; }
    public int ShotsInBox { get; set; }
    public int Headers { get; set; }
    public int RightFoot { get; set; }
    public int LeftFoot { get; set; }
    public int Passes { get; set; }
    public int PassesCompleted { get; set; }
    public int Crosses { get; set; }
    public int Interceptions { get; set; }
    public int Fouls { get; set; }
    public int FoulsDirect { get; set; }
    public int FoulsIndirect { get; set; }
    public int Offsides { get; set; }
    public int Corners { get; set; }
    public int CornersRight { get; set; }
    public int CornersLeft { get; set; }
    public int FreeKicks { get; set; }
    public int FreeKicksDirect { get; set; }
    public int Dribbles { get; set; }
    public int DribbleMeters { get; set; }
    public int? YellowCards { get; set; }
    public int? RedCards { get; set; }
}

public enum MatchEventType { Kickoff, Shot, Goal, Foul, FreeKick, Offside, Corner, Substitution, YellowCard, RedCard, Other }

/// <summary>Diễn biến trận (danh sách "Tường thuật" của game + thay người bắt lúc đá). Seq = thứ tự trong trận. Thay người: PlayerId vào,
/// OtherPlayerId ra. VideoOffsetSec = vị trí trong bản ghi hình (null = không ghép được mốc). RawType = mã gốc của game (thay người: -1).</summary>
public class MatchEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FixtureId { get; set; }
    public Guid ClubId { get; set; }
    public int Seq { get; set; }
    public int Half { get; set; }
    public int Minute { get; set; }
    public bool AddedTime { get; set; }
    public MatchEventType Type { get; set; }
    public int RawType { get; set; }
    public Guid? PlayerId { get; set; }
    public Guid? OtherPlayerId { get; set; }
    public bool? OnTarget { get; set; }
    public double? VideoOffsetSec { get; set; }
}

/// <summary>Lịch vòng tròn (circle method). Lượt về đối xứng: vòng n + (N−1) = vòng n đổi sân (PO chọn 01/11).</summary>
public static class RoundRobin
{
    public static IReadOnlyList<IReadOnlyList<(T Home, T Away)>> DoubleRoundRobin<T>(IReadOnlyList<T> clubs)
    {
        if (clubs.Count < 2 || clubs.Count % 2 != 0) throw new ArgumentException("Cần số CLB chẵn ≥ 2", nameof(clubs));
        var n = clubs.Count;
        var rotating = clubs.Skip(1).ToList();
        var first = new List<IReadOnlyList<(T, T)>>();
        for (var r = 0; r < n - 1; r++)
        {
            var round = new List<(T, T)>();
            // Đội cố định đổi sân mỗi vòng để xen kẽ nhà/khách.
            round.Add(r % 2 == 0 ? (clubs[0], rotating[0]) : (rotating[0], clubs[0]));
            for (var i = 1; i < n / 2; i++)
            {
                var a = rotating[i];
                var b = rotating[n - 1 - i];
                round.Add(i % 2 == 0 ? (a, b) : (b, a));
            }
            first.Add(round);
            rotating.Insert(0, rotating[^1]);
            rotating.RemoveAt(rotating.Count - 1);
        }
        return [.. first, .. first.Select(round => (IReadOnlyList<(T, T)>)round.Select(f => (f.Item2, f.Item1)).ToList())];
    }
}

public sealed record StandingRow<TId>(TId Club, int Played, int Won, int Drawn, int Lost, int GoalsFor, int GoalsAgainst)
{
    public int Points => Won * 3 + Drawn;
    public int GoalDifference => GoalsFor - GoalsAgainst;
}

public sealed record PlayedFixture<TId>(TId Home, TId Away, int HomeGoals, int AwayGoals);

/// <summary>Bảng xếp hạng: 3/1/0; bằng điểm → hiệu số → bàn thắng → đối đầu (điểm, hiệu số, bàn thắng giữa các đội bằng nhau) → tên (PO chọn 01/11).</summary>
public static class Standings
{
    public static IReadOnlyList<StandingRow<TId>> Compute<TId>(IReadOnlyCollection<TId> clubs, IReadOnlyCollection<PlayedFixture<TId>> fixtures, Func<TId, string> name)
        where TId : notnull
    {
        var table = Table(clubs, fixtures);
        return table.Values
            .GroupBy(r => (r.Points, r.GoalDifference, r.GoalsFor))
            .OrderByDescending(g => g.Key.Points).ThenByDescending(g => g.Key.GoalDifference).ThenByDescending(g => g.Key.GoalsFor)
            .SelectMany(IEnumerable<StandingRow<TId>> (g) =>
            {
                if (g.Count() == 1) return g;
                var tied = g.Select(r => r.Club).ToHashSet();
                var h2h = Table(tied, fixtures.Where(f => tied.Contains(f.Home) && tied.Contains(f.Away)).ToList());
                return g.OrderByDescending(r => h2h[r.Club].Points).ThenByDescending(r => h2h[r.Club].GoalDifference)
                    .ThenByDescending(r => h2h[r.Club].GoalsFor).ThenBy(r => name(r.Club), StringComparer.Ordinal);
            }).ToList();
    }

    private static Dictionary<TId, StandingRow<TId>> Table<TId>(IEnumerable<TId> clubs, IEnumerable<PlayedFixture<TId>> fixtures) where TId : notnull
    {
        var t = clubs.ToDictionary(c => c, c => new StandingRow<TId>(c, 0, 0, 0, 0, 0, 0));
        foreach (var f in fixtures)
        {
            t[f.Home] = Add(t[f.Home], f.HomeGoals, f.AwayGoals);
            t[f.Away] = Add(t[f.Away], f.AwayGoals, f.HomeGoals);
        }
        return t;
    }

    private static StandingRow<TId> Add<TId>(StandingRow<TId> r, int scored, int conceded) => r with
    {
        Played = r.Played + 1,
        Won = r.Won + (scored > conceded ? 1 : 0),
        Drawn = r.Drawn + (scored == conceded ? 1 : 0),
        Lost = r.Lost + (scored < conceded ? 1 : 0),
        GoalsFor = r.GoalsFor + scored,
        GoalsAgainst = r.GoalsAgainst + conceded,
    };
}

/// <summary>Phong độ alpha theo kết quả đội (PO chọn 01/11): 11 người đá chính thắng +0,5, hoà 0, thua −0,5.</summary>
public static class TeamResultForm
{
    public const double Win = 0.5, Draw = 0, Loss = -0.5;
    public static double For(int scored, int conceded) => scored > conceded ? Win : scored == conceded ? Draw : Loss;
}

/// <summary>Phong độ theo điểm cá nhân trận (PO chọn 04/11, thay <see cref="TeamResultForm"/>): (điểm − 6,0)/2 → 8,0 = +1 (+5%/vòng),
/// 5,0 = −0,5. Không đá/không có điểm → không đổi giá.</summary>
public static class RatingForm
{
    public const decimal Neutral = 6.0m;
    public static double For(decimal rating) => Math.Clamp((double)(rating - Neutral) / 2, -1, 1);
}
