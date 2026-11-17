using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Application;
using Pes17League.Clubs.Domain;
using Pes17League.Clubs.Infrastructure;

// CLI alpha (chưa có API/web): dựng mùa alpha, đá tuần tự các trận BOT của vòng hiện tại qua tools/match/pesmatch.py, xem BXH.
//   setup-alpha OF_GOC HUMAN_IDX IDX...   tạo mùa + nhập patch + bắt đầu mùa (CLB HUMAN_IDX = PO) + đóng kỳ CN vòng 1
//   setup-federation OF_GOC [TEN_MUA]     lưu trữ mùa alpha + tạo mùa đầu liên đoàn + nhập 101 CLB từ patch (BOT, nước, logo); chưa sinh lịch
//   set-contracts SO_TRAN                 mùa chưa bắt đầu: hợp đồng nhập từ patch (chưa đá, chưa có lương) + gia hạn mặc định → SO_TRAN trận
//   play-bots                             đá các trận BOT của vòng đang khoá/đang đá
//   status                                vòng hiện tại + BXH
//   top-up-budget QUY TRAN_MEM            đặt quỹ ngân sách + trần lương mềm mùa đang chạy, bù ngân sách đầu mùa bằng Adjustment
//   hide-retired                          ẩn khỏi chợ cầu thủ giải nghệ (tự do + không thuộc đội tuyển), chỉ người chưa bị admin ép
//   set-visibility true|false|null PES... ép hiện/ẩn (null = theo luật) theo ID PES của patch mùa đang chạy
//   end-attempt                           gỡ lần chạy kẹt Running (vd. DB sập lúc ghi kết quả) → Crashed, trận về Scheduled. Chỉ dùng khi không có trận nào đang chạy
var cs = Environment.GetEnvironmentVariable("PES17_DB") ?? throw new InvalidOperationException("Thiếu PES17_DB");
var storeDir = Environment.GetEnvironmentVariable("PES17_STORE") ?? throw new InvalidOperationException("Thiếu PES17_STORE");
var script = Environment.GetEnvironmentVariable("PESMATCH_SCRIPT") ?? Path.GetFullPath("tools/match/pesmatch.py");
var options = new DbContextOptionsBuilder<ClubsDbContext>()
    .UseNpgsql(cs, o => o.MigrationsHistoryTable("__EFMigrationsHistory", ClubsDbContext.Schema)).Options;
ClubsDbContext Db() => new(options);
var store = new FileSystemOptionFileStore(storeDir);
var clock = TimeProvider.System;

await using (var db = Db()) await db.Database.MigrateAsync();

switch (args.FirstOrDefault())
{
    case "setup-alpha" when args.Length >= 4:
        await SetupAlphaAsync(args[1], int.Parse(args[2]), args[3..].Select(int.Parse).ToList());
        break;
    case "setup-federation" when args.Length >= 2:
        await SetupFederationAsync(args[1], args.Length >= 3 ? args[2] : "Mùa 1");
        break;
    case "set-contracts" when args.Length >= 2:
        await SetContractsAsync(int.Parse(args[1]));
        break;
    case "play-bots":
        await PlayBotsAsync();
        break;
    case "status":
        await StatusAsync();
        break;
    case "top-up-budget" when args.Length >= 3:
        await TopUpBudgetAsync(long.Parse(args[1]), long.Parse(args[2]));
        break;
    case "hide-retired":
        await HideRetiredAsync();
        break;
    case "set-visibility" when args.Length >= 3:
        await SetVisibilityAsync(args[1] == "null" ? null : bool.Parse(args[1]), [.. args[2..].Select(int.Parse)]);
        break;
    case "end-attempt":
        await EndStuckAttemptsAsync();
        break;
    default:
        Console.Error.WriteLine("Dùng: setup-alpha OF_GOC HUMAN_IDX IDX... | setup-federation OF_GOC [TEN_MUA] | set-contracts SO_TRAN | play-bots | status | top-up-budget QUY TRAN_MEM | hide-retired | set-visibility true|false|null PES... | end-attempt");
        return 2;
}
return 0;

