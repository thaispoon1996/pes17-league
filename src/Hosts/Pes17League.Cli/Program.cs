using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Application;
using Pes17League.Clubs.Domain;
using Pes17League.Clubs.Infrastructure;

// CLI alpha (chưa có API/web): dựng mùa alpha, đá tuần tự các trận BOT của vòng hiện tại qua tools/match/pesmatch.py, xem BXH.
//   setup-alpha OF_GOC HUMAN_IDX IDX...   tạo mùa + nhập patch + bắt đầu mùa (CLB HUMAN_IDX = PO) + đóng kỳ CN vòng 1
//   setup-federation OF_GOC [TEN_MUA]     lưu trữ mùa alpha + tạo mùa đầu liên đoàn + nhập 101 CLB từ patch (BOT, nước, logo); chưa sinh lịch
//   import-pes17 PESDB_JSON [TEN_MUA]     tạo mùa (Planned) + nhập toàn bộ CLB PES17 (bỏ đội tuyển) + cầu thủ từ tools/pes17db/pesdb.py export
//   export-pes17-squads RA_JSON           đội hình hiện tại (patch PES17 mới nhất) → JSON cho tools/pes17db/deploy.sh
//   setup-pes17-leagues LEAGUES_JSON PES_DOI_PO   mùa chưa xong: 5 giải (pesdb.py leagues) + mùa giải + CLB; PO cầm CLB mã PES_DOI_PO
//   setup-pes17-world WORLD_JSON [--dry]  Career (PO 19/11): mọi giải + cúp quốc gia + C1/C2 thể thức mới (pesdb.py world); mùa chưa đá trận nào thì làm lại; CLB người → BOT
//   start-pes17-season [SEED [TEN_TK PES_DOI]] [--dry]  bắt đầu mùa Career (kinh tế lương 2‰, lịch, chợ hè), giao CLB cho TEN_TK (web: /su-nghiep)
//   career-continue [MAX_KHUNG] [--all]  (--all: mô phỏng cả trận người chơi) mô phỏng các trận không có người chơi tới trận kế tiếp của người chơi / hết mùa
//   career-next-season [SEED]             mùa trước xong → sang mùa mới (tuổi + phát triển, giải nghệ, lên/xuống hạng, C1/C2, lịch, ngân sách)
//   open-market                           mở chợ tự do PES17 cho mùa chưa xong (cần quỹ ngân sách đã lưu)
//   move-player PES_CT PES_DOI|free       admin chuyển cầu thủ (mã PES17) sang CLB (mã đội PES17) / thả tự do, qua chợ tự do
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
    case "import-pes17" when args.Length >= 2:
        await ImportPes17Async(args[1], args.Length >= 3 ? args[2] : "Mùa 1");
        break;
    case "export-pes17-squads" when args.Length >= 2:
        await ExportPes17SquadsAsync(args[1]);
        break;
    case "setup-pes17-leagues" when args.Length >= 3:
        await SetupPes17LeaguesAsync(args[1], int.Parse(args[2]));
        break;
    case "setup-pes17-world" when args.Length >= 2:
        await SetupPes17WorldAsync(args[1], args.Contains("--dry"));
        break;
    case "schedule-preview":
        await SchedulePreviewAsync(args.Length >= 2 ? int.Parse(args[1]) : 1);
        break;
    case "start-pes17-season":
        {
            var rest = args.Skip(1).Where(a => a != "--dry").ToList();
            var sd = rest.Count > 0 && int.TryParse(rest[0], out var x) ? x : 1;
            await StartPes17SeasonAsync(sd, args.Contains("--dry"), rest.Count >= 3 ? rest[1] : null, rest.Count >= 3 ? int.Parse(rest[2]) : null);
        }
        break;
    case "career-continue":
        await CareerContinueAsync(args.Skip(1).Where(a => a != "--all").Select(int.Parse).FirstOrDefault(500), args.Contains("--all"));
        break;
    case "career-next-season":
        await CareerNextSeasonAsync(args.Length > 1 ? int.Parse(args[1]) : Random.Shared.Next(1, 10_000));
        break;
    case "open-market":
        await OpenMarketAsync();
        break;
    case "move-player" when args.Length >= 3:
        await MovePlayerAsync(int.Parse(args[1]), args[2] == "free" ? null : int.Parse(args[2]));
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
    case "fix-club-names":
        await FixClubNamesAsync(args.Contains("--dry"));
        break;
    case "assign-club" when args.Length >= 3:
        await AssignClubAsync(args[1], int.Parse(args[2]));
        break;
    case "set-password" when args.Length >= 3:
        await SetPasswordAsync(args[1], args[2]);
        break;
    default:
        Console.Error.WriteLine("Dùng: setup-alpha OF_GOC HUMAN_IDX IDX... | setup-federation OF_GOC [TEN_MUA] | import-pes17 PESDB_JSON [TEN_MUA] | export-pes17-squads RA_JSON | setup-pes17-leagues LEAGUES_JSON PES_DOI_PO | setup-pes17-world WORLD_JSON [--dry] | schedule-preview [SEED] | open-market | move-player PES_CT PES_DOI|free | set-contracts SO_TRAN | play-bots | status | top-up-budget QUY TRAN_MEM | hide-retired | set-visibility true|false|null PES... | end-attempt | assign-club TEN_TK PES_DOI | set-password TEN_TK MAT_KHAU | fix-club-names [--dry]");
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

