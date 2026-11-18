using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Domain;
using Pes17League.Clubs.Infrastructure;

namespace Pes17League.Clubs.Application;

/// <summary>
/// Đội hình hiện tại trong DB → JSON cho tools/pes17db/pesdb.py apply (PES17 không có OF: ghi PlayerAssignment.bin
/// vào gói pes17league.cpk, PO duyệt 18/11/2026). Dạng: {"mã đội PES17": [[mã cầu thủ PES17, số áo], ...]} theo SlotOrder.
/// CLB đã lưu trữ (Inactive) không xuất: dòng gốc của nó giữ nguyên trong game (như OF PES6).
/// </summary>
public sealed class Pes17SquadExportService(ClubsDbContext db)
{
    public async Task<Dictionary<string, List<int[]>>> ExportAsync(Guid patchVersionId, CancellationToken ct = default)
    {
        var mappings = db.ClubPesMappings.Where(c => c.PatchVersionId == patchVersionId
            && db.Clubs.Any(x => x.Id == c.ClubId && x.Status == ClubStatus.Active));
        var rows = await (from cm in mappings
                          join m in db.SquadMemberships on cm.ClubId equals m.ClubId
                          where m.LeftAt == null
                          join pm in db.PlayerPesMappings on new { m.PlayerId, PatchVersionId = patchVersionId } equals new { pm.PlayerId, pm.PatchVersionId }
                          select new { cm.PesClubIndex, pm.PesPlayerId, m.ShirtNumber, m.SlotOrder }).ToListAsync(ct);
        var clubs = await mappings.Select(c => c.PesClubIndex).ToListAsync(ct);
        var byClub = rows.ToLookup(r => r.PesClubIndex);
        return clubs.Order().ToDictionary(c => c.ToString(),
            c => byClub[c].OrderBy(r => r.SlotOrder).Select(r => new[] { r.PesPlayerId, r.ShirtNumber }).ToList());
    }
}
