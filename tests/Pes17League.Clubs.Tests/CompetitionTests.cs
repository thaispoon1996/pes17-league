using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pes17League.Clubs.Application;

namespace Pes17League.Clubs.Tests;

public class CompetitionTests
{
    [Fact]
    public void Double_round_robin_8_clubs_mirrored()
    {
        var rounds = RoundRobin.DoubleRoundRobin(Enumerable.Range(0, 8).ToList());
        Assert.Equal(14, rounds.Count);
        Assert.All(rounds, r => Assert.Equal(8, r.SelectMany(f => new[] { f.Home, f.Away }).Distinct().Count()));
        var all = rounds.SelectMany(r => r).ToList();
        Assert.Equal(56, all.Distinct().Count());                       // mỗi cặp gặp 2 lần, mỗi lần 1 sân
        for (var i = 0; i < 7; i++)
            Assert.Equal(rounds[i].Select(f => (f.Away, f.Home)), rounds[i + 7]);   // lượt về đối xứng
        Assert.All(Enumerable.Range(0, 8), c => Assert.Equal(7, all.Count(f => f.Home == c)));
    }

    [Fact]
    public void Standings_tie_breaks()
    {
        PlayedFixture<string> F(string h, string a, int hg, int ag) => new(h, a, hg, ag);
        // Cả 3 đội 3 điểm, hiệu số 0. A chỉ 1 bàn → cuối. B, C cùng 2 bàn → đối đầu: B thắng C (dù tên/thứ tự nhập ngược).
        var rows = Standings.Compute(["C", "A", "B"], [F("A", "B", 1, 0), F("B", "C", 2, 1), F("C", "A", 1, 0)], s => s);
        Assert.Equal(["B", "C", "A"], rows.Select(r => r.Club));
        Assert.Equal(3, rows[0].Points);
        // Hoà toàn bộ, kể cả đối đầu → theo tên.
        var tie = Standings.Compute(["Y", "X"], [F("X", "Y", 1, 1)], s => s);
        Assert.Equal(["X", "Y"], tie.Select(r => r.Club));
    }

    [Fact]
    public void Team_result_form() => Assert.Equal((0.5, 0.0, -0.5), (TeamResultForm.For(2, 1), TeamResultForm.For(1, 1), TeamResultForm.For(0, 3)));

    [Fact]
    public void Rating_form_and_match_delta()
    {
        Assert.Equal((1.0, 0.5, 0.0, -0.5, 1.0), (RatingForm.For(8.0m), RatingForm.For(7.0m), RatingForm.For(6.0m), RatingForm.For(5.0m), RatingForm.For(9.5m)));
        Assert.Equal((1, 0, 0, -1), (AttributeEvolution.MatchDelta(7.5m), AttributeEvolution.MatchDelta(7.0m), AttributeEvolution.MatchDelta(5.5m), AttributeEvolution.MatchDelta(5.0m)));
        Assert.Contains("GOAL KEEPING", AttributeEvolution.RoleFields(Position.GK));
    }