async Task ImportPes17Async(string jsonPath, string seasonName)
{
    await using var db = Db();
    if (await db.Seasons.AnyAsync(s => s.Status == SeasonStatus.Active)) throw new InvalidOperationException("Đã có mùa đang chạy");
    var season = await new CompetitionService(db, store, clock).CreateSeasonAsync(seasonName, await db.Seasons.CountAsync() + 1);
    var i = await new Pes17ImportService(db, store, clock, importOptions: new(ContractMatches: FederationSetupService.FirstSeasonContractMatches))
        .ImportAsync("PES17 HANO V6", await File.ReadAllBytesAsync(jsonPath), season.Id);
    season.PatchVersionId = i.PatchVersionId;   // như setup-federation: mùa gắn patch ngay khi nhập
    await db.SaveChangesAsync();
    Console.WriteLine($"Mùa {seasonName}: {i.Clubs} CLB, {i.SquadPlayers} cầu thủ trong đội, {i.FreeAgents} tự do, {i.ShirtChanges} số áo đổi, {i.DuplicatePlayers.Count} cầu thủ trùng CLB");
    foreach (var g in i.AddedGoalkeepers) Console.WriteLine($"  thêm GK {g.PlayerName} ({g.GoalKeeping}) → {g.ClubName} số {g.ShirtNumber}");
    foreach (var g in i.GoalkeeperShortages) Console.WriteLine($"  THIẾU GK: {g.ClubName} ({g.Goalkeepers})");
}

async Task ExportPes17SquadsAsync(string outPath)
{
    await using var db = Db();
    var patch = await db.PatchVersions.Where(p => p.Name.StartsWith("PES17")).OrderByDescending(p => p.ImportedAt).FirstAsync();
    var squads = await new Pes17SquadExportService(db).ExportAsync(patch.Id);
    await File.WriteAllTextAsync(outPath, squads.ToJson());
    Console.WriteLine($"{patch.Name}: {squads.Squads.Count} CLB, {squads.Squads.Values.Sum(s => s.Count)} cầu thủ, {squads.Formations.Count} sơ đồ → {outPath}");
}

async Task<Season> OpenSeasonAsync(ClubsDbContext db) =>
    await db.Seasons.Where(s => s.Status != SeasonStatus.Finished).OrderBy(s => s.Number).FirstAsync();

