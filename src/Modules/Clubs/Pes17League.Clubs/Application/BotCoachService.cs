using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Infrastructure;

namespace Pes17League.Clubs.Application;

/// <summary>
/// Áp HLV BOT v1 lên mọi CLB đang BOT trước khi sinh OF vòng
/// (<see cref="CompetitionService.CloseTransferWindowAsync"/>).
/// </summary>
public sealed class BotCoachService(ClubsDbContext db, TimeProvider clock)
{
    public async Task ApplyAsync(Guid matchdayId, CancellationToken ct = default)
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

            var plan = BotCoach.Plan(squad);
            await WriteLineupAsync(clubId, squadRows, plan, ct);
            WriteTactics(clubId, plan, tacticsByClub, now);
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>Ghi SlotOrder mới: tạm LeftAt để tránh trùng chỉ mục (ClubId, SlotOrder) lọc LeftAt null — giống <see cref="LineupService"/>.</summary>
    async Task WriteLineupAsync(Guid clubId, List<SquadMembership> rows, BotPlan plan, CancellationToken ct)
    {
        var byPlayer = rows.ToDictionary(m => m.PlayerId);
        var order = plan.StarterOrder.Concat(plan.BenchOrder).Where(byPlayer.ContainsKey).Distinct().ToList();
        foreach (var m in rows)
            if (!order.Contains(m.PlayerId)) order.Add(m.PlayerId);

        foreach (var m in rows) m.LeftAt = m.JoinedAt;
        await db.SaveChangesAsync(ct);

        for (var i = 0; i < order.Count; i++)
        {
            var m = byPlayer[order[i]];
            m.SlotOrder = i;
            m.LeftAt = null;
        }
        (await db.Clubs.FindAsync([clubId], ct))!.SquadVersion++;
        await db.SaveChangesAsync(ct);
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