    /// <summary>Mùa 2 CLB (Arsenal, Aston Villa) = 2 vòng: mở mùa → đóng CN/sinh OF → chạy/crash/chạy lại → kết quả → tự chốt vòng → vòng 2 → hết mùa.</summary>
    [DbFact]
    public async Task Season_flow_close_window_play_settle_finish()
    {
        await using var t = await TestDb.CreateAsync();
        await using var db = t.Context();
        var svc = new CompetitionService(db, t.Store, t.Clock);
        var season = await svc.CreateSeasonAsync("Mùa 1", 1);
        var import = await new ImportPatchService(db, t.Store, t.Clock).ImportAsync("PES6VN 25-26", Env.OrigOptionFile, season.Id,
            [new LeagueClub(0, "Arsenal"), new LeagueClub(1)]);
        await new EconomyService(db, t.Clock).SetParametersAsync(season.Id, new(InitialBudgetFund: 100_000_000));
        var arsenal = await db.ClubPesMappings.Where(m => m.PatchVersionId == import.PatchVersionId && m.PesClubIndex == 0).Select(m => m.ClubId).SingleAsync();

        var start = await svc.StartSeasonAsync(season.Id, import.PatchVersionId, arsenal);
        Assert.True(start.Ok, start.Error.ToString());
        Assert.Equal(2, start.Matchdays);
        Assert.Equal(100_000_000, start.Budgets!.Values.Sum());
        Assert.Equal(ControllerType.Human, (await db.TeamAssignments.SingleAsync(a => a.ClubId == arsenal && a.To == null)).ControllerType);
        Assert.False((await svc.StartSeasonAsync(season.Id, import.PatchVersionId)).Ok);

        var md1 = await db.Matchdays.SingleAsync(m => m.SeasonId == season.Id && m.Number == 1);
        Assert.Equal(MatchdayStatus.WindowOpen, md1.Status);
        Assert.Equal(TransferWindowStatus.Open, (await db.TransferWindows.FindAsync(md1.TransferWindowId))!.Status);
        var f1 = await db.Fixtures.SingleAsync(f => f.MatchdayId == md1.Id);
        // PO 17/11 bỏ khoá vòng: vòng mở là đá được (không còn chặn "chưa đóng CN")

        var close = await svc.CloseTransferWindowAsync(md1.Id);
        Assert.True(close.Ok, string.Join("; ", close.Issues?.Select(i => i.Message) ?? []));
        Assert.Equal((MatchdayStatus.Locked, close.OptionFileBuildId), (md1.Status, md1.OptionFileBuildId));
        Assert.Equal(TransferWindowStatus.Closed, (await db.TransferWindows.FindAsync(md1.TransferWindowId))!.Status);

        // Lần 1 crash lúc tải trận → chạy lại.
        var a1 = await svc.StartFixtureAsync(f1.Id, "m1");
        Assert.Equal(CompetitionError.WrongFixtureStatus, (await svc.StartFixtureAsync(f1.Id, "m1b")).Error);
        Assert.Null(await svc.EndAttemptAsync(a1.AttemptId!.Value, MatchAttemptOutcome.Crashed));
        var a2 = await svc.StartFixtureAsync(f1.Id, "m1");
        Assert.True(a2.Ok);

        var home = f1.HomeClubId;
        var away = f1.AwayClubId;
        Assert.Equal(CompetitionError.GoalsMismatch,
            (await svc.RecordResultAsync(f1.Id, 2, 0, [new GoalInput(home, 1, 10)], ResultSource.Memory, a2.AttemptId)).Error);
        var homeStarter = await db.SquadMemberships.Where(m => m.ClubId == home && m.LeftAt == null && m.SlotOrder == 0).Select(m => m.PlayerId).SingleAsync();
        var awayStarter = await db.SquadMemberships.Where(m => m.ClubId == away && m.LeftAt == null && m.SlotOrder == 0).Select(m => m.PlayerId).SingleAsync();
        var homeSub = await db.SquadMemberships.Where(m => m.ClubId == home && m.LeftAt == null && m.SlotOrder == 15).Select(m => m.PlayerId).SingleAsync();
        // Tăng theo trận chặn ở tiềm năng (PO 19/11): cho thủ môn đội nhà 18 tuổi để còn chỗ tăng.
        var snap = await db.PlayerAttributes.SingleAsync(a => a.PlayerId == homeStarter && a.SeasonId == season.Id);
        snap.Values = new Dictionary<string, int>(snap.Values) { ["AGE"] = 18 };
        await db.SaveChangesAsync();

        var overall0 = db.PlayerPrices.AsNoTracking().Single(x => x.PlayerId == homeStarter && x.SeasonId == season.Id).Overall;
        var r1 = await svc.RecordResultAsync(f1.Id, 2, 1, [new GoalInput(home, 1, 10), new GoalInput(away, 2, 50), new GoalInput(home, 2, 88, 1234.5)],
            ResultSource.Memory, a2.AttemptId, "/rec/m1.mkv",
            ratings: [new(home, homeStarter, 0, 8.0m), new(away, awayStarter, 0, 5.0m), new(home, homeSub, 11, null)]);
        Assert.True(r1.Ok, r1.Error.ToString());
        Assert.NotNull(r1.Settlement);
        Assert.False(r1.SeasonFinished);
        Assert.Equal(3, await db.Goals.CountAsync(g => g.FixtureId == f1.Id));
        Assert.Equal([MatchAttemptOutcome.Crashed, MatchAttemptOutcome.Finished],
            await db.MatchAttempts.Where(a => a.FixtureId == f1.Id).OrderBy(a => a.StartedAt).ThenBy(a => a.Outcome).Select(a => a.Outcome).ToListAsync());
        // Lệnh lỗi phía trên đã rollback + xoá change tracker → md1 trong test đã tách, đọc lại.
        Assert.Equal(MatchdayStatus.Settled, (await db.Matchdays.AsNoTracking().SingleAsync(m => m.Id == md1.Id)).Status);
        Assert.True(await db.RoundSettlements.AnyAsync(s => s.MatchdayId == md1.Id));

        double Form(Guid p) => db.PlayerPrices.Single(x => x.PlayerId == p && x.SeasonId == season.Id).FormMultiplier;
        // Phong độ theo điểm cá nhân (8,0 → +5%, 5,0 → −2,5%, không điểm → giữ).
        Assert.Equal(1.05, Form(homeStarter), 6);
        Assert.Equal(0.975, Form(awayStarter), 6);
        Assert.Equal(1.0, Form(homeSub), 6);
        // Chỉ số hợp vai trò ±1 (8,0 ≥ 7,5; 5,0 ≤ 5,0) → giá gốc tính lại.
        var homePos = (await db.Players.FindAsync(homeStarter))!.Position;
        var homeChanges = await db.AttributeChanges.Where(c => c.PlayerId == homeStarter).ToListAsync();
        Assert.Equal(AttributeEvolution.RoleFields(homePos).Order(), homeChanges.Select(c => c.Field).Order());
        Assert.All(homeChanges, c => Assert.Equal((1, f1.Id), (c.Delta, c.FixtureId)));
        Assert.All(await db.AttributeChanges.Where(c => c.PlayerId == awayStarter).ToListAsync(), c => Assert.Equal(-1, c.Delta));
        Assert.False(await db.AttributeChanges.AnyAsync(c => c.PlayerId == homeSub));
        Assert.Equal(overall0 + 1, db.PlayerPrices.AsNoTracking().Single(x => x.PlayerId == homeStarter && x.SeasonId == season.Id).Overall);

        var md2 = await db.Matchdays.SingleAsync(m => m.SeasonId == season.Id && m.Number == 2);
        Assert.Equal(MatchdayStatus.WindowOpen, md2.Status);
        var f2 = await db.Fixtures.SingleAsync(f => f.MatchdayId == md2.Id);
        Assert.Equal((f1.AwayClubId, f1.HomeClubId), (f2.HomeClubId, f2.AwayClubId));

        Assert.True((await svc.CloseTransferWindowAsync(md2.Id)).Ok);
        // OF vòng 2 mang chỉ số đã tiến hoá.
        var of2 = OptionFile.OptionFileCodec.Decode(await t.Store.ReadAsync((await db.OptionFileBuilds.SingleAsync(b => b.MatchdayId == md2.Id)).StoragePath!));
        var pesId = await db.PlayerPesMappings.Where(m => m.PlayerId == homeStarter).Select(m => m.PesPlayerId).SingleAsync();
        var field = homeChanges[0].Field;
        var start0 = (await db.PlayerAttributes.SingleAsync(a => a.PlayerId == homeStarter && a.SeasonId == season.Id)).Values[field];
        Assert.Equal(start0 + 1, of2.Get(pesId, OptionFile.PlayerField.Verified.Single(f => f.Name == field)));
        var r2 = await svc.RecordResultAsync(f2.Id, 1, 1, [], ResultSource.Admin);   // admin nhập tay, không bàn thắng chi tiết
        Assert.True(r2.Ok);
        Assert.True(r2.SeasonFinished);
        Assert.Equal(SeasonStatus.Finished, (await db.Seasons.FindAsync(season.Id))!.Status);

        var table = await svc.GetStandingsAsync(season.Id);
        Assert.Equal(home, table[0].Club);
        Assert.Equal((4, 2, 3, 2), (table[0].Points, table[0].Played, table[0].GoalsFor, table[0].GoalsAgainst));
        Assert.Equal(1, table[1].Points);
    }

