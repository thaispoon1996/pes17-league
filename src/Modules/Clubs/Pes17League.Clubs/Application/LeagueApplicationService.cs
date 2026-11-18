using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Infrastructure;

namespace Pes17League.Clubs.Application;

/// <summary>1 CLB còn trống (BOT cầm) trong mùa đang mở — cho người chơi chọn khi đăng ký đá giải.</summary>
public sealed record OpenClub(Guid Id, string Name);

/// <summary>Đơn đăng ký đá giải (PO 16/11): người chơi chưa có CLB nộp đơn → admin duyệt (giao CLB qua
/// <see cref="ClubAdminService.SetHumanClubAsync"/>) hoặc từ chối.</summary>
public sealed class LeagueApplicationService(ClubsDbContext db, TimeProvider clock)
{
    /// <summary>CLB dự mùa đang mở (chưa kết thúc sớm nhất) mà chưa có người cầm.</summary>
    public async Task<List<OpenClub>> OpenClubsAsync(CancellationToken ct = default)
    {
        var season = await db.Seasons.Where(s => s.Status != SeasonStatus.Finished).OrderBy(s => s.Number).FirstOrDefaultAsync(ct);
        if (season is null) return [];
        var ids = await db.SeasonClubs.Where(x => x.SeasonId == season.Id).Select(x => x.ClubId)
            .Union(from ec in db.EditionClubs join e in db.Editions on ec.EditionId equals e.Id where e.SeasonId == season.Id select ec.ClubId)
            .ToListAsync(ct);
        var taken = await db.TeamAssignments.Where(a => a.To == null && a.ControllerType == ControllerType.Human).Select(a => a.ClubId).ToListAsync(ct);
        return await db.Clubs.Where(c => ids.Contains(c.Id) && !taken.Contains(c.Id) && c.Status == ClubStatus.Active)
            .OrderBy(c => c.Name).Select(c => new OpenClub(c.Id, c.Name)).ToListAsync(ct);
    }

    public Task<LeagueApplication?> PendingAsync(Guid managerId, CancellationToken ct = default) =>
        db.LeagueApplications.AsNoTracking().Where(a => a.ManagerId == managerId && a.Status == LeagueApplicationStatus.Pending).FirstOrDefaultAsync(ct);

    public Task<LeagueApplication?> LatestAsync(Guid managerId, CancellationToken ct = default) =>
        db.LeagueApplications.AsNoTracking().Where(a => a.ManagerId == managerId).OrderByDescending(a => a.CreatedAt).FirstOrDefaultAsync(ct);

    public async Task<LeagueApplication> ApplyAsync(Guid managerId, Guid? preferredClubId, string? note, CancellationToken ct = default)
    {
        if (await db.TeamAssignments.AnyAsync(a => a.ParticipantId == managerId && a.To == null && a.ControllerType == ControllerType.Human, ct))
            throw new AdminRuleException("Bạn đã có CLB");
        if (await PendingAsync(managerId, ct) is not null) throw new AdminRuleException("Bạn đã có đơn đang chờ duyệt");
        if (preferredClubId is { } c && (await OpenClubsAsync(ct)).All(x => x.Id != c)) throw new AdminRuleException("CLB này không còn trống");
        note = string.IsNullOrWhiteSpace(note) ? null : note.Trim()[..Math.Min(note.Trim().Length, 300)];
        var app = new LeagueApplication { ManagerId = managerId, PreferredClubId = preferredClubId, Note = note, Status = LeagueApplicationStatus.Pending, CreatedAt = clock.GetUtcNow() };
        db.LeagueApplications.Add(app);
        await db.SaveChangesAsync(ct);
        return app;
    }

    public async Task WithdrawAsync(Guid managerId, CancellationToken ct = default)
    {
        var app = await db.LeagueApplications.SingleOrDefaultAsync(a => a.ManagerId == managerId && a.Status == LeagueApplicationStatus.Pending, ct)
            ?? throw new AdminRuleException("Không có đơn đang chờ");
        app.Status = LeagueApplicationStatus.Withdrawn;
        app.DecidedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Đơn đang chờ (admin), cũ nhất trước.</summary>
    public Task<List<LeagueApplication>> PendingListAsync(CancellationToken ct = default) =>
        db.LeagueApplications.AsNoTracking().Where(a => a.Status == LeagueApplicationStatus.Pending).OrderBy(a => a.CreatedAt).ToListAsync(ct);

    /// <summary>Duyệt: giao CLB <paramref name="clubId"/> cho người chơi (CLB đó từ BOT sang người).</summary>
    public async Task ApproveAsync(Guid applicationId, Guid clubId, string actor, CancellationToken ct = default)
    {
        var app = await db.LeagueApplications.SingleOrDefaultAsync(a => a.Id == applicationId && a.Status == LeagueApplicationStatus.Pending, ct)
            ?? throw new AdminRuleException("Đơn không còn chờ duyệt");
        await new ClubAdminService(db, clock).SetHumanClubAsync(clubId, actor, app.ManagerId, ct);
        app.Status = LeagueApplicationStatus.Approved;
        app.ClubId = clubId;
        app.DecidedAt = clock.GetUtcNow();
        app.DecidedBy = actor;
        await db.SaveChangesAsync(ct);
    }

    public async Task RejectAsync(Guid applicationId, string? reason, string actor, CancellationToken ct = default)
    {
        var app = await db.LeagueApplications.SingleOrDefaultAsync(a => a.Id == applicationId && a.Status == LeagueApplicationStatus.Pending, ct)
            ?? throw new AdminRuleException("Đơn không còn chờ duyệt");
        app.Status = LeagueApplicationStatus.Rejected;
        app.Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim()[..Math.Min(reason.Trim().Length, 300)];
        app.DecidedAt = clock.GetUtcNow();
        app.DecidedBy = actor;
        await db.SaveChangesAsync(ct);
    }
}