async Task SetupAlphaAsync(string ofPath, int humanIdx, List<int> clubs)
{
    await using var db = Db();
    if (await db.Seasons.AnyAsync(s => s.Status == SeasonStatus.Active)) throw new InvalidOperationException("Đã có mùa đang chạy");
    var svc = new CompetitionService(db, store, clock);
    var number = await db.Seasons.CountAsync() + 1;
    var season = await svc.CreateSeasonAsync($"Alpha {number}", number);
    var import = await new ImportPatchService(db, store, clock).ImportAsync($"PES6VN 25-26 alpha {number}", await File.ReadAllBytesAsync(ofPath),
        season.Id, [.. clubs.Select(i => new LeagueClub(i))]);
    var human = await db.ClubPesMappings.Where(m => m.PatchVersionId == import.PatchVersionId && m.PesClubIndex == humanIdx).Select(m => m.ClubId).SingleAsync();
    var start = await svc.StartSeasonAsync(season.Id, import.PatchVersionId, human);
    if (!start.Ok) throw new InvalidOperationException($"Bắt đầu mùa lỗi: {start.Error}");
    var md1 = await db.Matchdays.Where(m => m.SeasonId == season.Id && m.Number == 1).Select(m => m.Id).SingleAsync();
    var close = await svc.CloseTransferWindowAsync(md1);
    if (!close.Ok) throw new InvalidOperationException($"Đóng kỳ CN lỗi: {close.Error}");
    Console.WriteLine($"Mùa {season.Name}: {start.Matchdays} vòng, vòng 1 đã khoá");
    await StatusAsync();
}

async Task SetupFederationAsync(string ofPath, string seasonName)
{
    await using var db = Db();
    var r = await new FederationSetupService(db, store, clock).SetupAsync("PES6VN 25-26", await File.ReadAllBytesAsync(ofPath), seasonName, "cli");
    var i = r.Import;
    Console.WriteLine($"Lưu trữ {r.ArchivedSeasons.Count} mùa, {r.ArchivedClubs} CLB cũ. Mùa mới {seasonName}: {i.Clubs} CLB, {i.SquadPlayers} cầu thủ trong đội, {i.FreeAgents} tự do");
    foreach (var g in i.AddedGoalkeepers) Console.WriteLine($"  thêm GK {g.PlayerName} ({g.GoalKeeping}) → {g.ClubName} số {g.ShirtNumber}");
    foreach (var g in i.GoalkeeperShortages) Console.WriteLine($"  THIẾU GK: {g.ClubName} ({g.Goalkeepers})");
    Console.WriteLine($"  {i.ShirtChanges.Count} số áo trùng đã đổi");
}

async Task SetContractsAsync(int matches)
{
    await using var db = Db();
    var season = await db.Seasons.SingleAsync(s => s.Status == SeasonStatus.Planned);
    var n = await new FederationSetupService(db, store, clock).SetImportedContractLengthAsync(season.Id, matches, "cli");
    Console.WriteLine($"Mùa {season.Name}: {n} hợp đồng nhập → {matches} trận; gia hạn mặc định {matches} trận");
}