    /// <summary>8 CLB → vòng 1 có 4 trận; ghi kết quả 4 trận cùng lúc từ 2 DbContext → vòng chốt đúng 1 lần.</summary>
    [DbFact]
    public async Task Two_results_at_once_settle_round_exactly_once()
    {
        await using var t = await TestDb.CreateAsync();
        Guid seasonId, md1;
        List<Guid> fixtures;
        await using (var db = t.Context())
        {
            var svc = new CompetitionService(db, t.Store, t.Clock);
            seasonId = (await svc.CreateSeasonAsync("Mùa 1", 1)).Id;
            var import = await new ImportPatchService(db, t.Store, t.Clock).ImportAsync("PES6VN 25-26", Env.OrigOptionFile, seasonId,
                [.. Enumerable.Range(0, 8).Select(i => new LeagueClub(i))]);
            Assert.True((await svc.StartSeasonAsync(seasonId, import.PatchVersionId)).Ok);
            md1 = await db.Matchdays.Where(m => m.SeasonId == seasonId && m.Number == 1).Select(m => m.Id).SingleAsync();
            Assert.True((await svc.CloseTransferWindowAsync(md1)).Ok);
            fixtures = await db.Fixtures.Where(f => f.MatchdayId == md1).Select(f => f.Id).ToListAsync();
            foreach (var f in fixtures) Assert.True((await svc.StartFixtureAsync(f, null)).Ok);   // vòng đã Playing như luồng thật
        }
        Assert.Equal(4, fixtures.Count);

        // Kết nối thứ 3 giữ khoá 4 dòng Fixture → 4 lệnh ghi kết quả cùng dồn lại, nhả ra cùng lúc (không có khoá vòng → không ai chốt).
        await using var blocker = new NpgsqlConnection(t.ConnectionString);
        await blocker.OpenAsync();
        var hold = await blocker.BeginTransactionAsync();
        await using (var cmd = new NpgsqlCommand($"SELECT 1 FROM clubs.\"Fixtures\" WHERE \"MatchdayId\" = '{md1}' FOR UPDATE", blocker, hold))
            await cmd.ExecuteNonQueryAsync();
        var running = fixtures.Select(async id =>
        {
            await using var db = t.Context();
            return await new CompetitionService(db, t.Store, t.Clock).RecordResultAsync(id, 1, 0, [], ResultSource.Admin);
        }).ToList();
        await Task.Delay(1500);
        await hold.RollbackAsync();
        var results = await Task.WhenAll(running);
        Assert.All(results, r => Assert.True(r.Ok, r.Error.ToString()));
        Assert.Single(results, r => r.Settlement is not null);

        await using var check = t.Context();
        Assert.Equal(1, await check.RoundSettlements.CountAsync(s => s.MatchdayId == md1));
        Assert.Equal(MatchdayStatus.Settled, (await check.Matchdays.SingleAsync(m => m.Id == md1)).Status);
        var md2 = await check.Matchdays.SingleAsync(m => m.SeasonId == seasonId && m.Number == 2);
        Assert.Equal(MatchdayStatus.WindowOpen, md2.Status);
        Assert.Equal(1, await check.TransferWindows.CountAsync(w => w.MatchdayId == md2.Id));
    }
}
