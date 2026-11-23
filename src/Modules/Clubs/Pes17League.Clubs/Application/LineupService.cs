using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Infrastructure;

namespace Pes17League.Clubs.Application;

public enum LineupError { PlayerNotInSquad, SamePlayer, GoalkeeperSlot }

/// <summary>
/// Đội hình trên web (C11): đổi chỗ 2 cầu thủ trong squad — đá chính ↔ dự bị hoặc đổi ô trong đội hình (SlotOrder 0–10 = đá chính,
/// thứ tự ô theo sơ đồ của đội trong OF). Ô 0 = thủ môn, chỉ GK được đứng. Có hiệu lực trong game từ OF sinh lần tới (đóng kỳ CN).
/// </summary>
public sealed class LineupService(ClubsDbContext db)
{
    public async Task<LineupError?> SwapAsync(Guid clubId, Guid playerA, Guid playerB, CancellationToken ct = default)
    {
        if (playerA == playerB) return LineupError.SamePlayer;
        var rows = await (from m in db.SquadMemberships
                          join p in db.Players on m.PlayerId equals p.Id
                          where m.ClubId == clubId && m.LeftAt == null && (m.PlayerId == playerA || m.PlayerId == playerB)
                          select new { m, p.Position }).ToListAsync(ct);
        if (rows.Count != 2) return LineupError.PlayerNotInSquad;
        var (a, b) = rows[0].m.PlayerId == playerA ? (rows[0], rows[1]) : (rows[1], rows[0]);
        if ((a.m.SlotOrder == 0 && b.Position != Position.GK) || (b.m.SlotOrder == 0 && a.Position != Position.GK))
            return LineupError.GoalkeeperSlot;

        // Chỉ mục duy nhất (ClubId, SlotOrder) lọc theo LeftAt null → tạm đánh dấu A rời squad để đổi ô không trùng.
        // Đã ở trong giao dịch (đội hình riêng của trận, MatchLineupService) → dùng giao dịch đó.
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        var (slotA, slotB, joined) = (a.m.SlotOrder, b.m.SlotOrder, a.m.JoinedAt);
        a.m.LeftAt = joined;
        await db.SaveChangesAsync(ct);
        b.m.SlotOrder = slotA;
        await db.SaveChangesAsync(ct);
        a.m.SlotOrder = slotB;
        a.m.LeftAt = null;
        (await db.Clubs.FindAsync([clubId], ct))!.SquadVersion++;
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return null;
    }
}
