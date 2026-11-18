namespace Pes17League.Clubs.Domain;

// Giao hữu người vs người (PO duyệt 16/11, docs/domain-friendly-design.md): 2 người CHUNG 1 máy giao hữu riêng; ghép trận,
// chọn đội, đội hình, chat làm hết trên web; vào game chỉ đá.

/// <summary>Free = tự chọn đội của patch (kể cả đội tuyển, trùng đội được), chỉ vào hồ sơ. Ranked = CLB thật, đổi Elo, có tiền + bảng điểm.</summary>
public enum FriendlyMode { Free, Ranked }

/// <summary>Invited (thách đấu, chờ nhận) → Picking (phòng chọn đội) → Queued (chờ máy) → Playing → Played / Abandoned / Voided.
/// Declined / Expired / Cancelled = chưa đá.</summary>
public enum FriendlyStatus { Invited, Picking, Queued, Playing, Played, Abandoned, Voided, Declined, Expired, Cancelled }

public class FriendlyMatch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public FriendlyMode Mode { get; set; }
    public FriendlyStatus Status { get; set; }
    public Guid HomeManagerId { get; set; }
    public Guid AwayManagerId { get; set; }
    /// <summary>Ranked: CLB thật của 2 người + mùa tính bảng điểm.</summary>
    public Guid? HomeClubId { get; set; }
    public Guid? AwayClubId { get; set; }
    public Guid? SeasonId { get; set; }
    /// <summary>Free: chỉ số đội trong patch (đội tuyển + CLB, theo menu game); Ranked: chỉ số PES của CLB lúc vào trận.</summary>
    public int? HomePesTeam { get; set; }
    public int? AwayPesTeam { get; set; }
    public string? HomeTeamName { get; set; }
    public string? AwayTeamName { get; set; }
    public bool HomeReady { get; set; }
    public bool AwayReady { get; set; }
    /// <summary>Hạn chót phòng chọn đội (PO: không giữ phòng mãi, hết hạn tự Sẵn sàng với lựa chọn hiện tại).</summary>
    public DateTimeOffset? PickDeadline { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public int? HomeGoals { get; set; }
    public int? AwayGoals { get; set; }
    /// <summary>Người rời trận (Abandoned: sau hiệp 1 → Ranked xử thua 0-3; Voided: trước hết hiệp 1 → huỷ).</summary>
    public Guid? AbandonedBy { get; set; }
    public int? EloHomeDelta { get; set; }
    public int? EloAwayDelta { get; set; }
    /// <summary>Ranked: trận có tính tiền + điểm bảng không. PO 16/11 "không cần luật chống cày" → mặc định luôn true.</summary>
    public bool Counts { get; set; } = true;
    /// <summary>"Đá lại" từ trận này.</summary>
    public Guid? RematchOf { get; set; }
    public string? LastError { get; set; }

    public bool IsOpen => Status is FriendlyStatus.Invited or FriendlyStatus.Picking or FriendlyStatus.Queued or FriendlyStatus.Playing;
    public bool Involves(Guid managerId) => HomeManagerId == managerId || AwayManagerId == managerId;
}

/// <summary>Bàn thắng trận giao hữu (bộ đọc kết quả pesmatch như trận giải; không trộn với bảng Goals của giải).</summary>
public class FriendlyGoal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MatchId { get; set; }
    public bool Home { get; set; }
    public int Half { get; set; }
    public int Minute { get; set; }
    public int? Slot { get; set; }
    public string? ScorerName { get; set; }
}

/// <summary>Vé "Tìm trận" (hàng chờ). Ranked ghép Elo gần: chênh ≤ <see cref="FriendlyRules.EloWindow"/>, mỗi 30 s nới thêm.</summary>
public class MatchmakingTicket
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ManagerId { get; set; }
    public FriendlyMode Mode { get; set; }
    public int Elo { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public Guid? MatchId { get; set; }
    public bool Cancelled { get; set; }
}

/// <summary>Chat: MatchId null = sảnh chung, có = phòng trận.</summary>
public class FriendlyChatMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? MatchId { get; set; }
    public Guid ManagerId { get; set; }
    public required string Text { get; set; }
    public DateTimeOffset At { get; set; }
}

public static class FriendlyRules
{
    public const int StartElo = 1000, EloK = 32, EloWindow = 100, EloWiden = 50, ChatMax = 300;
    public static readonly TimeSpan WidenEvery = TimeSpan.FromSeconds(30), TicketTtl = TimeSpan.FromMinutes(10),
        InviteTtl = TimeSpan.FromMinutes(2), PickTime = TimeSpan.FromSeconds(90);
    /// <summary>Thưởng Ranked mặc định = 25% thưởng trận giải (PO 16/11), admin chỉnh trong <see cref="SeasonEconomy"/>.</summary>
    public const long DefaultWinPrize = FinanceRules.WinPrize / 4, DefaultDrawPrize = FinanceRules.DrawPrize / 4;

    /// <summary>Chênh Elo tối đa cho vé đã chờ <paramref name="waited"/>.</summary>
    public static int Window(TimeSpan waited) => EloWindow + EloWiden * (int)(waited / WidenEvery);

    /// <summary>Elo chuẩn, K = 32: (đổi nhà, đổi khách), tổng 0.</summary>
    public static (int Home, int Away) EloDelta(int home, int away, int homeGoals, int awayGoals)
    {
        var expected = 1 / (1 + Math.Pow(10, (away - home) / 400.0));
        var score = homeGoals > awayGoals ? 1 : homeGoals == awayGoals ? 0.5 : 0;
        var d = (int)Math.Round(EloK * (score - expected));
        return (d, -d);
    }

    /// <summary>Thắng 3 / hoà 1 / thua 0.</summary>
    public static int Points(int mine, int theirs) => mine > theirs ? 3 : mine == theirs ? 1 : 0;
}
