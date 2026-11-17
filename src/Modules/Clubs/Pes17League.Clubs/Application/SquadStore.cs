using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Infrastructure;

namespace Pes17League.Clubs.Application;

/// <summary>Squad hiện tại theo SlotOrder, kèm dòng membership để cập nhật.</summary>
internal sealed record Squad(IReadOnlyList<SquadEntry<Guid>> Entries, Dictionary<Guid, SquadMembership> Rows);

/// <summary>Đọc/ghi squad từ SquadMembership — dùng chung cho chuyển nhượng và xử lý hết hợp đồng.</summary>
internal static class SquadStore
{
    public static async Task<Squad> LoadAsync(ClubsDbContext db, Guid clubId, CancellationToken ct)
    {
        var rows = await (from m in db.SquadMemberships
                          join p in db.Players on m.PlayerId equals p.Id
                          where m.ClubId == clubId && m.LeftAt == null
                          orderby m.SlotOrder
                          select new { m, p.Position }).ToListAsync(ct);
        return new(rows.Select(r => new SquadEntry<Guid>(r.m.PlayerId, r.m.ShirtNumber, r.Position)).ToList(),
            rows.ToDictionary(r => r.m.PlayerId, r => r.m));
    }

    /// <summary>Ghi kết quả luật vào membership: người rời → LeftAt; còn lại cập nhật slot/số áo; người mới → thêm dòng. Trả squad mới (chưa SaveChanges).</summary>
    public static Squad Apply(ClubsDbContext db, Club club, Squad before, SquadChange<Guid> after, DateTimeOffset now)
    {
        var keep = after.Squad.Select(e => e.PlayerId).ToHashSet();
        foreach (var (pid, row) in before.Rows)
            if (!keep.Contains(pid)) row.LeftAt = now;
        var rows = new Dictionary<Guid, SquadMembership>();
        for (var i = 0; i < after.Squad.Count; i++)
        {
            var e = after.Squad[i];
            if (before.Rows.TryGetValue(e.PlayerId, out var row))
            {
                row.SlotOrder = i;
                row.ShirtNumber = e.ShirtNumber;
            }
            else db.SquadMemberships.Add(row = new SquadMembership { ClubId = club.Id, PlayerId = e.PlayerId, ShirtNumber = e.ShirtNumber, SlotOrder = i, JoinedAt = now });
            rows[e.PlayerId] = row;
        }
        club.SquadVersion++;
        return new(after.Squad, rows);
    }

    /// <summary>CLB do người chơi điều khiển (TeamAssignment hiện tại = Human).</summary>
    public static Task<bool> IsHumanAsync(ClubsDbContext db, Guid clubId, CancellationToken ct) =>
        db.TeamAssignments.AnyAsync(a => a.ClubId == clubId && a.To == null && a.ControllerType == ControllerType.Human, ct);

    public static Task<long> PayrollAsync(ClubsDbContext db, Guid clubId, CancellationToken ct) =>
        db.Contracts.Where(c => c.ClubId == clubId && c.Status == ContractStatus.Active).SumAsync(c => c.Wage ?? 0, ct);

    public static Task<long> BalanceAsync(ClubsDbContext db, Guid clubId, CancellationToken ct) =>
        db.Ledger.Where(e => e.ClubId == clubId).SumAsync(e => e.Amount, ct);
}
