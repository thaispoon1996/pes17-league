using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Infrastructure;
using Pes17League.OptionFile;

namespace Pes17League.Clubs.Application;

public enum TacticsError { UnknownFormation, SettingOutOfRange, PositionOutOfRange, StrategyOutOfRange, NotStarter }

/// <summary>
/// Sơ đồ/thiết lập đội trên web (PO duyệt 03/11): chọn 1 trong 21 sơ đồ mẫu + 4 thiết lập A/B/C. Null = giữ như patch.
/// Giai đoạn 2 (PO duyệt 05/11): vị trí tự do ô 1–10, 4 chiến thuật phím L2, đội trưởng + người đá phạt.
/// Có hiệu lực trong game như đội hình: OF khoá vòng cho mọi CLB, trận của PO lấy bản lúc vào trận.
/// </summary>
public sealed class TacticsService(ClubsDbContext db, TimeProvider clock)
{
    public Task<ClubTactics?> GetAsync(Guid clubId, CancellationToken ct = default) =>
        db.Tactics.AsNoTracking().SingleOrDefaultAsync(t => t.ClubId == clubId, ct);

    /// <summary>Đổi sơ đồ thì xoá vị trí tự do (vị trí cũ thuộc sơ đồ cũ).</summary>
    public async Task<TacticsError?> SetAsync(Guid clubId, TeamTactics t, CancellationToken ct = default)
    {
        if (t.Formation is { } f && !Formations.Names.Contains(f)) return TacticsError.UnknownFormation;
        if (new[] { t.BackLine, t.Pressure, t.OffsideTrap, t.CounterAttack }.Any(v => v is < 0 or > 2)) return TacticsError.SettingOutOfRange;
        var row = await RowAsync(clubId, ct);
        if (row.Formation != t.Formation) (row.Positions, row.Roles) = (null, null);
        (row.Formation, row.BackLine, row.Pressure, row.OffsideTrap, row.CounterAttack) = (t.Formation, t.BackLine, t.Pressure, t.OffsideTrap, t.CounterAttack);
        return await SaveAsync(row, ct);
    }

    /// <summary>Vị trí tự do: 10 cặp (X, Y) toạ độ OF của ô 1–10; null = về theo sơ đồ (xoá cả vai trò).
    /// <paramref name="roles"/> (career): 10 mã vai trò ô 1–10; null = giữ vai trò đã lưu (về sơ đồ thì xoá).</summary>
    public async Task<TacticsError?> SetPositionsAsync(Guid clubId, IReadOnlyList<(int X, int Y)>? positions, CancellationToken ct = default,
        IReadOnlyList<int>? roles = null)
    {
        if (positions is not null && (positions.Count != 10 || positions.Any(p =>
                p.X is < FormationBounds.MinX or > FormationBounds.MaxX || p.Y is < FormationBounds.MinY or > FormationBounds.MaxY)))
            return TacticsError.PositionOutOfRange;
        if (roles is not null && (positions is null || roles.Count != 10 || roles.Any(r => r is < 1 or > 40))) return TacticsError.PositionOutOfRange;
        var row = await RowAsync(clubId, ct);
        row.Positions = positions is null ? null : [.. positions.SelectMany(p => new[] { p.X, p.Y })];
        if (positions is null) row.Roles = null;
        else if (roles is not null) row.Roles = [.. roles];
        return await SaveAsync(row, ct);
    }

    /// <summary>Chiến thuật phím L2 thứ <paramref name="button"/> (0..3 = ○ ✕ Select □); null = theo patch.</summary>
    public async Task<TacticsError?> SetStrategyAsync(Guid clubId, int button, int? value, CancellationToken ct = default)
    {
        if (button is < 0 or > 3 || value is < 0 || value >= TeamStrategies.Names.Count) return TacticsError.StrategyOutOfRange;
        var row = await RowAsync(clubId, ct);
        var cur = row.Strategies ?? [-1, -1, -1, -1];      // -1 = theo patch (cột int[] không chứa null)
        cur[button] = value ?? -1;
        row.Strategies = cur.All(v => v < 0) ? null : [.. cur];
        return await SaveAsync(row, ct);
    }

