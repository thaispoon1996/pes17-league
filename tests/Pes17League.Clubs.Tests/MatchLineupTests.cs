using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Application;
using Pes17League.Clubs.Domain;

namespace Pes17League.Clubs.Tests;

public class MatchLineupTests
{
    /// <summary>PO 23/11: sửa đội hình ở trang trận chỉ lưu bản nháp của trận; đội hình chính không đổi; ảnh chụp cho game lấy bản nháp.</summary>
    [DbFact]
    public async Task Match_draft_keeps_main_lineup_and_feeds_snapshot()
    {
        await using var t = await TestDb.CreateAsync();
        await using var db = t.Context();
        var svc = new CompetitionService(db, t.Store, t.Clock);
        var season = await svc.CreateSeasonAsync("Mùa 1", 1);
        var import = await new ImportPatchService(db, t.Store, t.Clock).ImportAsync("PES17 test", Env.OrigOptionFile, season.Id,
            [new LeagueClub(0, "Arsenal"), new LeagueClub(1)]);
        Assert.True((await svc.StartSeasonAsync(season.Id, import.PatchVersionId)).Ok);
        var club = await db.ClubPesMappings.Where(m => m.PatchVersionId == import.PatchVersionId && m.PesClubIndex == 0).Select(m => m.ClubId).SingleAsync();
        async Task<List<Guid>> Main()
        {
            await using var q = t.Context();
            return await q.SquadMemberships.Where(m => m.ClubId == club && m.LeftAt == null).OrderBy(m => m.SlotOrder).Select(m => m.PlayerId).ToListAsync();
        }
        var before = await Main();
        var fixture = Guid.NewGuid();
        var ml = new MatchLineupService(t.Store);

        await using (var e = t.Context())
            Assert.Null(await ml.EditAsync(e, fixture, club, d => new LineupService(d).SwapAsync(club, before[1], before[11]), save: true));
        await using (var e = t.Context())
            Assert.Null(await ml.EditAsync(e, fixture, club, d => new TacticsService(d, t.Clock).SetPes17PlanAsync(club, 5, 8), save: true));
        Assert.Equal(before, await Main());                                   // đội hình chính giữ nguyên
        Assert.Null((await new TacticsService(t.Context(), t.Clock).GetAsync(club))?.Pes17Plan);

        var draft = await ml.GetAsync(fixture, club);
        Assert.NotNull(draft);
        Assert.Equal((11, 1), (draft!.Slots[before[1]], draft.Slots[before[11]]));   // lần sửa sau chồng lên lần trước
        Assert.Equal(8, draft.Tactics!.Pes17Plan![5]);

        await using var s = t.Context();
        var build = await new Pes17RoundService(s, t.Store, t.Clock).SnapshotAsync(import.PatchVersionId, null, default, (fixture, [club]));
        using var doc = JsonDocument.Parse(await t.Store.ReadAsync(build.StoragePath!));
        var pes = await db.PlayerPesMappings.Where(m => m.PatchVersionId == import.PatchVersionId).ToDictionaryAsync(m => m.PlayerId, m => m.PesPlayerId);
        Assert.Equal(pes[before[11]], doc.RootElement.GetProperty("0")[1][0].GetInt32());
        Assert.Equal(8, doc.RootElement.GetProperty("_plan").GetProperty("0").GetProperty("defensiveLine").GetInt32());
        Assert.Equal(before, await Main());

        await ml.ResetAsync(fixture, club);
        Assert.Null(await ml.GetAsync(fixture, club));
    }
}
