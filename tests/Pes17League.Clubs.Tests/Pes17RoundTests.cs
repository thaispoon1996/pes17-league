using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Application;

namespace Pes17League.Clubs.Tests;

public class Pes17RoundTests
{
    [Fact]
    public void Patch_name_detects_pes17()
    {
        Assert.True(Pes17RoundService.IsPes17("PES17 HANO V6"));
        Assert.False(Pes17RoundService.IsPes17("PES6VN 25-26"));
        Assert.False(Pes17RoundService.IsPes17(null));
    }

    /// <summary>Patch PES17: vòng không tạo kỳ CN riêng; "đóng kỳ CN" = chụp đội hình JSON (không dựng OF PES6) → Locked.</summary>
    [DbFact]
    public async Task Pes17_round_has_no_window_and_snapshots_squads()
    {
        await using var t = await TestDb.CreateAsync();
        await using var db = t.Context();
        var svc = new CompetitionService(db, t.Store, t.Clock);
        var season = await svc.CreateSeasonAsync("Mùa 1", 1);
        var import = await new ImportPatchService(db, t.Store, t.Clock).ImportAsync("PES17 test", Env.OrigOptionFile, season.Id,
            [new LeagueClub(0, "Arsenal"), new LeagueClub(1)]);
        Assert.True((await svc.StartSeasonAsync(season.Id, import.PatchVersionId)).Ok);

        var md1 = await db.Matchdays.SingleAsync(m => m.SeasonId == season.Id && m.Number == 1);
        Assert.Equal(MatchdayStatus.WindowOpen, md1.Status);
        Assert.Null(md1.TransferWindowId);
        Assert.False(await db.TransferWindows.AnyAsync(w => w.SeasonId == season.Id));

        var close = await svc.CloseTransferWindowAsync(md1.Id);
        Assert.True(close.Ok, close.Error.ToString());
        Assert.Equal((MatchdayStatus.Locked, close.OptionFileBuildId), (md1.Status, md1.OptionFileBuildId));
        var build = await db.OptionFileBuilds.SingleAsync(b => b.Id == close.OptionFileBuildId);
        Assert.EndsWith(".pes17.json", build.StoragePath);
        var squads = JsonSerializer.Deserialize<Dictionary<string, List<int[]>>>(await t.Store.ReadAsync(build.StoragePath!))!;
        Assert.Equal(["0", "1"], squads.Keys.Order());
        Assert.All(squads.Values, s => Assert.NotEmpty(s));
    }
}