async Task PlayBotsAsync()
{
    var md = await CurrentMatchdayAsync() ?? throw new InvalidOperationException("Không có vòng đang khoá/đang đá");
    Console.WriteLine($"Vòng {md.Number}: đá các trận BOT");
    var newsDir = Path.Combine(storeDir, "..", "news");
    Directory.CreateDirectory(newsDir);
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
    var press = new MatchPress(Db, new DeepSeekArticleWriter(http, Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY")), new DockerFrameGrabber(), newsDir, clock);
    var report = await new MatchOrchestrator(Db, store, clock, new ScriptMatchRunner(script, Path.Combine(storeDir, "..", "run"), recordingDir: Path.Combine(storeDir, "..", "recordings")),
        highlights: new ScriptHighlightRenderer(Path.Combine(Path.GetDirectoryName(script)!, "highlight.py"), Path.Combine(storeDir, "..", "highlights")), press: press).PlayBotFixturesAsync(md.Id);
    foreach (var f in report.Fixtures)
    {
        Console.WriteLine($"  {await FixtureTextAsync(f.FixtureId)}  lần thử {f.Attempts}{(f.Played ? "" : $" — CHƯA ĐÁ: {f.LastError}")}");
        if (f.PressError is not null) Console.WriteLine($"    bài tường thuật: {f.PressError}");
    }
    foreach (var id in report.WaitingForHuman) Console.WriteLine($"  chờ PO: {await FixtureTextAsync(id)}");
    if (report.Settlement is not null) Console.WriteLine("  vòng đã chốt");
    await StatusAsync();
}

async Task StatusAsync()
{
    await using var db = Db();
    var season = await db.Seasons.Where(s => s.Status == SeasonStatus.Active).SingleOrDefaultAsync();
    if (season is null) { Console.WriteLine("Không có mùa đang chạy"); return; }
    var names = await db.Clubs.ToDictionaryAsync(c => c.Id, c => c.Name);
    if (await CurrentMatchdayAsync() is { } md)
    {
        Console.WriteLine($"Vòng {md.Number} ({md.Status}):");
        foreach (var f in await db.Fixtures.Where(f => f.MatchdayId == md.Id).ToListAsync()) Console.WriteLine($"  {await FixtureTextAsync(f.Id)}");
    }
    Console.WriteLine("BXH:");
    foreach (var r in await new CompetitionService(db, store, clock).GetStandingsAsync(season.Id))
        Console.WriteLine($"  {names[r.Club],-24} {r.Played,2} {r.Won,2} {r.Drawn,2} {r.Lost,2} {r.GoalsFor,3}-{r.GoalsAgainst,-3} {r.Won * 3 + r.Drawn,3}đ");
}

async Task TopUpBudgetAsync(long fund, long softCap)
{
    await using var db = Db();
    var season = await db.Seasons.Where(s => s.Status == SeasonStatus.Active).SingleAsync();
    var eco = new EconomyService(db, clock);
    await eco.SetParametersAsync(season.Id, new(SoftWageCap: softCap, InitialBudgetFund: fund));
    var topUp = await eco.TopUpInitialBudgetsAsync(season.Id);
    if (topUp.Count == 0) Console.WriteLine("Không bù (mùa chưa chia hoặc đã bù rồi)");
    var names = await db.Clubs.ToDictionaryAsync(c => c.Id, c => c.Name);
    foreach (var (club, amount) in topUp.OrderBy(x => x.Value)) Console.WriteLine($"  {names[club],-24} +{amount:N0}");
}

async Task HideRetiredAsync()
{
    await using var db = Db();
    var patch = await db.Seasons.Where(s => s.Status == SeasonStatus.Active).Select(s => s.PatchVersionId!.Value).SingleAsync();
    var hidden = await new MarketVisibilityService(db, store).HideRetiredAsync(patch);
    Console.WriteLine($"Đã ẩn {hidden.Count} cầu thủ giải nghệ");
}

async Task SetVisibilityAsync(bool? visible, List<int> pesIds)
{
    await using var db = Db();
    var patch = await db.Seasons.Where(s => s.Status == SeasonStatus.Active).Select(s => s.PatchVersionId!.Value).SingleAsync();
    var n = await new MarketVisibilityService(db, store).SetByPesIdsAsync(patch, pesIds, visible);
    Console.WriteLine($"Đã ghi {visible?.ToString() ?? "null"} cho {n}/{pesIds.Count} cầu thủ");
}

async Task EndStuckAttemptsAsync()
{
    await using var db = Db();
    var running = await db.MatchAttempts.Where(a => a.Outcome == MatchAttemptOutcome.Running).ToListAsync();
    if (running.Count == 0) { Console.WriteLine("Không có lần chạy nào kẹt"); return; }
    var svc = new CompetitionService(db, store, clock);
    foreach (var a in running)
    {
        var err = await svc.EndAttemptAsync(a.Id, MatchAttemptOutcome.Crashed);
        Console.WriteLine($"  {await FixtureTextAsync(a.FixtureId)}  (bắt đầu {a.StartedAt.ToLocalTime():dd/MM HH:mm}){(err is null ? "" : $" — lỗi: {err}")}");
    }
}

async Task<Matchday?> CurrentMatchdayAsync()
{
    await using var db = Db();
    return await (from m in db.Matchdays
                  join s in db.Seasons on m.SeasonId equals s.Id
                  where s.Status == SeasonStatus.Active && (m.Status == MatchdayStatus.Locked || m.Status == MatchdayStatus.Playing)
                  orderby m.Number
                  select m).FirstOrDefaultAsync();
}

async Task<string> FixtureTextAsync(Guid id)
{
    await using var db = Db();
    var f = await db.Fixtures.SingleAsync(x => x.Id == id);
    var names = await db.Clubs.Where(c => c.Id == f.HomeClubId || c.Id == f.AwayClubId).ToDictionaryAsync(c => c.Id, c => c.Name);
    var score = f.Status == FixtureStatus.Played
        ? $"{f.HomeGoals}-{f.AwayGoals}" + (f.HomePenalties is { } ph ? $" (pen {ph}-{f.AwayPenalties})" : "") : f.Status.ToString();
    return $"{names[f.HomeClubId]} {score} {names[f.AwayClubId]}";
}
