using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Domain;
using Pes17League.Clubs.Infrastructure;

namespace Pes17League.Clubs.Application;

/// <summary>Đội hình xuất cho game: cầu thủ theo ô + sơ đồ web của CLB đã chọn sơ đồ/vị trí (mã đội PES17 dạng chuỗi).</summary>
public sealed record Pes17Squads(Dictionary<string, List<int[]>> Squads, Dictionary<string, int[][]> Formations)
{
    /// <summary>{"mã đội": [[cầu thủ, số áo], ...], "_formations": {"mã đội": [[vai trò, X, Y] × 11]}} — bỏ "_formations" khi trống.</summary>
    public string ToJson()
    {
        var doc = Squads.ToDictionary(kv => kv.Key, kv => (object)kv.Value);
        if (Formations.Count > 0) doc["_formations"] = Formations;
        return JsonSerializer.Serialize(doc);
    }
}

/// <summary>
/// Đội hình hiện tại trong DB → JSON cho tools/pes17db/pesdb.py apply (PES17 không có OF: ghi PlayerAssignment.bin
/// vào gói pes17league.cpk, PO duyệt 18/11/2026). Cầu thủ theo SlotOrder; PO 21/11: kèm sơ đồ/vị trí/vai trò web
/// (TacticsFormation.bin) cho CLB đã chọn sơ đồ — CLB chưa chọn giữ sơ đồ của game.
/// CLB đã lưu trữ (Inactive) không xuất: dòng gốc của nó giữ nguyên trong game (như OF PES6).
/// </summary>
public sealed class Pes17SquadExportService(ClubsDbContext db)
{
    public async Task<Pes17Squads> ExportAsync(Guid patchVersionId, CancellationToken ct = default)
    {
        var mappings = db.ClubPesMappings.Where(c => c.PatchVersionId == patchVersionId
            && db.Clubs.Any(x => x.Id == c.ClubId && x.Status == ClubStatus.Active));
        var rows = await (from cm in mappings
                          join m in db.SquadMemberships on cm.ClubId equals m.ClubId
                          where m.LeftAt == null
                          join pm in db.PlayerPesMappings on new { m.PlayerId, PatchVersionId = patchVersionId } equals new { pm.PlayerId, pm.PatchVersionId }
                          select new { cm.PesClubIndex, pm.PesPlayerId, m.ShirtNumber, m.SlotOrder }).ToListAsync(ct);
        var clubs = await mappings.Select(c => new { c.PesClubIndex, c.ClubId }).ToListAsync(ct);
        var byClub = rows.ToLookup(r => r.PesClubIndex);
        var squads = clubs.Select(c => c.PesClubIndex).Order().ToDictionary(c => c.ToString(),
            c => byClub[c].OrderBy(r => r.SlotOrder).Select(r => new[] { r.PesPlayerId, r.ShirtNumber }).ToList());

        var ids = clubs.Select(c => c.ClubId).ToList();
        var tactics = await db.Tactics.Where(t => ids.Contains(t.ClubId)).ToDictionaryAsync(t => t.ClubId, ct);
        var formations = new Dictionary<string, int[][]>();
        foreach (var c in clubs.OrderBy(c => c.PesClubIndex))
            if (byClub[c.PesClubIndex].Count() >= 11 && Pes17Formations.WebLayout(tactics.GetValueOrDefault(c.ClubId)) is { } l)
                formations[c.PesClubIndex.ToString()] = Pes17Formations.ToGame(l);
        return new Pes17Squads(squads, formations);
    }
}
