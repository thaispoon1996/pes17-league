using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Infrastructure;

namespace Pes17League.Clubs.Application;

/// <summary>
/// Áp HLV BOT v1 lên mọi CLB đang BOT trước khi sinh OF vòng
/// (<see cref="CompetitionService.CloseTransferWindowAsync"/>).
/// </summary>
public sealed class BotCoachService(ClubsDbContext db, TimeProvider clock)
{
    /// <param name="onlyClubs">Chỉ xếp cho các CLB này (mô phỏng 1 khung: CLB có trận); null = mọi CLB BOT của mùa.</param>
    public async Task ApplyAsync(Guid matchdayId, CancellationToken ct = default, IReadOnlyCollection<Guid>? onlyClubs = null)
    {
        var md = await db.Matchdays.AsNoTracking().SingleOrDefaultAsync(m => m.Id == matchdayId, ct)
            ?? throw new InvalidOperationException($"Matchday {matchdayId} không tồn tại.");
        var seasonId = md.SeasonId;

        var botClubs = await (
            from sc in db.SeasonClubs
            where sc.SeasonId == seasonId
            join a in db.TeamAssignments on sc.ClubId equals a.ClubId
            where a.To == null && a.ControllerType == ControllerType.Bot
            select sc.ClubId).Distinct().ToListAsync(ct);
        if (onlyClubs is not null) botClubs = [.. botClubs.Where(onlyClubs.Contains)];
        if (botClubs.Count == 0) return;

        var memberships = await db.SquadMemberships
            .Where(m => botClubs.Contains(m.ClubId) && m.LeftAt == null)
            .ToListAsync(ct);
        var playerIds = memberships.Select(m => m.PlayerId).Distinct().ToList();
        var players = await db.Players.Where(p => playerIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Position }).ToDictionaryAsync(p => p.Id, ct);
        var ovrs = await db.PlayerPrices.Where(p => p.SeasonId == seasonId && playerIds.Contains(p.PlayerId))
            .ToDictionaryAsync(p => p.PlayerId, p => p.Overall, ct);
        // FM đợt 5: chấn thương / mệt → hạ chỉ số hiệu dụng để BOT để dự bị (vẫn dùng nếu thiếu người).
        var conditions = await db.PlayerConditions.Where(c => playerIds.Contains(c.PlayerId)).ToDictionaryAsync(c => c.PlayerId, ct);

        var now = clock.GetUtcNow();
        var tacticsByClub = await db.Tactics.Where(t => botClubs.Contains(t.ClubId)).ToDictionaryAsync(t => t.ClubId, ct);

        // Gộp ghi cho cả khung (mô phỏng career: ~330 CLB/khung — mỗi CLB 3 lần SaveChanges trên ~12k dòng theo dõi mất ~50 s):
        // bước 1 tạm LeftAt mọi dòng (tránh trùng chỉ mục (ClubId, SlotOrder) lọc LeftAt null — giống LineupService), bước 2 ghi thứ tự mới.
        var plans = new List<(Guid ClubId, List<SquadMembership> Rows, BotPlan Plan)>();
        foreach (var clubId in botClubs)
        {
            var squadRows = memberships.Where(m => m.ClubId == clubId).ToList();
            if (squadRows.Count == 0) continue;
            var squad = squadRows.Select(m =>
            {
                var pos = players[m.PlayerId].Position;
                var ovr = ovrs.TryGetValue(m.PlayerId, out var o) ? o : 0;
                return new BotPlayer(m.PlayerId, pos, ConditionRules.EffectiveOverall(ovr, conditions.GetValueOrDefault(m.PlayerId)));
            }).ToList();
            plans.Add((clubId, squadRows, BotCoach.Plan(squad)));
        }
        if (plans.Count == 0) return;

        var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        try
        {
            foreach (var (_, rows, _) in plans)
                foreach (var m in rows) m.LeftAt = m.JoinedAt;
            await db.SaveChangesAsync(ct);

            var planned = plans.Select(p => p.ClubId).ToList();
            var clubs = await db.Clubs.Where(c => planned.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
            foreach (var (clubId, rows, plan) in plans)
            {
                WriteLineup(rows, plan);
                clubs[clubId].SquadVersion++;
                WriteTactics(clubId, plan, tacticsByClub, now);
            }
            await db.SaveChangesAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);
        }
        finally
        {
            if (tx is not null) await tx.DisposeAsync();
        }
    }

    /// <summary>Ghi SlotOrder mới (11 đá chính, dự bị, còn lại) và bỏ LeftAt tạm.</summary>
    static void WriteLineup(List<SquadMembership> rows, BotPlan plan)
    {
        var byPlayer = rows.ToDictionary(m => m.PlayerId);
        var order = plan.StarterOrder.Concat(plan.BenchOrder).Where(byPlayer.ContainsKey).Distinct().ToList();
        foreach (var m in rows)
            if (!order.Contains(m.PlayerId)) order.Add(m.PlayerId);
        for (var i = 0; i < order.Count; i++)
        {
            var m = byPlayer[order[i]];
            m.SlotOrder = i;
            m.LeftAt = null;
        }
    }

    void WriteTactics(Guid clubId, BotPlan plan, Dictionary<Guid, ClubTactics> existing, DateTimeOffset now)
    {
        if (!existing.TryGetValue(clubId, out var row))
        {
            row = new ClubTactics { ClubId = clubId };
            db.Tactics.Add(row);
            existing[clubId] = row;
        }
        var t = plan.Tactics;
        row.Formation = t.Formation;
        row.BackLine = t.BackLine;
        row.Pressure = t.Pressure;
        row.OffsideTrap = t.OffsideTrap;
        row.CounterAttack = t.CounterAttack;
        row.Positions = null;
        row.Strategies = t.Strategies is { } s ? [.. s.Select(v => v ?? -1)] : null;
        row.UpdatedAt = now;
    }
}
