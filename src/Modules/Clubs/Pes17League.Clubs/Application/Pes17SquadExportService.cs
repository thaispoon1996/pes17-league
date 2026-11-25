using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Domain;
using Pes17League.Clubs.Infrastructure;
using Pes17League.OptionFile;

namespace Pes17League.Clubs.Application;

/// <summary>Đội hình xuất cho game: cầu thủ theo ô + sơ đồ web của CLB đã chọn sơ đồ/vị trí (mã đội PES17 dạng chuỗi).</summary>
public sealed record Pes17Squads(Dictionary<string, List<int[]>> Squads, Dictionary<string, int[][]> Formations,
    Dictionary<string, Dictionary<string, int>>? Jobs = null, Dictionary<string, Dictionary<string, int>>? Plans = null, int[]? Humans = null)
{
    /// <summary>
    /// {"mã đội": [[cầu thủ, số áo], ...], "_formations": {"mã đội": [[vai trò, X, Y] × 11]}, "_jobs": {"mã đội": {"captain": cầu thủ, …}},
    ///  "_plan": {"mã đội": {"attackStyle": 0/1, …, "defensiveLine": 1..10, …}}}
    /// — bỏ khoá phụ khi trống.
    /// </summary>
    public string ToJson()
    {
        var doc = Squads.ToDictionary(kv => kv.Key, kv => (object)kv.Value);
        if (Formations.Count > 0) doc["_formations"] = Formations;
        if (Jobs is { Count: > 0 }) doc["_jobs"] = Jobs;
        if (Plans is { Count: > 0 }) doc["_plan"] = Plans;
        if (Humans is { Length: > 0 }) doc["_human"] = Humans;   // CLB người chơi: không cộng chỉ số khi tăng độ khó (pesdb.py apply, PO 26/11)
        return JsonSerializer.Serialize(doc);
    }
}

/// <summary>
/// Đội hình hiện tại trong DB → JSON cho tools/pes17db/pesdb.py apply (PES17 không có OF: ghi PlayerAssignment.bin
/// vào gói pes17league.cpk, PO duyệt 18/11/2026). Cầu thủ theo SlotOrder; PO 21/11: kèm sơ đồ/vị trí/vai trò web
/// (TacticsFormation.bin) cho CLB đã chọn sơ đồ — CLB chưa chọn giữ sơ đồ của game.
/// Thông số chiến thuật PES17 (<see cref="Pes17Plan"/>) cho CLB đã chọn — mục chưa chọn giữ của game.
/// Đội trưởng + người đá phạt cho MỌI CLB (quy tắc PO duyệt 05/11, áp cho PES17 21/11): người CLB chọn nếu đang đá chính,
/// không thì tự chọn (<see cref="TeamJobPicker"/>) theo chỉ số mùa đang đá.
/// CLB đã lưu trữ (Inactive) không xuất: dòng gốc của nó giữ nguyên trong game (như OF PES6).
/// </summary>
public sealed class Pes17SquadExportService(ClubsDbContext db, TimeProvider clock)
{
    private static readonly (TeamJob Job, string Key)[] JobKeys =
    [
        (TeamJob.Captain, "captain"), (TeamJob.Penalty, "penalty"), (TeamJob.LongFreeKick, "longFk"),
        (TeamJob.ShortFreeKick, "shortFk"), (TeamJob.LeftCorner, "leftCk"), (TeamJob.RightCorner, "rightCk"),
    ];

    public async Task<Pes17Squads> ExportAsync(Guid patchVersionId, CancellationToken ct = default)
    {
        var mappings = db.ClubPesMappings.Where(c => c.PatchVersionId == patchVersionId
            && db.Clubs.Any(x => x.Id == c.ClubId && x.Status == ClubStatus.Active));
        var rows = await (from cm in mappings
                          join m in db.SquadMemberships on cm.ClubId equals m.ClubId
                          where m.LeftAt == null
                          join pm in db.PlayerPesMappings on new { m.PlayerId, PatchVersionId = patchVersionId } equals new { pm.PlayerId, pm.PatchVersionId }
                          select new { cm.PesClubIndex, cm.ClubId, m.PlayerId, pm.PesPlayerId, m.ShirtNumber, m.SlotOrder }).ToListAsync(ct);
        var clubs = await mappings.Select(c => new { c.PesClubIndex, c.ClubId }).ToListAsync(ct);
        var byClub = rows.ToLookup(r => r.PesClubIndex);
        var squads = clubs.Select(c => c.PesClubIndex).Order().ToDictionary(c => c.ToString(),
            c => byClub[c].OrderBy(r => r.SlotOrder).Select(r => new[] { r.PesPlayerId, r.ShirtNumber }).ToList());

        var ids = clubs.Select(c => c.ClubId).ToList();
        var tactics = await db.Tactics.Where(t => ids.Contains(t.ClubId)).ToDictionaryAsync(t => t.ClubId, ct);
        var formations = new Dictionary<string, int[][]>();
        var plans = new Dictionary<string, Dictionary<string, int>>();
        foreach (var c in clubs.OrderBy(c => c.PesClubIndex))
        {
            if (byClub[c.PesClubIndex].Count() >= 11 && Pes17Formations.WebLayout(tactics.GetValueOrDefault(c.ClubId)) is { } l)
                formations[c.PesClubIndex.ToString()] = Pes17Formations.ToGame(l);
            if (Pes17Plan.ToGame(tactics.GetValueOrDefault(c.ClubId)) is { } pl) plans[c.PesClubIndex.ToString()] = pl;
        }

        // mùa đang đá của patch (chỉ số hiện tại cho tự chọn); chưa có mùa → giữ vai như dữ liệu game
        var seasonId = await db.Seasons.Where(s => s.PatchVersionId == patchVersionId && s.Status == SeasonStatus.Active)
            .OrderByDescending(s => s.Number).Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct);
        var jobs = new Dictionary<string, Dictionary<string, int>>();
        if (seasonId is { } sid)
        {
            var starters = await new TacticsService(db, clock).StartersAsync(sid, ids, ct);
            var pesId = rows.ToDictionary(r => (r.ClubId, r.PlayerId), r => r.PesPlayerId);
            foreach (var c in clubs.OrderBy(c => c.PesClubIndex))
            {
                if (!starters.TryGetValue(c.ClubId, out var xi) || xi.Count == 0) continue;
                var t = tactics.GetValueOrDefault(c.ClubId);
                var js = JobKeys.Select(k => (k.Key, P: TeamJobPicker.Resolve(k.Job, TacticsService.GetJob(t, k.Job), xi)))
                    .Where(x => x.P is not null && pesId.ContainsKey((c.ClubId, x.P.PlayerId)))
                    .ToDictionary(x => x.Key, x => pesId[(c.ClubId, x.P!.PlayerId)]);
                if (js.Count > 0) jobs[c.PesClubIndex.ToString()] = js;
            }
        }
        var humanClubs = await db.TeamAssignments.Where(a => a.To == null && a.ControllerType == ControllerType.Human).Select(a => a.ClubId).ToListAsync(ct);
        var humans = clubs.Where(c => humanClubs.Contains(c.ClubId)).Select(c => c.PesClubIndex).Order().ToArray();
        return new Pes17Squads(squads, formations, jobs, plans, humans);
    }
}
