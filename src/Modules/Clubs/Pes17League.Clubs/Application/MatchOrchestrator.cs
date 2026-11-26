using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Infrastructure;

namespace Pes17League.Clubs.Application;

public enum MatchSide { Home, Away }

/// <param name="HumanSide">Bên do người điều khiển (trận PO); null = COM vs COM.</param>
/// <param name="BothHuman">Giao hữu: 2 người chung máy (pesmatch "both": bàn phím/cảm ứng = nhà, tay cầm ô 2 = khách).</param>
/// <param name="Go">Chuẩn bị trận sẵn (PO 05/11): có → game đi menu tới màn Thiết lập rồi chờ, Go xong mới vào trận.</param>
/// <param name="ExtraTime">Chung kết (PO 06/11): hoà sau 90' → game tự đá hiệp phụ rồi loạt penalty.</param>
/// <param name="Refresh">Vào trận nhanh (PO 16/11, cần <paramref name="Go"/>): game dựng sẵn tới màn Chọn đội ("warm"), lúc Go dựng lại OF
/// (đội hình mới nhất) và ghi squad + đội hình 2 đội vào bộ nhớ game rồi mới chọn đội.</param>
/// <param name="Sync">Cùng <paramref name="Refresh"/> (PES17, PO 21/11): HLV sửa đội hình trong lúc chờ → bộ chạy dựng lại ảnh chụp và
/// gửi "sync FILE" để game cài lại ngay, lúc bấm Sẵn sàng thường không phải khởi động lại game nữa.</param>
/// <param name="CompetitionLogo">Logo giải của trận (đường dẫn web /img/upload/…, PO 22/11): hiện góc phải trên màn hình chơi (stream).</param>
/// <param name="Hot">Trận cầu đinh (<see cref="HotMatchService"/>, PO 23/11): record-mode = hot thì chỉ ghi hình + phát trực tiếp trận này.</param>
/// <param name="CompetitionId">Giải của trận (PO 23/11): bộ chạy cài gói đồ hoạ menu của giải (trang /admin/do-hoa) trước khi vào trận.</param>
public sealed record MatchRunRequest(Guid FixtureId, string ContainerName, byte[] OptionFile, int HomePesClubIndex, int AwayPesClubIndex, MatchSide? HumanSide, Task? Go = null,
    bool ExtraTime = false, Func<CancellationToken, Task<byte[]>>? Refresh = null, bool BothHuman = false, SquadSync? Sync = null,
    string? CompetitionLogo = null, bool Hot = false, Guid? CompetitionId = null, string? CompetitionName = null, string? Conditions = null,
    IReadOnlyList<RunCoach>? Coaches = null);

/// <summary>HLV career của đội người chơi (PO 26/11, mẫu A): ảnh mặt tách nền + tên → thẻ góc dưới màn hình chơi.</summary>
public sealed record RunCoach(MatchSide Side, string Name, string Club, byte[] Face);

/// <summary>Báo "đội hình vừa đổi" trong lúc game dựng sẵn chờ Sẵn sàng (gộp nhiều lần báo thành 1).</summary>
public sealed class SquadSync
{
    private readonly Channel<bool> ch = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
    public void Changed() => ch.Writer.TryWrite(true);
    public ChannelReader<bool> Reader => ch.Reader;
}

/// <param name="Half">1 = hiệp 1, 2 = hiệp 2, 3/4 = hiệp phụ 1/2 (phút 91–120).</param>
/// <param name="Slot">Ô cầu thủ ghi bàn (cùng thứ tự ô với RunRating); null = không xác định.</param>
/// <param name="Shirt">Số áo + tên trên dải người ghi bàn của game (PO 26/11); ghép theo đội hình CLB ghi bàn, tên phải khớp (phản lưới → không ghép).</param>
public sealed record RunGoal(MatchSide Side, int Half, int Minute, double WallSeconds, int? Slot = null, int? Shirt = null, string? Name = null);

/// <summary>Điểm 1 cầu thủ (C18): theo ID cầu thủ PES (bộ nhớ) hoặc số áo (chụp màn).</summary>
public sealed record RunRating(MatchSide Side, int Slot, int? PesPlayerId, int? Shirt, decimal? Rating);

