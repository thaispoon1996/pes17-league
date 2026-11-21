using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Application;
using Pes17League.Clubs.Domain;

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
        using var doc = JsonDocument.Parse(await t.Store.ReadAsync(build.StoragePath!));
        var keys = doc.RootElement.EnumerateObject().Select(p => p.Name).ToList();
        Assert.Equal(["0", "1"], keys.Where(k => k != "_formations").Order());
        Assert.All(keys.Where(k => k != "_formations"), k => Assert.NotEqual(0, doc.RootElement.GetProperty(k).GetArrayLength()));
        // PO 21/11: HLV BOT đã chọn sơ đồ → kèm sơ đồ 11 ô [vai trò PES17, X, Y], ô 0 GK
        foreach (var f in doc.RootElement.GetProperty("_formations").EnumerateObject())
        {
            var rows = f.Value.EnumerateArray().Select(r => r.EnumerateArray().Select(v => v.GetInt32()).ToArray()).ToList();
            Assert.Equal(11, rows.Count);
            Assert.Equal(0, rows[0][0]);
            Assert.All(rows.Skip(1), r => Assert.InRange(r[0], 1, 12));
        }
    }

    [Fact]
    public void Web_layout_maps_to_pes17_roles()
    {
        // 5-3-2 A: LWB/RWB/libero không có trong PES17 → LB/RB/CB
        var l = Pes17Formations.WebLayout(new ClubTactics { Formation = "5-3-2 A" })!;
        var g = Pes17Formations.ToGame(l);
        Assert.Equal(11, g.Length);
        Assert.Equal([0, 3, 52], g[0]);
        Assert.Equal(l.Positions.Select(p => (p.X, p.Y)), g.Skip(1).Select(r => (r[1], r[2])));
        Assert.All(g.Skip(1), r => Assert.InRange(r[0], 1, 12));
        Assert.Contains(g, r => r[0] == 2);   // LB
        Assert.Contains(g, r => r[0] == 3);   // RB
        Assert.Equal(12, g[^1][0]);           // CF

        // Career chọn vai trò: mã vai trò đã lưu thắng vai trò sơ đồ
        var pos = l.Positions.SelectMany(p => new[] { p.X, p.Y }).ToArray();
        var roles = Enumerable.Repeat(19, 10).ToArray();   // CM giữa
        var custom = Pes17Formations.ToGame(Pes17Formations.WebLayout(new ClubTactics { Formation = "5-3-2 A", Positions = pos, Roles = roles })!);
        Assert.All(custom.Skip(1), r => Assert.Equal(5, r[0]));

        Assert.Null(Pes17Formations.WebLayout(new ClubTactics()));   // chưa chọn sơ đồ → giữ sơ đồ game
        Assert.Null(Pes17Formations.WebLayout(null));
    }

    [Fact]
    public void Role_codes_round_trip()
    {
        foreach (var n in new[] { "GK", "CB", "LB", "RB", "DM", "CM", "LM", "RM", "AM", "LW", "RW", "SS", "CF" })
            Assert.Equal(n, Pes17Formations.Name(Pes17Formations.Code(n)));
        Assert.Equal(2, Pes17Formations.Code("LWB"));
        Assert.Equal(1, Pes17Formations.Code("CWP"));
    }
}