    /// <summary>Gán vai cho cầu thủ đang đá chính (ô 0–10) của CLB; null = tự chọn.</summary>
    public async Task<TacticsError?> SetJobAsync(Guid clubId, TeamJob job, Guid? playerId, CancellationToken ct = default)
    {
        if (playerId is { } p && !await db.SquadMemberships.AnyAsync(m => m.ClubId == clubId && m.PlayerId == p && m.LeftAt == null && m.SlotOrder <= 10, ct))
            return TacticsError.NotStarter;
        var row = await RowAsync(clubId, ct);
        SetJob(row, job, playerId);
        return await SaveAsync(row, ct);
    }

    public static Guid? GetJob(ClubTactics? t, TeamJob job) => t is null ? null : job switch
    {
        TeamJob.LongFreeKick => t.LongFreeKickTakerId, TeamJob.ShortFreeKick => t.ShortFreeKickTakerId,
        TeamJob.LeftCorner => t.LeftCornerTakerId, TeamJob.RightCorner => t.RightCornerTakerId,
        TeamJob.Penalty => t.PenaltyTakerId, _ => t.CaptainId,
    };

    private static void SetJob(ClubTactics t, TeamJob job, Guid? v)
    {
        switch (job)
        {
            case TeamJob.LongFreeKick: t.LongFreeKickTakerId = v; break;
            case TeamJob.ShortFreeKick: t.ShortFreeKickTakerId = v; break;
            case TeamJob.LeftCorner: t.LeftCornerTakerId = v; break;
            case TeamJob.RightCorner: t.RightCornerTakerId = v; break;
            case TeamJob.Penalty: t.PenaltyTakerId = v; break;
            default: t.CaptainId = v; break;
        }
    }

    /// <summary>11 người đá chính (ô 0–10) của từng CLB kèm OVR + chỉ số hiện tại của mùa — đầu vào <see cref="TeamJobPicker"/>.</summary>
    public async Task<Dictionary<Guid, List<StarterInfo>>> StartersAsync(Guid seasonId, IReadOnlyCollection<Guid> clubIds, CancellationToken ct = default)
    {
        var rows = await (from m in db.SquadMemberships
                          where clubIds.Contains(m.ClubId) && m.LeftAt == null && m.SlotOrder <= 10
                          join p in db.Players on m.PlayerId equals p.Id
                          join pp in db.PlayerPrices.Where(x => x.SeasonId == seasonId) on p.Id equals pp.PlayerId into g
                          from pp in g.DefaultIfEmpty()
                          select new { m.ClubId, m.PlayerId, m.SlotOrder, p.Position, Overall = pp == null ? 0 : pp.Overall }).ToListAsync(ct);
        var attrs = await new EconomyService(db, clock).CurrentAttributesAsync(seasonId, [.. rows.Select(r => r.PlayerId)], ct);
        return rows.GroupBy(r => r.ClubId).ToDictionary(g => g.Key, g => g.OrderBy(r => r.SlotOrder)
            .Select(r => new StarterInfo(r.PlayerId, r.SlotOrder, r.Position, r.Overall,
                attrs.TryGetValue(r.PlayerId, out var a) ? a : new Dictionary<string, int>())).ToList());
    }

    /// <summary>Mã vai trò ô 1–10 đã chọn (career); null = theo sơ đồ.</summary>
    public static IReadOnlyList<int>? RolesOf(ClubTactics? t) => t?.Roles is { Length: 10 } r && t.Positions is { Length: 20 } ? r : null;

    /// <summary>Vị trí tự do dạng cặp (null nếu chưa chỉnh).</summary>
    public static IReadOnlyList<(int X, int Y)>? PositionsOf(ClubTactics? t) =>
        t?.Positions is { Length: 20 } p ? [.. Enumerable.Range(0, 10).Select(i => (p[2 * i], p[2 * i + 1]))] : null;

    /// <summary>Chiến thuật L2 dạng null = theo patch.</summary>
    public static IReadOnlyList<int?>? StrategiesOf(ClubTactics? t) =>
        t?.Strategies is { Length: 4 } s ? [.. s.Select(v => v < 0 ? (int?)null : v)] : null;

    private async Task<ClubTactics> RowAsync(Guid clubId, CancellationToken ct)
    {
        var row = await db.Tactics.SingleOrDefaultAsync(x => x.ClubId == clubId, ct);
        if (row is null) db.Tactics.Add(row = new ClubTactics { ClubId = clubId });
        return row;
    }

    private async Task<TacticsError?> SaveAsync(ClubTactics row, CancellationToken ct)
    {
        row.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return null;
    }
}
