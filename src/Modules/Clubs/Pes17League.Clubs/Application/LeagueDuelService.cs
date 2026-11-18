using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Infrastructure;

namespace Pes17League.Clubs.Application;

/// <summary>Trận giải người vs người (PO 17/11): 2 HLV bấm Sẵn sàng → máy 2 người (máy giao hữu) nhận trận, đá "both"
/// (nhà = bàn phím/cảm ứng, khách = tay cầm). Lỗi/huỷ → bỏ Sẵn sàng 2 bên, bấm lại để đá lại.</summary>
public sealed class LeagueDuelService(ClubsDbContext db, TimeProvider clock)
{
    /// <summary>Bên của <paramref name="managerId"/> trong trận người vs người còn chờ đá; null = không phải trận của người này.</summary>
    public async Task<(Fixture Fixture, MatchSide Side)?> SeatAsync(Guid fixtureId, Guid managerId, CancellationToken ct = default)
    {
        var f = await db.Fixtures.SingleOrDefaultAsync(x => x.Id == fixtureId, ct);
        if (f is null) return null;
        var humans = await db.TeamAssignments.Where(a => (a.ClubId == f.HomeClubId || a.ClubId == f.AwayClubId) && a.To == null && a.ControllerType == ControllerType.Human)
            .ToListAsync(ct);
        if (humans.Count != 2) return null;
        var mine = humans.FirstOrDefault(a => a.ParticipantId == managerId);
        return mine is null ? null : (f, mine.ClubId == f.HomeClubId ? MatchSide.Home : MatchSide.Away);
    }

    public async Task ReadyAsync(Guid fixtureId, Guid managerId, bool ready, CancellationToken ct = default)
    {
        var seat = await SeatAsync(fixtureId, managerId, ct) ?? throw new AdminRuleException("Không phải trận người vs người của bạn");
        var (f, side) = seat;
        if (f.Status != FixtureStatus.Scheduled) throw new AdminRuleException("Trận không còn chờ đá");
        if (f.AssistantPlays) throw new AdminRuleException("Trận đã giao trợ lý đá thay");
        var md = await db.Matchdays.SingleAsync(m => m.Id == f.MatchdayId, ct);
        if (md.Status is not (MatchdayStatus.WindowOpen or MatchdayStatus.Locked or MatchdayStatus.Playing)) throw new AdminRuleException("Vòng này chưa mở");
        DateTimeOffset? at = ready ? clock.GetUtcNow() : null;
        if (side == MatchSide.Home) f.HomeReadyAt = at; else f.AwayReadyAt = at;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Trận 2 bên đã Sẵn sàng, chờ máy (sẵn sàng sớm nhất trước) kèm 2 HLV (máy chỉ nhận khi cả 2 đang mở trang trận).</summary>
    public async Task<IReadOnlyList<(Fixture Fixture, Guid Home, Guid Away)>> ReadyAsync(CancellationToken ct = default)
    {
        var rows = await (from f in db.Fixtures join m in db.Matchdays on f.MatchdayId equals m.Id
                          join h in db.TeamAssignments on f.HomeClubId equals h.ClubId
                          join a in db.TeamAssignments on f.AwayClubId equals a.ClubId
                          where f.Status == FixtureStatus.Scheduled && !f.AssistantPlays && f.HomeReadyAt != null && f.AwayReadyAt != null
                                && (m.Status == MatchdayStatus.WindowOpen || m.Status == MatchdayStatus.Locked || m.Status == MatchdayStatus.Playing)
                                && h.To == null && h.ControllerType == ControllerType.Human && h.ParticipantId != null
                                && a.To == null && a.ControllerType == ControllerType.Human && a.ParticipantId != null
                          select new { f, Home = h.ParticipantId!.Value, Away = a.ParticipantId!.Value }).ToListAsync(ct);
        return [.. rows.OrderBy(r => r.f.HomeReadyAt > r.f.AwayReadyAt ? r.f.HomeReadyAt : r.f.AwayReadyAt).Select(r => (r.f, r.Home, r.Away))];
    }

    /// <summary>Đá lỗi / máy khởi động lại → bỏ Sẵn sàng 2 bên (không tự vào lại khi người chơi đã rời).</summary>
    public Task UnreadyAsync(Guid fixtureId, CancellationToken ct = default) =>
        db.Fixtures.Where(f => f.Id == fixtureId && f.Status == FixtureStatus.Scheduled)
            .ExecuteUpdateAsync(x => x.SetProperty(f => f.HomeReadyAt, (DateTimeOffset?)null).SetProperty(f => f.AwayReadyAt, (DateTimeOffset?)null), ct);
}