// PO chọn 18/11/2026: 5 giải lớn châu Âu, 1 hạng (không lên/xuống, chưa có cúp), vòng tròn 2 lượt 3/1/0.
async Task SetupPes17LeaguesAsync(string leaguesPath, int humanPesClub)
{
    await using var db = Db();
    var season = await OpenSeasonAsync(db);
    var clubOf = await db.ClubPesMappings.Where(m => m.PatchVersionId == season.PatchVersionId).ToDictionaryAsync(m => m.PesClubIndex, m => m.ClubId);
    var admin = new CompetitionAdminService(db, clock);
    using var doc = System.Text.Json.JsonDocument.Parse(await File.ReadAllBytesAsync(leaguesPath));
    foreach (var l in doc.RootElement.EnumerateObject())
    {
        var name = l.Value.GetProperty("name").GetString()!;
        var comp = await db.Competitions.SingleOrDefaultAsync(c => c.Name == name && c.Status == CompetitionStatus.Active)
            ?? await admin.CreateAsync(new CompetitionInput(name, $"{l.Name} 1", l.Name, 1, CompetitionType.DomesticLeague, null, new CompetitionRules(), null, null), "cli");
        var edition = await db.Editions.SingleOrDefaultAsync(e => e.CompetitionId == comp.Id && e.SeasonId == season.Id)
            ?? await admin.CreateEditionAsync(comp.Id, season.Id, "cli");
        var teams = l.Value.GetProperty("teams").EnumerateArray().Select(t => t.GetInt32()).ToList();
        var missing = teams.Where(t => !clubOf.ContainsKey(t)).ToList();
        if (missing.Count > 0) throw new InvalidOperationException($"{name}: đội chưa nhập {string.Join(",", missing)}");
        foreach (var t in teams) await admin.SetEditionClubAsync(edition.Id, clubOf[t], null, "cli");
        Console.WriteLine($"{name}: {teams.Count} CLB");
    }
    await new ClubAdminService(db, clock, store).SetHumanClubAsync(clubOf[humanPesClub], "cli");
    Console.WriteLine($"PO cầm: {(await db.Clubs.FindAsync(clubOf[humanPesClub]))!.Name}");
}

