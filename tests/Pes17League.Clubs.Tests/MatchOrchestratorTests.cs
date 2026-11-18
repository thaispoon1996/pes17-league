using System.Buffers.Binary;
using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Application;

namespace Pes17League.Clubs.Tests;

public class MatchOrchestratorTests
{
    private sealed class FakeRunner(Func<MatchRunRequest, int, Task<MatchRunOutcome>> play) : IMatchRunner
    {
        public ConcurrentQueue<MatchRunRequest> Calls { get; } = new();
        private readonly ConcurrentDictionary<Guid, int> _attempts = new();
        private int _running;
        public int MaxConcurrent;

        public async Task<MatchRunOutcome> RunAsync(MatchRunRequest r, CancellationToken ct)
        {
            Calls.Enqueue(r);
            var n = Interlocked.Increment(ref _running);
            lock (this) MaxConcurrent = Math.Max(MaxConcurrent, n);
            try { return await play(r, _attempts.AddOrUpdate(r.FixtureId, 1, (_, a) => a + 1)); }
            finally { Interlocked.Decrement(ref _running); }
        }
    }

    internal static async Task<(Guid Season, Guid Md1, Guid Human)> SetupAsync(TestDb t, int clubs, bool human, bool lockRound = true)
    {
        await using var db = t.Context();
        var svc = new CompetitionService(db, t.Store, t.Clock);
        var season = (await svc.CreateSeasonAsync("Mùa 1", 1)).Id;
        var import = await new ImportPatchService(db, t.Store, t.Clock).ImportAsync("PES6VN 25-26", Env.OrigOptionFile, season,
            [.. Enumerable.Range(0, clubs).Select(i => new LeagueClub(i))]);
        var arsenal = await db.ClubPesMappings.Where(m => m.PatchVersionId == import.PatchVersionId && m.PesClubIndex == 0).Select(m => m.ClubId).SingleAsync();
        Assert.True((await svc.StartSeasonAsync(season, import.PatchVersionId, human ? arsenal : null)).Ok);
        var md1 = await db.Matchdays.Where(m => m.SeasonId == season && m.Number == 1).Select(m => m.Id).SingleAsync();
        if (lockRound) Assert.True((await svc.CloseTransferWindowAsync(md1)).Ok);
        return (season, md1, arsenal);
    }