/// <param name="Finished">false = crash/treo/không vào được trận → chạy lại.</param>
/// <param name="Ratings">Điểm cầu thủ; rỗng khi không đọc được (không ảnh hưởng kết quả trận).</param>
/// <param name="Penalties">Loạt penalty (chung kết hoà sau 120'); null = không đá hoặc không đọc được (→ chờ admin nhập). Home/AwayGoals = tỷ số sau 120'.</param>
/// <param name="Moments">Mốc từng pha lúc đá (B3: highlight + ghép mốc video cho diễn biến).</param>
/// <param name="StatsBlocks">Khối bộ nhớ thống kê lúc hết giờ (<see cref="MatchStatsDecoder"/>); null = không đọc được (không ảnh hưởng kết quả trận).</param>
public sealed record MatchRunOutcome(bool Finished, int HomeGoals, int AwayGoals, IReadOnlyList<RunGoal> Goals, string? Error = null, string? RecordingPath = null,
    IReadOnlyList<RunRating>? Ratings = null, (int Home, int Away)? Penalties = null, IReadOnlyList<RunMoment>? Moments = null,
    IReadOnlyList<MemoryBlock>? StatsBlocks = null, bool KickedOff = false);

/// <param name="VideoOffsetSec">Vị trí pha bóng trong bản ghi hình.</param>
/// <param name="Caption">Chữ trên video, vd "MCI 0–1 MUN · 11' Sesko — Bàn thắng".</param>
/// <param name="Pre">Số giây lấy trước mốc; <paramref name="Post"/>: sau mốc (bàn thắng 15/5, sút trúng đích 8/4 — B3).</param>
public sealed record HighlightClip(double VideoOffsetSec, string Caption, double Pre = 8, double Post = 3);   // PO 17/11: cắt gọn (trước 15/5)

/// <summary>Cắt highlight sau trận (C7): ghép clip quanh các bàn từ bản ghi hình. Bản thật: Infrastructure.ScriptHighlightRenderer.</summary>
public interface IHighlightRenderer
{
    Task RenderAsync(Guid fixtureId, string recordingPath, IReadOnlyList<HighlightClip> clips, CancellationToken ct);
}

/// <summary>Chạy 1 trận trong 1 container: nạp OF, vào trận, đá hết, đọc kết quả từ bộ nhớ. Bản thật: Infrastructure.ScriptMatchRunner.</summary>
public interface IMatchRunner
{
    Task<MatchRunOutcome> RunAsync(MatchRunRequest request, CancellationToken ct);
}

public sealed class MatchOrchestratorOptions
{
    /// <summary>Container trận dùng được; số trận song song = số container (mặc định 1 → tuần tự).</summary>
    public IReadOnlyList<string> Containers { get; init; } = ["pes17-bench"];
    /// <summary>Số lần chạy tối đa mỗi trận (pes6 có thể crash/treo lúc tải trận); hết lượt → để admin nhập tay.</summary>
    public int MaxAttempts { get; init; } = 3;
    /// <summary>Chặn ổ đầy khi đá BOT không người trông (PO uỷ quyền 06/11): trước mỗi trận hỏi số byte còn trống của ổ ghi hình,
    /// dưới <see cref="MinFreeDiskBytes"/> → không đá tiếp, trận còn lại giữ Scheduled (không xoá gì). null = không kiểm.</summary>
    public Func<long>? FreeDiskBytes { get; init; }
    public long MinFreeDiskBytes { get; init; } = 20L << 30;
    /// <summary>Thư mục file thô thống kê <c>{fixture:N}.json</c> (khối bộ nhớ + mốc từng pha + bàn thắng, B3): giải mã thêm (thẻ…) thì điền lại
    /// từ đây, không cần đá lại. null = không lưu.</summary>
    public string? StatsDir { get; init; }
    /// <summary>Sân + thời tiết trận (PO 25/11): CLB nhà → JSON cho bộ chạy (pesmatch17 match_conditions, menu STADIUM). null/lỗi = để nguyên menu.</summary>
    public Func<Guid, CancellationToken, Task<string?>>? Conditions { get; init; }
}

/// <param name="HighlightError">Lỗi cắt highlight (không ảnh hưởng kết quả trận).</param>
/// <param name="StatsError">Thống kê trận không đọc/giải mã được hoặc không khớp tỷ số → không ghi thống kê (kết quả trận vẫn ghi, không đá lại).</param>
public sealed record FixtureRunReport(Guid FixtureId, bool Played, int Attempts, string? LastError, string? HighlightError = null, string? StatsError = null, string? PressError = null);