async Task SetupPes17WorldAsync(string worldPath, bool dry)
{
    await using var db = Db();
    var world = System.Text.Json.JsonSerializer.Deserialize<Pes17World>(await File.ReadAllBytesAsync(worldPath),
        new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    var r = await new Pes17WorldService(db, clock).SetupAsync(world, "cli", dry);
    Console.WriteLine($"{(dry ? "(chạy thử) " : "")}làm lại mùa: {r.Reset}, CLB người → BOT: {r.ReleasedClubs}, {r.Countries} nước, {r.Competitions.Count} giải/cúp");
    foreach (var e in r.Allocation.Editions.OrderBy(e => e.Competition.Type).ThenBy(e => e.Competition.Country).ThenBy(e => e.Competition.Tier))
        Console.WriteLine($"  {e.Competition.Name}: {e.Clubs.Count} CLB" + (e.Competition.Type == CompetitionType.ContinentalCup
            ? " — hạt giống 1–5: " + string.Join(", ", e.Clubs.OrderBy(c => c.Seed).Take(5).Select(c => c.Club.Name)) : ""));
    Console.WriteLine($"  không dự giải: {r.Allocation.Unassigned.Count} CLB" + (r.OutsideLeagues.Count > 0 ? $" (trong đó ngoài giải của nước mình: {string.Join(", ", r.OutsideLeagues)})" : ""));
    foreach (var n in r.Allocation.Notes) Console.WriteLine($"  ghi chú: {n}");
    foreach (var e in r.Allocation.Errors) Console.WriteLine($"  LỖI: {e}");
}

async Task SchedulePreviewAsync(int seed)
{
    await using var db = Db();
    var season = await OpenSeasonAsync(db);
    var plan = await new SeasonScheduleService(db, clock).PreviewAsync(season.Id, seed);
    Console.WriteLine($"{plan.SeasonName}: {plan.Slots.Count} khung ngày, {plan.Slots.Sum(s => s.Matches.Count)} trận (xem trước, chưa ghi)");
    foreach (var s in plan.Slots.Take(3).Concat(plan.Slots.TakeLast(2)))
        Console.WriteLine($"  khung {s.Number} ({s.Slot}): {s.Matches.Count} trận");
}

// PO 19/11/2026: kinh tế + ghi lịch (mã SEED) + bắt đầu mùa + mở chợ hè, như nút "Bắt đầu sự nghiệp" trên web (Pes17CareerService);
// TEN_TK PES_DOI (không bắt buộc) = giao CLB cho tài khoản đó.
async Task StartPes17SeasonAsync(int seed, bool dry, string? user, int? pesClub)
{
    await using var db = Db();
    var career = new Pes17CareerService(db, store, clock);
    var season = await career.PendingSeasonAsync();
    if (season is null) { Console.Error.WriteLine("Không có mùa PES17 nào chờ bắt đầu (cần setup-pes17-world)"); return; }
    var change = await career.EconomyAsync(season.Id);
    var pv = await new EconomyAdminService(db, clock).PreviewAsync(season.Id, change);
    var budgets = pv.Clubs.Select(c => c.Budget).OrderBy(x => x).ToList();
    Console.WriteLine($"{season.Name}: {pv.Clubs.Count} CLB, quỹ {change.InitialBudgetFund / 1e6:N0} tr ({Pes17CareerService.FundShare:P0}), trần mềm {change.SoftWageCap / 1e6:N3} tr/khung, lương {Pes17CareerService.WagePermille}‰, k {Pes17CareerService.BudgetExponent}");
    Console.WriteLine($"  ngân sách: thấp nhất {budgets[0] / 1e6:N2} · trung vị {budgets[budgets.Count / 2] / 1e6:N2} · cao nhất {budgets[^1] / 1e6:N2} tr");
    foreach (var c in pv.Clubs.OrderByDescending(c => c.SquadValue).Take(3))
        Console.WriteLine($"  {c.Name}: giá trị {c.SquadValue / 1e6:N1} tr, lương {c.Payroll / 1e6:N3} tr/khung, ngân sách {c.Budget / 1e6:N2} tr, đủ {c.Khung:0.#} khung");
    Guid? club = null, manager = null;
    if (user is not null && pesClub is { } pc)
    {
        manager = await db.Managers.Where(m => m.Username == user).Select(m => (Guid?)m.Id).SingleOrDefaultAsync() ?? throw new InvalidOperationException($"Không có tài khoản {user}");
        club = await db.ClubPesMappings.Where(m => m.PatchVersionId == season.PatchVersionId && m.PesClubIndex == pc).Select(m => (Guid?)m.ClubId).SingleAsync();
    }
    if (dry)
    {
        var plan = await new SeasonScheduleService(db, clock).PreviewAsync(season.Id, seed);
        Console.WriteLine($"  lịch mã {seed}: {plan.Slots.Count} khung ngày, {plan.Slots.Sum(s => s.Matches.Count)} trận (chạy thử, chưa ghi gì)");
        return;
    }
    var r = await career.StartAsync(club, manager, seed, "cli");
    Console.WriteLine($"  đã bắt đầu {r.Season}: {r.Slots} khung, {r.Matches} trận; CLB người: {r.Club}");
}

async Task CareerContinueAsync(int maxSlots, bool all)
{
    await using var db = Db();
    var sw = System.Diagnostics.Stopwatch.StartNew();
    var r = await new Pes17CareerService(db, store, clock).ContinueAsync(maxSlots, line => Console.WriteLine($"  {sw.Elapsed:mm\\:ss} {line}"), all);
    Console.WriteLine($"Mô phỏng {r.Simulated} trận / {r.Slots.Count} khung trong {sw.Elapsed:mm\\:ss}" + (r.SeasonFinished ? " — HẾT MÙA" : ""));
    if (r.MyFixtureId is { } f)
    {
        var x = await (from fx in db.Fixtures join h in db.Clubs on fx.HomeClubId equals h.Id join w in db.Clubs on fx.AwayClubId equals w.Id
                       join m in db.Matchdays on fx.MatchdayId equals m.Id where fx.Id == f select new { h.Name, Away = w.Name, m.Number }).SingleAsync();
        Console.WriteLine($"Dừng ở trận của người chơi: khung {x.Number} {x.Name} – {x.Away} ({f})");
    }
    foreach (var e in r.Errors.Take(10)) Console.WriteLine($"  LỖI {e}");
}

async Task CareerNextSeasonAsync(int seed)
{
    await using var db = Db();
    var sw = System.Diagnostics.Stopwatch.StartNew();
    var r = await new SeasonRolloverService(db, store, clock).NextSeasonAsync(seed, "cli", line => Console.WriteLine($"  {sw.Elapsed:mm\\:ss} {line}"));
    Console.WriteLine($"{r.Season}: {r.Players} cầu thủ cộng tuổi, giải nghệ {r.Retired} (hoãn {r.Deferred}), BOT ký thay {r.Replaced}; "
        + $"{r.Start.Slots} khung / {r.Start.Matches} trận, quỹ {r.Start.Fund:N0} — {sw.Elapsed:mm\\:ss}");
    if (r.MyRetired.Count > 0) Console.WriteLine($"  CLB người chơi giải nghệ: {string.Join(", ", r.MyRetired)}");
    foreach (var m in r.Moves) Console.WriteLine($"  {m}");
}

async Task OpenMarketAsync()
{
    await using var db = Db();
    var season = await OpenSeasonAsync(db);
    var r = await new FreeMarketService(db, clock).OpenAsync(season.Id, "cli");
    Console.WriteLine($"{season.Name}: chợ tự do mở — {r.Prices} giá, {r.Wages} lương, ngân sách {r.Budgets} CLB");
}

async Task MovePlayerAsync(int pesPlayer, int? pesClub)
{
    await using var db = Db();
    var season = await OpenSeasonAsync(db);
    var player = await db.PlayerPesMappings.Where(m => m.PatchVersionId == season.PatchVersionId && m.PesPlayerId == pesPlayer).Select(m => m.PlayerId).SingleAsync();
    Guid? club = pesClub is { } pc
        ? await db.ClubPesMappings.Where(m => m.PatchVersionId == season.PatchVersionId && m.PesClubIndex == pc).Select(m => m.ClubId).SingleAsync()
        : null;
    var r = await new FreeMarketService(db, clock).AdminMoveAsync(season.Id, player, club, "cli");
    Console.WriteLine(r.Ok ? $"OK, giao dịch {r.TransferId}" : $"LỖI: {r.Error}");
}

/// <summary>PO 18/11 (sửa UI): chuẩn hoá tên CLB đã nhập ("Arsenal Fc" → "Arsenal FC"), xem <see cref="ClubNames"/>.</summary>
async Task FixClubNamesAsync(bool dry)
{
    await using var db = Db();
    var n = 0;
    foreach (var c in await db.Clubs.ToListAsync())
    {
        var p = ClubNames.Pretty(c.Name);
        if (p == c.Name) continue;
        if (n++ < 15) Console.WriteLine($"  {c.Name} → {p}");
        c.Name = p;
    }
    if (!dry) await db.SaveChangesAsync();
    Console.WriteLine($"{n} tên CLB {(dry ? "sẽ đổi (chạy thử)" : "đã đổi")}");
}

/// <summary>PO 18/11: giao CLB cho tài khoản người chơi (admin không cầm CLB). CLB đang do admin cầm (ParticipantId null) → trả admin về không cầm trước.</summary>
/// <summary>Đặt mật khẩu tài khoản HLV (PO 18/11: test1/test2 = 123). Không in mật khẩu.</summary>
async Task SetPasswordAsync(string username, string password)
{
    await using var db = Db();
    var m = await db.Managers.SingleAsync(x => x.Username == username);
    m.PasswordHash = ManagerService.Hash(password);
    await db.SaveChangesAsync();
    Console.WriteLine($"Đã đặt mật khẩu cho {username}");
}

async Task AssignClubAsync(string username, int pesClub)
{
    await using var db = Db();
    var season = await OpenSeasonAsync(db);
    var manager = await db.Managers.SingleAsync(m => m.Username == username);
    var club = await db.ClubPesMappings.Where(m => m.PatchVersionId == season.PatchVersionId && m.PesClubIndex == pesClub).Select(m => m.ClubId).SingleAsync();
    var svc = new ClubAdminService(db, clock);
    if (await db.TeamAssignments.AnyAsync(a => a.ClubId == club && a.To == null && a.ControllerType == ControllerType.Human && a.ParticipantId == null))
        await svc.SetHumanClubAsync(null, "cli");
    await svc.SetHumanClubAsync(club, "cli", manager.Id);
    Console.WriteLine($"OK: {username} cầm CLB PES {pesClub}");
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
