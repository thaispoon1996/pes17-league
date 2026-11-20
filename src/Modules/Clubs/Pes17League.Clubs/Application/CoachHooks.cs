using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Infrastructure;

namespace Pes17League.Clubs.Application;

/// <summary>
/// Tra HLV career đang dẫn CLB (người cầm CLB có hồ sơ HLV, đang có việc) để áp năng lực vào tập luyện, thể lực, họp báo, trinh sát, mua cầu thủ.
/// Giải online không có hồ sơ HLV → rỗng, mọi luật cũ giữ nguyên.
/// </summary>
public static class CoachHooks
{
    public static async Task<Dictionary<Guid, Coach>> ByClubAsync(ClubsDbContext db, CancellationToken ct = default)
    {
        if (!await db.Coaches.AnyAsync(c => c.Status == CoachStatus.Employed, ct)) return [];
        var rows = await (from a in db.TeamAssignments
                          join c in db.Coaches on a.ParticipantId equals (Guid?)c.ManagerId
                          where a.To == null && a.ControllerType == ControllerType.Human && c.Status == CoachStatus.Employed
                          select new { a.ClubId, Coach = c }).AsNoTracking().ToListAsync(ct);
        return rows.GroupBy(r => r.ClubId).ToDictionary(g => g.Key, g => g.First().Coach);
    }

    public static async Task<Coach?> ForClubAsync(ClubsDbContext db, Guid clubId, CancellationToken ct = default) =>
        (await ByClubAsync(db, ct)).GetValueOrDefault(clubId);

    /// <summary>% giảm phí mua thẳng theo Đàm phán của HLV CLB mua (0 = không có HLV career).</summary>
    public static async Task<int> BuyDiscountPctAsync(ClubsDbContext db, Guid clubId, CancellationToken ct = default) =>
        await ForClubAsync(db, clubId, ct) is { } c ? CoachRules.NegotiationDiscountPct(CoachRules.Effective(c, CoachSkill.Negotiation)) : 0;
}