/// <param name="LowDiskFreeBytes">Dừng giữa chừng vì ổ ghi hình còn ít (số byte trống lúc dừng); trận chưa đá giữ Scheduled.</param>
/// <param name="Stopped">Dừng giữa chừng theo yêu cầu (nhường máy cho trận PO); trận chưa đá giữ Scheduled.</param>
public sealed record MatchdayRunReport(IReadOnlyList<FixtureRunReport> Fixtures, IReadOnlyList<Guid> WaitingForHuman, RoundSettlementResult? Settlement, bool SeasonFinished,
    long? LowDiskFreeBytes = null, bool Stopped = false);

/// <summary>
/// Alpha bước 2: điều phối chạy trận của 1 vòng đã khoá (OF đã sinh). Tự đá các trận BOT vs BOT (COM vs COM),
/// trận có CLB Human để PO đá (<see cref="PlayFixtureAsync"/> với HumanSide). Mỗi trận 1 DbContext riêng (song song an toàn;
/// chốt vòng đúng 1 lần nhờ khoá dòng vòng trong <see cref="CompetitionService.RecordResultAsync"/>).
/// </summary>
public sealed class MatchOrchestrator(Func<ClubsDbContext> dbFactory, IOptionFileStore store, TimeProvider clock, IMatchRunner runner,
    MatchOrchestratorOptions? options = null, IHighlightRenderer? highlights = null, IMatchPress? press = null)
{
    private readonly MatchOrchestratorOptions _o = options ?? new();

    /// <summary>Đá mọi trận BOT vs BOT còn Scheduled của vòng (song song tối đa = số container).</summary>
    /// <param name="stopAfterCurrent">Huỷ token này = đá xong trận đang đá thì dừng (không bắt trận mới, không đá lại lượt lỗi), khác <paramref name="ct"/> là bỏ ngang trận.</param>
    public async Task<MatchdayRunReport> PlayBotFixturesAsync(Guid matchdayId, CancellationToken ct = default, CancellationToken stopAfterCurrent = default)
    {
        List<Guid> bot, human;
        await using (var db = dbFactory())
        {
            var fixtures = await db.Fixtures.Where(f => f.MatchdayId == matchdayId && f.Status == FixtureStatus.Scheduled).ToListAsync(ct);
            var humans = await HumanClubsAsync(db, fixtures.SelectMany(f => new[] { f.HomeClubId, f.AwayClubId }), ct);
            bot = fixtures.Where(f => !NeedsHuman(f, humans)).Select(f => f.Id).ToList();
            human = fixtures.Where(f => NeedsHuman(f, humans)).Select(f => f.Id).ToList();
        }

        var queue = new Queue<Guid>(bot);
        var reports = new List<FixtureRunReport>();
        RecordResult? last = null;
        long? lowDisk = null;
        var stopped = false;
        async Task Worker(string container)
        {
            while (true)
            {
                Guid id;
                lock (queue)
                {
                    if (lowDisk is not null || stopped || queue.Count == 0) return;
                    if (stopAfterCurrent.IsCancellationRequested) { stopped = true; return; }
                    if (_o.FreeDiskBytes?.Invoke() is { } free && free < _o.MinFreeDiskBytes) { lowDisk = free; return; }
                    id = queue.Dequeue();
                }
                var (report, result) = await RunFixtureAsync(id, container, null, ct, stop: stopAfterCurrent);
                lock (reports)
                {
                    reports.Add(report);
                    if (result?.Settlement is not null) last = result;
                }
            }
        }
        await Task.WhenAll(_o.Containers.Select(Worker));
        return new MatchdayRunReport(reports, human, last?.Settlement, last?.SeasonFinished ?? false, lowDisk, stopped);
    }

    /// <summary>
    /// Lượt chạy còn Running trên container của mình = sót từ tiến trình trước (web khởi động lại giữa trận) → Crashed, trận về Scheduled
    /// để đá lại. Chỉ gọi khi tiến trình này không đá trận nào trên các container đó (MatchJobService: 1 việc mỗi lúc).
    /// </summary>
    public async Task<IReadOnlyList<Guid>> ReleaseStaleAttemptsAsync(Guid matchdayId, CancellationToken ct = default)
    {
        await using var db = dbFactory();
        var containers = _o.Containers.ToArray();
        var stale = await (from a in db.MatchAttempts
                           join f in db.Fixtures on a.FixtureId equals f.Id
                           where f.MatchdayId == matchdayId && a.Outcome == MatchAttemptOutcome.Running && containers.Contains(a.ContainerName)
                           select a).ToListAsync(ct);
        var svc = new CompetitionService(db, store, clock);
        foreach (var a in stale) await svc.EndAttemptAsync(a.Id, MatchAttemptOutcome.Crashed, ct: ct);
        return [.. stale.Select(a => a.FixtureId)];
    }

    /// <summary>Đá 1 trận (vd trận của PO: humanSide = bên của PO).</summary>
    /// <param name="go">Chuẩn bị sẵn: trận có người → game chờ ở màn Chọn đội, đội hình vẫn sửa được tới khi go xong (<see cref="MatchRunRequest.Refresh"/>);
/// BOT → chờ ở màn Thiết lập (<see cref="MatchRunRequest.Go"/>).</param>
    /// <param name="both">Trận giải người vs người (PO 17/11): 2 HLV chung máy 2 người (pesmatch "both"), không thử lại khi lỗi (người chơi bấm Sẵn sàng lại).</param>
    public async Task<FixtureRunReport> PlayFixtureAsync(Guid fixtureId, MatchSide? humanSide, string? container = null, CancellationToken ct = default, Task? go = null,
        bool both = false, SquadSync? sync = null) =>
        (await RunFixtureAsync(fixtureId, container ?? _o.Containers[0], humanSide, ct, go, both: both, sync: sync)).Report;

    private async Task<(FixtureRunReport Report, RecordResult? Result)> RunFixtureAsync(Guid fixtureId, string container, MatchSide? humanSide, CancellationToken ct, Task? go = null,
        CancellationToken stop = default, bool both = false, SquadSync? sync = null)
    {
        string? error = null;
        var max = both ? 1 : _o.MaxAttempts;
        for (var attempt = 1; attempt <= max; attempt++)
        {
            if (attempt > 1 && stop.IsCancellationRequested) return (new FixtureRunReport(fixtureId, false, attempt - 1, error), null);   // xin dừng: không đá lại, trận giữ Scheduled
            await using var db = dbFactory();
            var svc = new CompetitionService(db, store, clock);
            var start = await svc.StartFixtureAsync(fixtureId, container, ct);
            if (!start.Ok) return (new FixtureRunReport(fixtureId, false, attempt - 1, start.Error.ToString()), null);

            MatchRunOutcome outcome;
            try
            {
                var req = await RequestAsync(db, fixtureId, container, humanSide, ct) with { Go = go, BothHuman = both };
                if (go is not null && humanSide is not null)
                    req = req with { Refresh = async c => { await using var db2 = dbFactory(); return (await RequestAsync(db2, fixtureId, container, humanSide, c)).OptionFile; }, Sync = sync };
                outcome = await runner.RunAsync(req, ct);
            }
            catch (OperationCanceledException)
            {
                await svc.EndAttemptAsync(start.AttemptId!.Value, MatchAttemptOutcome.Aborted, ct: CancellationToken.None);
                throw;
            }
            catch (Exception e)
            {
                outcome = new MatchRunOutcome(false, 0, 0, [], e.Message);
            }

            if (outcome.Finished)
            {
                var f = (await db.Fixtures.FindAsync([fixtureId], ct))!;
                var ratings = await RatingsAsync(db, f, outcome.Ratings ?? [], ct);
                var byShirt = outcome.Goals.Any(g => g.Shirt is not null) ? await ShirtsAsync(db, f, ct) : [];
                var goals = outcome.Goals.Select(g =>
                {
                    var club = g.Side == MatchSide.Home ? f.HomeClubId : f.AwayClubId;   // người ghi: ô → cầu thủ qua bảng điểm
                    var scorer = g.Slot is { } s ? ratings.FirstOrDefault(r => r.ClubId == club && r.Slot == s)?.PlayerId : null;
                    scorer ??= g.Shirt is { } sh ? byShirt.FirstOrDefault(m => m.ClubId == club && m.Shirt == sh && ScorerName.Matches(g.Name, m.Name))?.PlayerId : null;
                    return new GoalInput(club, g.Half, g.Minute, g.WallSeconds, scorer);
                }).ToList();
                // Penalty đọc sai (vd hoà loạt penalty) coi như không đọc được: vẫn ghi tỷ số 120', chờ admin nhập (PO 06/11), không đá lại.
                (int Home, int Away)? pens = outcome.Penalties is { } p && f.Stage == FixtureStage.Final
                    && Fixture.PenaltiesValid(outcome.HomeGoals, outcome.AwayGoals, p.Home, p.Away) ? p : null;
                var (stats, statsError) = await StatsAsync(db, f, outcome, ratings, ct);
                Task<RecordResult> Record(MatchStatsInput? s) => svc.RecordResultAsync(fixtureId, outcome.HomeGoals, outcome.AwayGoals, goals, ResultSource.Memory,
                    start.AttemptId, outcome.RecordingPath, ct, ratings, pens, s);
                RecordResult r;
                try { r = await Record(stats); }
                catch (Exception e) when (stats is not null && e is not OperationCanceledException)
                {
                    // ghi thống kê hỏng cả lần ghi (vd lỗi DB) → ghi kết quả không kèm thống kê, không đá lại
                    statsError = $"ghi thống kê lỗi: {(e.InnerException ?? e).Message}";
                    db.ChangeTracker.Clear();
                    r = await Record(null);
                }
                if (r.Ok) return (new FixtureRunReport(fixtureId, true, attempt, null, await HighlightAsync(db, f, outcome, goals, ratings, ct), statsError,
                    await PressAsync(fixtureId, outcome.RecordingPath, ct)), r);
                error = $"Ghi kết quả lỗi: {r.Error}";   // vd bàn thắng đọc được không khớp tỷ số → chạy lại
                await using var db2 = dbFactory();
                await new CompetitionService(db2, store, clock).EndAttemptAsync(start.AttemptId!.Value, MatchAttemptOutcome.Crashed, outcome.RecordingPath, ct);
                if (humanSide is not null) return (new FixtureRunReport(fixtureId, false, attempt, error), null);   // trận có người: không đá lại thay HLV (PO 25/11)
                continue;
            }
            error = outcome.Error ?? "Không rõ lỗi";
            await svc.EndAttemptAsync(start.AttemptId!.Value, MatchAttemptOutcome.Crashed, outcome.RecordingPath, ct);
            // người chơi không giao bóng (vắng/rớt mạng): thử lại cũng chỉ chờ tiếp → dừng, để HLV bấm Chuẩn bị lại (24/11)
            if (error.StartsWith("no_kickoff") || error.StartsWith("human_timeout")) return (new FixtureRunReport(fixtureId, false, attempt, error), null);
            // PO 25/11: trận có người đã giao bóng rồi mới lỗi → không tự vào lại trận không người (giữ máy hàng chục phút), trả máy để HLV chuẩn bị lại
            if (humanSide is not null && outcome.KickedOff) return (new FixtureRunReport(fixtureId, false, attempt, error), null);
        }
        return (new FixtureRunReport(fixtureId, false, max, error), null);
    }

    public const int MaxHighlightClips = 12;

    /// <summary>
    /// Highlight trận vừa ghi kết quả: chỉ bàn thắng (8 s trước, 3 s sau), tối đa <see cref="MaxHighlightClips"/> clip.
    /// Không có bản ghi hình hoặc không có bàn có mốc video → bỏ qua. Trả lỗi thay vì ném.
    /// </summary>
    private async Task<string?> HighlightAsync(ClubsDbContext db, Fixture f, MatchRunOutcome outcome, IReadOnlyList<GoalInput> goals, IReadOnlyList<RatingInput> ratings,
        CancellationToken ct)
    {
        if (highlights is null || outcome.RecordingPath is null) return null;
        try
        {
            var timed = goals.Where(g => g.VideoOffsetSec is not null).OrderBy(g => g.VideoOffsetSec).Take(MaxHighlightClips).ToList();
            if (timed.Count == 0) return null;

            var clubs = await db.Clubs.Where(c => c.Id == f.HomeClubId || c.Id == f.AwayClubId).ToDictionaryAsync(c => c.Id, c => c.ShortName ?? c.Name, ct);
            var ids = timed.Where(g => g.ScorerPlayerId is not null).Select(g => g.ScorerPlayerId!.Value).ToList();
            var names = await db.Players.Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, ct);
            int h = 0, a = 0;
            var clips = new List<HighlightClip>();
            foreach (var g in timed)
            {
                if (g.ClubId == f.HomeClubId) h++; else a++;
                var who = g.ScorerPlayerId is { } p && names.TryGetValue(p, out var n) ? n : clubs[g.ClubId];
                var score = $"{clubs[f.HomeClubId]} {h}–{a} {clubs[f.AwayClubId]}";
                clips.Add(new HighlightClip(g.VideoOffsetSec!.Value, $"{score} · {g.Minute}' {who} — Bàn thắng"));
            }
            await highlights.RenderAsync(f.Id, outcome.RecordingPath, clips, ct);
            return null;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return e.Message;
        }
    }

    /// <summary>Bài tường thuật + ảnh bàn thắng. Lỗi trả về chuỗi, không làm hỏng kết quả trận.</summary>
    private async Task<string?> PressAsync(Guid fixtureId, string? recordingPath, CancellationToken ct)
    {
        if (press is null) return null;
        try { return await press.PublishAsync(fixtureId, recordingPath, ct); }
        catch (Exception e) when (e is not OperationCanceledException) { return (e.InnerException ?? e).Message; }
    }

    private static Guid Club(Fixture f, MatchSide side) => side == MatchSide.Home ? f.HomeClubId : f.AwayClubId;

    /// <summary>
    /// Thống kê trận (B3): lưu file thô (<see cref="MatchOrchestratorOptions.StatsDir"/>), giải mã khối bộ nhớ, ô → cầu thủ qua bảng điểm (dự phòng: ID PES
    /// trong khối). Không có khối / lỗi / số bàn trong khối đội ≠ tỷ số → null + lý do; không bao giờ làm đá lại trận.
    /// </summary>
    private async Task<(MatchStatsInput? Stats, string? Error)> StatsAsync(ClubsDbContext db, Fixture f, MatchRunOutcome outcome, IReadOnlyList<RatingInput> ratings,
        CancellationToken ct)
    {
        if (outcome.StatsBlocks is not { } blocks) return (null, null);
        try
        {
            if (_o.StatsDir is { } dir)
            {
                Directory.CreateDirectory(dir);
                await File.WriteAllTextAsync(Path.Combine(dir, $"{f.Id:N}.json"), MatchStatsRaw.Of(f.Id, blocks, outcome.Moments ?? [], outcome.Goals).ToJson(), ct);
            }
            var d = MatchStatsDecoder.Decode(blocks, outcome.Moments ?? [], outcome.Goals);
            if (d.Goals != (outcome.HomeGoals, outcome.AwayGoals)) return (null, $"bàn trong thống kê {d.Goals.Home}-{d.Goals.Away} ≠ tỷ số");
            var patch = await (from m in db.Matchdays join s in db.Seasons on m.SeasonId equals s.Id where m.Id == f.MatchdayId select s.PatchVersionId).SingleAsync(ct);
            var pesIds = d.Players.Where(p => p.PesPlayerId is not null).Select(p => p.PesPlayerId!.Value).ToList();
            var byPes = await db.PlayerPesMappings.Where(m => m.PatchVersionId == patch && pesIds.Contains(m.PesPlayerId)).ToDictionaryAsync(m => m.PesPlayerId, m => m.PlayerId, ct);
            Guid? Player(MatchSide side, int? slot) => slot is not { } s ? null
                : ratings.FirstOrDefault(r => r.ClubId == Club(f, side) && r.Slot == s)?.PlayerId
                  ?? (d.Players.FirstOrDefault(p => p.Side == side && p.Slot == s)?.PesPlayerId is { } pid && byPes.TryGetValue(pid, out var g) ? g : null);
            (d.Home.ClubId, d.Away.ClubId) = (f.HomeClubId, f.AwayClubId);
            var events = d.Events.Select(e => new MatchEvent
            {
                ClubId = Club(f, e.Side), Seq = e.Seq, Half = e.Half, Minute = e.Minute, AddedTime = e.AddedTime, Type = e.Type, RawType = e.RawType,
                PlayerId = Player(e.Side, e.Slot), OtherPlayerId = Player(e.Side, e.OtherSlot), OnTarget = e.OnTarget, VideoOffsetSec = e.VideoOffsetSec,
            }).ToList();
            var players = d.Players.Select(p => (P: p, Id: Player(p.Side, p.Slot))).Where(x => x.Id is not null)
                .Select(x => new PlayerStatsInput(Club(f, x.P.Side), x.Id!.Value, x.P.Slot, x.P.Goals, x.P.Shots, x.P.ShotsOnTarget, x.P.Fouls, x.P.Offsides, x.P.Corners,
                    x.P.InMinute, x.P.OutMinute)).ToList();
            return (new MatchStatsInput([d.Home, d.Away], events, players), null);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return (null, e.Message);
        }
    }

    private async Task<MatchRunRequest> RequestAsync(ClubsDbContext db, Guid fixtureId, string container, MatchSide? humanSide, CancellationToken ct)
    {
        var f = (await db.Fixtures.FindAsync([fixtureId], ct))!;
        var md = (await db.Matchdays.FindAsync([f.MatchdayId], ct))!;
        var season = (await db.Seasons.FindAsync([md.SeasonId], ct))!;
        // PO 17/11 bỏ khoá vòng: đội hình lúc vào trận (chuyển nhượng tới lúc này đều có hiệu lực); HLV BOT xếp đội trước.
        if (md.OptionFileBuildId is null) await new BotCoachService(db, clock).ApplyAsync(md.Id, ct);
        Guid buildId;
        if (await new Pes17RoundService(db, store, clock).IsPes17SeasonAsync(season.Id, ct))   // PES17: ảnh chụp đội hình JSON cả liên đoàn
            buildId = (await new Pes17RoundService(db, store, clock).SnapshotAsync(season.PatchVersionId!.Value, md.Id, ct)).Id;   // PO 25/11: không còn đội hình riêng trận (chỉnh trong game)
        else
        {
            var mine = await new RoundOptionFileService(db, store, clock).BuildForFixtureAsync(md.Id, [f.HomeClubId, f.AwayClubId], ct);
            if (!mine.Ok) throw new InvalidOperationException($"Đội hình không hợp lệ: {string.Join("; ", mine.Issues.Select(i => i.Message))}");
            buildId = mine.BuildId;
        }
        var build = (await db.OptionFileBuilds.FindAsync([buildId], ct))!;
        var index = await db.ClubPesMappings.Where(m => m.PatchVersionId == season.PatchVersionId && (m.ClubId == f.HomeClubId || m.ClubId == f.AwayClubId))
            .ToDictionaryAsync(m => m.ClubId, m => m.PesClubIndex, ct);
        var comp = f.CompetitionEditionId is { } ed
            ? await (from e in db.Editions join c in db.Competitions on e.CompetitionId equals c.Id where e.Id == ed select new { c.Id, c.LogoPath, c.Name }).SingleOrDefaultAsync(ct)
            : null;
        return new MatchRunRequest(fixtureId, container, await store.ReadAsync(build.StoragePath!, ct), index[f.HomeClubId], index[f.AwayClubId], humanSide,
            ExtraTime: f.Stage == FixtureStage.Final, CompetitionLogo: comp?.LogoPath, Hot: await HotAsync(db, fixtureId, ct), CompetitionId: comp?.Id, CompetitionName: comp?.Name,
            Conditions: await ConditionsAsync(f.HomeClubId, ct), Coaches: await CoachesAsync(db, f, ct));
    }

    /// <summary>HLV career (có ảnh mặt) của 2 CLB trong trận; CLB máy không có HLV career → bỏ.</summary>
    private static async Task<List<RunCoach>> CoachesAsync(ClubsDbContext db, Fixture f, CancellationToken ct)
    {
        var by = await CoachHooks.ByClubAsync(db, ct);
        var list = new List<RunCoach>();
        foreach (var (side, club) in new[] { (MatchSide.Home, f.HomeClubId), (MatchSide.Away, f.AwayClubId) })
            if (by.GetValueOrDefault(club) is { Face: { Length: > 0 } face } c)
            {
                var clubName = await db.Clubs.Where(x => x.Id == club).Select(x => x.Name).SingleAsync(ct);
                list.Add(new RunCoach(side, c.Nickname is { Length: > 0 } n ? n : c.Name, clubName, face));
            }
        return list;
    }

    /// <summary>Sân/thời tiết là phụ: lỗi (mạng, dịch vụ thời tiết) → trận vẫn đá với menu như cũ.</summary>
    private async Task<string?> ConditionsAsync(Guid homeClubId, CancellationToken ct)
    {
        if (_o.Conditions is null) return null;
        try { return await _o.Conditions(homeClubId, ct); }
        catch (Exception e) when (e is not OperationCanceledException) { return null; }
    }

    /// <summary>Trận cầu đinh? Lỗi tính BXH/sức mạnh thì coi như không (chỉ ảnh hưởng ghi hình).</summary>
    private async Task<bool> HotAsync(ClubsDbContext db, Guid fixtureId, CancellationToken ct)
    {
        try { return await new HotMatchService(db, store, clock).IsHotAsync(fixtureId, ct); }
        catch (Exception e) when (e is not OperationCanceledException) { return false; }
    }

    private sealed record ShirtRow(Guid ClubId, int Shirt, Guid PlayerId, string Name);

    private static Task<List<ShirtRow>> ShirtsAsync(ClubsDbContext db, Fixture f, CancellationToken ct) =>
        (from m in db.SquadMemberships
         join p in db.Players on m.PlayerId equals p.Id
         where (m.ClubId == f.HomeClubId || m.ClubId == f.AwayClubId) && m.LeftAt == null
         select new ShirtRow(m.ClubId, m.ShirtNumber, m.PlayerId, p.Name)).ToListAsync(ct);

    /// <summary>ID PES → cầu thủ theo patch của mùa; số áo (chụp màn) → cầu thủ đang ở CLB. Không ghép được thì bỏ dòng đó.</summary>
    private static async Task<List<RatingInput>> RatingsAsync(ClubsDbContext db, Fixture f, IReadOnlyList<RunRating> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return [];
        var patch = await (from m in db.Matchdays join s in db.Seasons on m.SeasonId equals s.Id where m.Id == f.MatchdayId select s.PatchVersionId).SingleAsync(ct);
        var pesIds = rows.Where(r => r.PesPlayerId is not null).Select(r => r.PesPlayerId!.Value).ToList();
        var byPes = await db.PlayerPesMappings.Where(m => m.PatchVersionId == patch && pesIds.Contains(m.PesPlayerId))
            .ToDictionaryAsync(m => m.PesPlayerId, m => m.PlayerId, ct);
        var byShirt = await db.SquadMemberships.Where(m => (m.ClubId == f.HomeClubId || m.ClubId == f.AwayClubId) && m.LeftAt == null)
            .ToListAsync(ct);
        var list = new List<RatingInput>();
        foreach (var r in rows)
        {
            var club = r.Side == MatchSide.Home ? f.HomeClubId : f.AwayClubId;
            var player = r.PesPlayerId is { } pid ? (byPes.TryGetValue(pid, out var g) ? g : (Guid?)null)
                : byShirt.FirstOrDefault(m => m.ClubId == club && m.ShirtNumber == r.Shirt)?.PlayerId;
            if (player is { } p) list.Add(new RatingInput(club, p, r.Slot, r.Rating));
        }
        return list;
    }

    /// <summary>Trận chờ HLV người tự đá: có bên Human và chưa giao trợ lý. PO 17/11: người vs người cũng chờ 2 HLV đá trên máy 2 người
    /// (trước đây = COM vs COM); quá hạn vòng thì trợ lý đá thay (<see cref="AbsenceService"/>).</summary>
    public static bool NeedsHuman(Fixture f, IReadOnlySet<Guid> humans) =>
        !f.AssistantPlays && (humans.Contains(f.HomeClubId) || humans.Contains(f.AwayClubId));

    public static bool BothHuman(Fixture f, IReadOnlySet<Guid> humans) => humans.Contains(f.HomeClubId) && humans.Contains(f.AwayClubId);

    public static async Task<HashSet<Guid>> HumanClubsAsync(ClubsDbContext db, IEnumerable<Guid> clubs, CancellationToken ct)
    {
        var ids = clubs.Distinct().ToList();
        return (await db.TeamAssignments.Where(a => ids.Contains(a.ClubId) && a.To == null && a.ControllerType == ControllerType.Human)
            .Select(a => a.ClubId).ToListAsync(ct)).ToHashSet();
    }
}

/// <summary>Tên trên dải ghi bàn ("K.Rodriguez", OCR không dấu) khớp tên cầu thủ ("Kevin Rodríguez"): họ (phần sau dấu chấm/cách cuối) có trong tên.</summary>
public static class ScorerName
{
    public static bool Matches(string? banner, string name)
    {
        if (string.IsNullOrWhiteSpace(banner)) return true;   // chỉ có số áo: tin số áo
        var last = Norm(banner.Split('.', ' ', StringSplitOptions.RemoveEmptyEntries)[^1]);
        return last.Length >= 3 && Norm(name).Contains(last[..Math.Min(last.Length, 6)]);
    }

    private static string Norm(string s)
    {
        var d = s.Normalize(System.Text.NormalizationForm.FormD);
        return new string(d.Where(c => char.IsLetter(c) && System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
            .Select(char.ToLowerInvariant).ToArray()).Replace("đ", "d");
    }
}