    /// <summary>4 CLB, Arsenal = PO: trận BOT crash 1 lần → chạy lại → ghi kết quả; trận của PO chờ; PO đá xong → chốt vòng.</summary>
    [DbFact]
    public async Task Bot_fixture_retries_after_crash_then_human_fixture_settles_round()
    {
        await using var t = await TestDb.CreateAsync();
        var (_, md1, arsenal) = await SetupAsync(t, 4, human: true);
        var runner = new FakeRunner((r, attempt) => Task.FromResult(attempt == 1
            ? new MatchRunOutcome(false, 0, 0, [], "game_exited")
            : new MatchRunOutcome(true, 2, 1, [new(MatchSide.Home, 1, 12, 300), new(MatchSide.Away, 2, 60, 900), new(MatchSide.Home, 2, 89, 1200)], RecordingPath: "/rec/x.mkv")));
        var orch = new MatchOrchestrator(t.Context, t.Store, t.Clock, runner);

        var report = await orch.PlayBotFixturesAsync(md1);
        var bot = Assert.Single(report.Fixtures);
        Assert.Equal((true, 2), (bot.Played, bot.Attempts));
        var humanFixture = Assert.Single(report.WaitingForHuman);
        Assert.Null(report.Settlement);

        await using (var db = t.Context())
        {
            var f = await db.Fixtures.SingleAsync(x => x.Id == bot.FixtureId);
            Assert.Equal((FixtureStatus.Played, 2, 1, ResultSource.Memory), (f.Status, f.HomeGoals, f.AwayGoals, f.ResultSource));
            Assert.Equal([2, 1], await db.Goals.Where(g => g.FixtureId == f.Id).GroupBy(g => g.ClubId == f.HomeClubId)
                .OrderByDescending(g => g.Key).Select(g => g.Count()).ToListAsync());
            Assert.Equal([MatchAttemptOutcome.Crashed, MatchAttemptOutcome.Finished],
                await db.MatchAttempts.Where(a => a.FixtureId == f.Id).OrderBy(a => a.Outcome).Select(a => a.Outcome).ToListAsync());
            var hf = await db.Fixtures.SingleAsync(x => x.Id == humanFixture);
            Assert.Contains(arsenal, new[] { hf.HomeClubId, hf.AwayClubId });
            // Yêu cầu chạy trận: đúng chỉ số CLB trong OF của patch + OF của vòng.
            var req = runner.Calls.First();
            var idx = await db.ClubPesMappings.Where(m => m.ClubId == f.HomeClubId || m.ClubId == f.AwayClubId).ToDictionaryAsync(m => m.ClubId, m => m.PesClubIndex);
            Assert.Equal((idx[f.HomeClubId], idx[f.AwayClubId], "pes17-bench", (MatchSide?)null), (req.HomePesClubIndex, req.AwayPesClubIndex, req.ContainerName, req.HumanSide));
            Assert.Equal(Env.OrigOptionFile.Length, req.OptionFile.Length);
        }

        MatchSide side;
        await using (var db = t.Context())
            side = await db.Fixtures.Where(x => x.Id == humanFixture).Select(x => x.HomeClubId == arsenal ? MatchSide.Home : MatchSide.Away).SingleAsync();
        var po = await orch.PlayFixtureAsync(humanFixture, side);
        Assert.Equal((true, 2), (po.Played, po.Attempts));   // fake crash lần đầu của mọi trận
        Assert.Equal(side, runner.Calls.Last().HumanSide);
        await using var check = t.Context();
        Assert.Equal(MatchdayStatus.Settled, (await check.Matchdays.SingleAsync(m => m.Id == md1)).Status);
    }

    /// <summary>C18: điểm cầu thủ theo ID PES (bộ nhớ) và số áo (chụp màn) được ghép cầu thủ và lưu cùng kết quả; dòng không ghép được bị bỏ.</summary>
    [DbFact]
    public async Task Ratings_are_stored_with_result()
    {
        await using var t = await TestDb.CreateAsync();
        var (_, md1, _) = await SetupAsync(t, 2, human: false);
        Guid home, away, starter, sub, shirtPlayer;
        int starterPes, subPes, shirt;
        await using (var db = t.Context())
        {
            var f = await db.Fixtures.SingleAsync(x => x.MatchdayId == md1);
            (home, away) = (f.HomeClubId, f.AwayClubId);
            var hs = await (from m in db.SquadMemberships join pm in db.PlayerPesMappings on m.PlayerId equals pm.PlayerId
                            where m.ClubId == home && m.LeftAt == null orderby m.SlotOrder select new { m.PlayerId, pm.PesPlayerId }).ToListAsync();
            (starter, starterPes, sub, subPes) = (hs[0].PlayerId, hs[0].PesPlayerId, hs[12].PlayerId, hs[12].PesPlayerId);
            var am = await db.SquadMemberships.Where(m => m.ClubId == away && m.LeftAt == null).OrderBy(m => m.SlotOrder).FirstAsync();
            (shirtPlayer, shirt) = (am.PlayerId, am.ShirtNumber);
        }
        var runner = new FakeRunner((_, _) => Task.FromResult(new MatchRunOutcome(true, 0, 0, [], Ratings:
        [
            new(MatchSide.Home, 0, starterPes, null, 7.5m),
            new(MatchSide.Home, 11, subPes, null, 6.0m),
            new(MatchSide.Home, 12, 999999, null, 6.0m),      // ID không có trong patch → bỏ
            new(MatchSide.Away, 0, null, shirt, null),
        ])));
        Assert.True(Assert.Single((await new MatchOrchestrator(t.Context, t.Store, t.Clock, runner).PlayBotFixturesAsync(md1)).Fixtures).Played);

        await using var check = t.Context();
        var rows = await check.MatchRatings.OrderBy(r => r.ClubId == away).ThenBy(r => r.Slot).ToListAsync();
        Assert.Equal([(home, starter, 0, (decimal?)7.5m), (home, sub, 11, 6.0m), (away, shirtPlayer, 0, null)],
            rows.Select(r => (r.ClubId, r.PlayerId, r.Slot, r.Rating)).ToList());
    }

    private sealed class FakeHighlights : IHighlightRenderer
    {
        public List<HighlightClip> Clips { get; } = [];

        public Task RenderAsync(Guid fixtureId, string recordingPath, IReadOnlyList<HighlightClip> clips, CancellationToken ct)
        {
            Clips.AddRange(clips);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// B3: thống kê (khối bộ nhớ trận thử 3, Real 0–1 Barça) ghi cùng kết quả: 2 dòng đội, 20 diễn biến, chỉ số cầu thủ gộp vào dòng điểm; ô chỉ ghép
    /// được bằng ID PES trong khối (không có điểm) → thêm dòng điểm null; ô không ghép được → bỏ. File thô ở StatsDir. Highlight: chỉ bàn thắng.
    /// </summary>
    [DbFact]
    public async Task Stats_and_highlights_are_stored_with_result()
    {
        await using var t = await TestDb.CreateAsync();
        var (_, md1, _) = await SetupAsync(t, 2, human: false);
        Guid fixture, home, away, h10, h6, a8;
        int h10Pes, h6Pes, a8Pes;
        string a8Name;
        await using (var db = t.Context())
        {
            var f = await db.Fixtures.SingleAsync(x => x.MatchdayId == md1);
            (fixture, home, away) = (f.Id, f.HomeClubId, f.AwayClubId);
            var squad = await (from m in db.SquadMemberships join pm in db.PlayerPesMappings on m.PlayerId equals pm.PlayerId join p in db.Players on m.PlayerId equals p.Id
                               where m.LeftAt == null orderby m.SlotOrder select new { m.ClubId, m.PlayerId, pm.PesPlayerId, p.Name }).ToListAsync();
            var (hs, aw) = (squad.Where(x => x.ClubId == home).ToList(), squad.Where(x => x.ClubId == away).ToList());
            (h10, h10Pes, h6, h6Pes) = (hs[10].PlayerId, hs[10].PesPlayerId, hs[6].PlayerId, hs[6].PesPlayerId);
            (a8, a8Pes, a8Name) = (aw[8].PlayerId, aw[8].PesPlayerId, aw[8].Name);
        }
        var raw = MatchStatsDecoderTests.Raw(3);
        var ids = new byte[0x48];   // ID PES trong khối: chỉ ô 6 nhà (ID thật của Real/Barça không thuộc 2 CLB test)
        BinaryPrimitives.WriteUInt16LittleEndian(ids.AsSpan(2 * 6), (ushort)h6Pes);
        var highlights = new FakeHighlights();
        var runner = new FakeRunner((_, _) => Task.FromResult(new MatchRunOutcome(true, 0, 1, raw.Goals, RecordingPath: "/rec/x.mkv",
            Ratings: [new(MatchSide.Home, 10, h10Pes, null, 7.0m), new(MatchSide.Away, 8, a8Pes, null, 8.5m)], Moments: raw.Moments,
            StatsBlocks: [.. raw.MemoryBlocks().Select(b => b.Name == "ids" ? b with { Data = ids } : b)])));
        var dir = Path.Combine(Path.GetTempPath(), $"pes17league-stats-{Guid.NewGuid():N}");
        try
        {
            var report = Assert.Single((await new MatchOrchestrator(t.Context, t.Store, t.Clock, runner, new() { StatsDir = dir }, highlights)
                .PlayBotFixturesAsync(md1)).Fixtures);
            Assert.Equal((true, (string?)null, (string?)null), (report.Played, report.StatsError, report.HighlightError));
            Assert.Equal(raw.Moments, MatchStatsRaw.Parse(await File.ReadAllTextAsync(Path.Combine(dir, $"{fixture:N}.json"))).Moments);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }

        await using var check = t.Context();
        Assert.Equal([(home, true, 49, 6), (away, false, 51, 5)],
            (await check.MatchTeamStats.OrderByDescending(s => s.IsHome).ToListAsync()).Select(s => (s.ClubId, s.IsHome, s.PossessionPct, s.Shots)).ToList());
        var events = await check.MatchEvents.OrderBy(e => e.Seq).ToListAsync();
        Assert.Equal(20, events.Count);
        Assert.All(events, e => Assert.Equal(fixture, e.FixtureId));
        Assert.Equal(a8, Assert.Single(events, e => e.Type == MatchEventType.Goal).PlayerId);
        Assert.Equal([h6, h6], events.Where(e => e.Type == MatchEventType.Corner).Select(e => e.PlayerId!.Value));
        Assert.Equal(3, events.Count(e => e.PlayerId == h10 && e.Type == MatchEventType.Shot));
        Assert.All(events.Where(e => e.Type == MatchEventType.Substitution), e => Assert.Null(e.PlayerId ?? e.OtherPlayerId));   // ô 11/12/8/4 không ghép được
        var rows = await check.MatchRatings.OrderBy(r => r.ClubId == away).ThenBy(r => r.Slot).ToListAsync();
        Assert.Equal([(h6, 6, (decimal?)null, 0), (h10, 10, 7.0m, 0), (a8, 8, 8.5m, 1)], rows.Select(r => (r.PlayerId, r.Slot, r.Rating, r.Goals!.Value)).ToList());
        Assert.Equal((3, 2, (int?)null), (rows[1].Shots!.Value, rows[1].ShotsOnTarget!.Value, rows[1].OutMinute));
        Assert.Equal(2, rows[0].Corners);
        Assert.Equal(a8, (await check.Goals.SingleAsync()).ScorerPlayerId);

        var goal = Assert.Single(highlights.Clips);
        Assert.EndsWith($"70' {a8Name} — Bàn thắng", goal.Caption);
        Assert.Equal((raw.Goals[0].WallSeconds, 8.0, 3.0), (goal.VideoOffsetSec, goal.Pre, goal.Post));
        Assert.Contains("0–1", goal.Caption);
    }

    /// <summary>B3: bàn trong khối đội ≠ tỷ số → bỏ thống kê, báo lỗi; kết quả vẫn ghi, không đá lại.</summary>
    [DbFact]
    public async Task Stats_not_matching_score_are_dropped()
    {
        await using var t = await TestDb.CreateAsync();
        var (_, md1, _) = await SetupAsync(t, 2, human: false);
        var raw = MatchStatsDecoderTests.Raw(3);   // 0–1
        var runner = new FakeRunner((_, _) => Task.FromResult(new MatchRunOutcome(true, 2, 0, [new(MatchSide.Home, 1, 10, 100), new(MatchSide.Home, 2, 60, 900)],
            Moments: raw.Moments, StatsBlocks: raw.MemoryBlocks())));
        var report = Assert.Single((await new MatchOrchestrator(t.Context, t.Store, t.Clock, runner).PlayBotFixturesAsync(md1)).Fixtures);
        Assert.Equal((true, 1), (report.Played, report.Attempts));
        Assert.Contains("≠ tỷ số", report.StatsError);
        await using var check = t.Context();
        var f = await check.Fixtures.SingleAsync(x => x.MatchdayId == md1);
        Assert.Equal((FixtureStatus.Played, 2, 0), (f.Status, f.HomeGoals, f.AwayGoals));
        Assert.False(await check.MatchTeamStats.AnyAsync());
        Assert.False(await check.MatchEvents.AnyAsync());
    }

    /// <summary>B3: ghi thống kê lỗi ở DB (giả bằng trigger) → kết quả vẫn ghi, không kèm thống kê, báo lỗi; không đá lại.</summary>
    [DbFact]
    public async Task Stats_db_failure_keeps_result()
    {
        await using var t = await TestDb.CreateAsync();
        var (_, md1, _) = await SetupAsync(t, 2, human: false);
        await using (var db = t.Context())
            await db.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION clubs.fail_event() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'test: diễn biến lỗi'; END $$;
                CREATE TRIGGER fail_event BEFORE INSERT ON clubs."MatchEvents" FOR EACH ROW EXECUTE FUNCTION clubs.fail_event();
                """);
        var raw = MatchStatsDecoderTests.Raw(3);   // 0–1
        var runner = new FakeRunner((_, _) => Task.FromResult(new MatchRunOutcome(true, 0, 1, raw.Goals, Moments: raw.Moments, StatsBlocks: raw.MemoryBlocks())));
        var report = Assert.Single((await new MatchOrchestrator(t.Context, t.Store, t.Clock, runner).PlayBotFixturesAsync(md1)).Fixtures);
        Assert.Equal((true, 1), (report.Played, report.Attempts));
        Assert.Contains("test: diễn biến lỗi", report.StatsError);
        await using var check = t.Context();
        var f = await check.Fixtures.SingleAsync(x => x.MatchdayId == md1);
        Assert.Equal((FixtureStatus.Played, 0, 1), (f.Status, f.HomeGoals, f.AwayGoals));
        Assert.Equal(1, await check.Goals.CountAsync());
        Assert.False(await check.MatchTeamStats.AnyAsync());
    }

    /// <summary>Trận của PO: đội hình + sơ đồ chốt lúc vào trận — đổi sau khi khoá vòng có trong OF gửi container, OF của vòng giữ nguyên.</summary>
    [DbFact]
    public async Task Human_fixture_uses_lineup_at_kickoff()
    {
        await using var t = await TestDb.CreateAsync();
        var (_, md1, arsenal) = await SetupAsync(t, 2, human: true);
        Guid a, b, fixture;
        MatchSide side;
        await using (var db = t.Context())
        {
            var squad = await db.SquadMemberships.Where(m => m.ClubId == arsenal && m.LeftAt == null).OrderBy(m => m.SlotOrder).Select(m => m.PlayerId).ToListAsync();
            (a, b) = (squad[1], squad[15]);
            Assert.Null(await new LineupService(db).SwapAsync(arsenal, a, b));
            Assert.Null(await new TacticsService(db, t.Clock).SetAsync(arsenal, new("3-4-3 A", BackLine: 2)));
            var f = await db.Fixtures.SingleAsync(x => x.MatchdayId == md1);
            (fixture, side) = (f.Id, f.HomeClubId == arsenal ? MatchSide.Home : MatchSide.Away);
        }
        var runner = new FakeRunner((_, _) => Task.FromResult(new MatchRunOutcome(true, 0, 0, [])));
        Assert.True((await new MatchOrchestrator(t.Context, t.Store, t.Clock, runner).PlayFixtureAsync(fixture, side)).Played);

        await using var check = t.Context();
        var pes = await check.PlayerPesMappings.Where(m => m.PlayerId == a || m.PlayerId == b).ToDictionaryAsync(m => m.PlayerId, m => m.PesPlayerId);
        var sentOf = OptionFile.OptionFileCodec.Decode(Assert.Single(runner.Calls).OptionFile);
        var sent = sentOf.GetSquad(0).Players;
        Assert.Equal((pes[b], pes[a]), (sent[1].PlayerId, sent[15].PlayerId));
        Assert.Equal(("3-4-3 A", 2), (sentOf.GetTactics(0).Formation, sentOf.GetTactics(0).BackLine));
        var md = await check.Matchdays.SingleAsync(m => m.Id == md1);
        var roundOf = OptionFile.OptionFileCodec.Decode(await t.Store.ReadAsync((await check.OptionFileBuilds.SingleAsync(x => x.Id == md.OptionFileBuildId)).StoragePath!));
        var round = roundOf.GetSquad(0).Players;
        Assert.Equal((pes[a], pes[b]), (round[1].PlayerId, round[15].PlayerId));
        Assert.NotEqual("3-4-3 A", roundOf.GetTactics(0).Formation);
    }

    /// <summary>Hết lượt chạy lại → trận không ghi kết quả, vòng không chốt (admin nhập tay).</summary>
    [DbFact]
    public async Task Gives_up_after_max_attempts()
    {
        await using var t = await TestDb.CreateAsync();
        var (_, md1, _) = await SetupAsync(t, 2, human: false);
        var runner = new FakeRunner((_, _) => Task.FromResult(new MatchRunOutcome(false, 0, 0, [], "stalled")));
        var report = await new MatchOrchestrator(t.Context, t.Store, t.Clock, runner, new() { MaxAttempts = 2 }).PlayBotFixturesAsync(md1);
        Assert.Equal((false, 2, "stalled"), (report.Fixtures[0].Played, report.Fixtures[0].Attempts, report.Fixtures[0].LastError));
        await using var db = t.Context();
        Assert.Equal(FixtureStatus.Scheduled, (await db.Fixtures.SingleAsync(f => f.MatchdayId == md1)).Status);
        Assert.Equal(2, await db.MatchAttempts.CountAsync(a => a.Outcome == MatchAttemptOutcome.Crashed));
    }

    /// <summary>8 CLB, 2 container song song: 4 trận, ≤ 2 trận cùng lúc, vòng chốt đúng 1 lần.</summary>
    [DbFact]
    public async Task Parallel_containers_settle_round_once()
    {
        await using var t = await TestDb.CreateAsync();
        var (season, md1, _) = await SetupAsync(t, 8, human: false);
        var runner = new FakeRunner(async (_, _) => { await Task.Delay(200); return new MatchRunOutcome(true, 0, 0, []); });
        var report = await new MatchOrchestrator(t.Context, t.Store, t.Clock, runner, new() { Containers = ["m1", "m2"] }).PlayBotFixturesAsync(md1);

        Assert.Equal(4, report.Fixtures.Count(f => f.Played));
        Assert.NotNull(report.Settlement);
        Assert.Equal(2, runner.MaxConcurrent);
        Assert.Equal(["m1", "m2"], runner.Calls.Select(c => c.ContainerName).Distinct().Order());
        await using var db = t.Context();
        Assert.Equal(1, await db.RoundSettlements.CountAsync(s => s.MatchdayId == md1));
        Assert.Equal(MatchdayStatus.WindowOpen, (await db.Matchdays.SingleAsync(m => m.SeasonId == season && m.Number == 2)).Status);
    }

    /// <summary>PO 17/11 bỏ khoá vòng: vòng mở đá luôn, OF sinh riêng cho 2 đội lúc vào trận; vòng sau mở ngay, cùng 1 kỳ CN suốt mùa.</summary>
    [DbFact]
    public async Task Open_round_plays_without_lock_with_per_fixture_option_file()
    {
        await using var t = await TestDb.CreateAsync();
        var (season, md1, _) = await SetupAsync(t, 4, human: false, lockRound: false);
        var runner = new FakeRunner((_, _) => Task.FromResult(new MatchRunOutcome(true, 1, 0, [new(MatchSide.Home, 1, 10, 100)])));
        var orch = new MatchOrchestrator(t.Context, t.Store, t.Clock, runner);
        var r1 = await orch.PlayBotFixturesAsync(md1);
        Assert.Equal(2, r1.Fixtures.Count(f => f.Played));
        Assert.NotNull(r1.Settlement);
        await using var db = t.Context();
        Assert.Equal(2, await db.OptionFileBuilds.CountAsync(b => b.MatchdayId == md1 && b.Status == OptionFileBuildStatus.Built));   // mỗi trận 1 OF
        var md2 = await db.Matchdays.SingleAsync(m => m.SeasonId == season && m.Number == 2);
        Assert.Equal(MatchdayStatus.WindowOpen, md2.Status);
        var w1 = (await db.Matchdays.SingleAsync(m => m.Id == md1)).TransferWindowId;
        Assert.Equal(w1, md2.TransferWindowId);
        Assert.Null((await db.Matchdays.SingleAsync(m => m.Id == md1)).OptionFileBuildId);
        Assert.Equal(2, (await orch.PlayBotFixturesAsync(md2.Id)).Fixtures.Count(f => f.Played));
    }

    /// <summary>Ổ ghi hình còn dưới mức (PO uỷ quyền 06/11): 30 GB, mỗi trận tốn 6 GB, mức 20 GB → đá 2 trận rồi dừng, 2 trận giữ Scheduled.</summary>
    [DbFact]
    public async Task Low_disk_stops_before_next_fixture()
    {
        await using var t = await TestDb.CreateAsync();
        var (_, md1, _) = await SetupAsync(t, 8, human: false);
        var free = 30L << 30;
        var runner = new FakeRunner((_, _) => { free -= 6L << 30; return Task.FromResult(new MatchRunOutcome(true, 0, 0, [])); });
        var report = await new MatchOrchestrator(t.Context, t.Store, t.Clock, runner, new() { FreeDiskBytes = () => free }).PlayBotFixturesAsync(md1);

        Assert.Equal(2, report.Fixtures.Count(f => f.Played));
        Assert.Equal(18L << 30, report.LowDiskFreeBytes);
        Assert.Null(report.Settlement);
        await using var db = t.Context();
        Assert.Equal(2, await db.Fixtures.CountAsync(f => f.MatchdayId == md1 && f.Status == FixtureStatus.Scheduled));
    }

    /// <summary>Web khởi động lại giữa trận: lượt chạy còn Running trên container của mình → đặt lại (Crashed, trận về Scheduled), đá lại được, vòng chốt.
    /// Lượt Running trên container khác không đụng tới.</summary>
    [DbFact]
    public async Task Stale_running_attempt_is_released_then_replayed()
    {
        await using var t = await TestDb.CreateAsync();
        var (_, md1, _) = await SetupAsync(t, 4, human: false);
        await using (var db = t.Context())
        {
            var svc = new CompetitionService(db, t.Store, t.Clock);
            var ids = await db.Fixtures.Where(f => f.MatchdayId == md1).Select(f => f.Id).ToListAsync();
            Assert.Null((await svc.StartFixtureAsync(ids[0], "pes17-bench")).Error);
            Assert.Null((await svc.StartFixtureAsync(ids[1], "khac")).Error);
        }
        var orch = new MatchOrchestrator(t.Context, t.Store, t.Clock, new FakeRunner((_, _) => Task.FromResult(new MatchRunOutcome(true, 0, 0, []))));

        Assert.Single(await orch.ReleaseStaleAttemptsAsync(md1));
        await using (var db = t.Context())
        {
            var fixtures = await db.Fixtures.Where(f => f.MatchdayId == md1).ToListAsync();
            Assert.Equal(1, fixtures.Count(f => f.Status == FixtureStatus.Scheduled));
            await new CompetitionService(db, t.Store, t.Clock).EndAttemptAsync(
                (await db.MatchAttempts.SingleAsync(a => a.ContainerName == "khac")).Id, MatchAttemptOutcome.Crashed);
        }
        var report = await orch.PlayBotFixturesAsync(md1);
        Assert.Equal(2, report.Fixtures.Count(f => f.Played));
        Assert.NotNull(report.Settlement);
    }

    /// <summary>Dừng sau trận này (nhường máy cho trận PO, uỷ quyền 06/11): xin dừng giữa trận → trận đó đá xong, không bắt trận mới;
    /// trận lỗi lúc đã xin dừng → không đá lại, giữ Scheduled. Đá BOT lần nữa → đá nốt, vòng chốt.</summary>
    [DbFact]
    public async Task Stop_after_current_finishes_running_fixture_then_resumes()
    {
        await using var t = await TestDb.CreateAsync();
        var (_, md1, _) = await SetupAsync(t, 8, human: false);
        var stop = new CancellationTokenSource();
        var orch = new MatchOrchestrator(t.Context, t.Store, t.Clock,
            new FakeRunner((_, _) => { stop.Cancel(); return Task.FromResult(new MatchRunOutcome(true, 0, 0, [])); }));
        var report = await orch.PlayBotFixturesAsync(md1, stopAfterCurrent: stop.Token);
        Assert.Equal((1, true), (report.Fixtures.Count(f => f.Played), report.Stopped));
        Assert.Null(report.Settlement);

        var stop2 = new CancellationTokenSource();
        var failing = new FakeRunner((_, _) => { stop2.Cancel(); return Task.FromResult(new MatchRunOutcome(false, 0, 0, [], "stalled")); });
        var f = Assert.Single((await new MatchOrchestrator(t.Context, t.Store, t.Clock, failing).PlayBotFixturesAsync(md1, stopAfterCurrent: stop2.Token)).Fixtures);
        Assert.Equal((false, 1, "stalled"), (f.Played, f.Attempts, f.LastError));
        await using (var db = t.Context())
            Assert.Equal(3, await db.Fixtures.CountAsync(x => x.MatchdayId == md1 && x.Status == FixtureStatus.Scheduled));

        report = await orch.PlayBotFixturesAsync(md1);
        Assert.Equal((3, false), (report.Fixtures.Count(x => x.Played), report.Stopped));
        Assert.NotNull(report.Settlement);
    }

    /// <summary>Sau trận: một bài từ số liệu, một ảnh đúng mốc bàn + 3 giây. Đăng lại thì không thêm bài.</summary>
    [DbFact]
    public async Task Press_publishes_one_article_and_a_goal_photo()
    {
        await using var t = await TestDb.CreateAsync();
        var (_, md1, _) = await SetupAsync(t, 2, human: false);
        var dir = Path.Combine(Path.GetTempPath(), "pes17league-news-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var rec = Path.Combine(dir, "rec.mkv");
        await File.WriteAllBytesAsync(rec, [1]);
        var grab = new SavingGrabber();
        var press = new MatchPress(t.Context, new TemplateArticleWriter(), grab, dir, t.Clock);
        try
        {
            var runner = new FakeRunner((_, _) => Task.FromResult(new MatchRunOutcome(true, 1, 0, [new(MatchSide.Home, 1, 12, 100)], RecordingPath: rec)));
            var report = Assert.Single((await new MatchOrchestrator(t.Context, t.Store, t.Clock, runner, press: press).PlayBotFixturesAsync(md1)).Fixtures);
            Assert.True(report.Played, report.LastError);
            Assert.Null(report.PressError);
            Assert.Equal([100 + MatchPress.AfterGoalSec], grab.At);
            await using var db = t.Context();
            var article = await db.MatchArticles.SingleAsync();
            Assert.Contains(MatchReportText.Dash(1, 0), article.Title);
            Assert.Contains("không rõ người ghi", article.Body);
            Assert.False(article.ByAi);
            Assert.Equal(t.Clock.GetUtcNow(), article.PublishedAt);
            var photo = await db.MatchPhotos.SingleAsync();
            Assert.Contains("12'", photo.Caption);
            Assert.True(File.Exists(Path.Combine(dir, photo.FileName)));
            Assert.Null(await press.PublishAsync(article.FixtureId, rec, default));
            Assert.Equal(1, await db.MatchArticles.CountAsync());
            Assert.Single(grab.At);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    private sealed class SavingGrabber : IFrameGrabber
    {
        public List<double> At { get; } = [];
        public Task GrabAsync(string recordingPath, IReadOnlyList<FrameGrab> frames, CancellationToken ct)
        {
            foreach (var f in frames)
            {
                At.Add(f.AtSec);
                Directory.CreateDirectory(Path.GetDirectoryName(f.DestPath)!);
                File.WriteAllBytes(f.DestPath, [0xFF, 0xD8]);
            }
            return Task.CompletedTask;
        }
    }
}
