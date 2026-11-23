using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Application;
using Pes17League.Clubs.Domain;
using Pes17League.Clubs.Infrastructure;

namespace Pes17League.Web;

/// <param name="RecordingDir">Bản ghi hình cả trận (giữ 7 ngày); <paramref name="HighlightDir"/>: highlight theo trận (C7). null = không ghi hình.</param>
/// <param name="MinFreeDiskGb">Đá BOT: ổ ghi hình còn dưới mức này thì dừng trước trận kế (<see cref="MatchOrchestratorOptions.FreeDiskBytes"/>).</param>
/// <param name="StatsDir">File thô thống kê trận (B3, <see cref="MatchOrchestratorOptions.StatsDir"/>).</param>
/// <param name="LiveDir">Hình xem trực tiếp (<see cref="LiveStore"/>); cần có ghi hình.</param>
/// <param name="LogoDir">Logo tải lên (<see cref="LogoStore"/>): logo giải của trận hiện trên màn hình chơi (PO 22/11).</param>
/// <param name="GraphicsDir">Đồ hoạ trong game (<see cref="GraphicsStore"/>, PO 23/11): bộ chạy cài gói menu của giải trước trận.</param>
/// <param name="LockFile">Khoá "đang đá" match.lock: web xoá hộ khi dừng bộ chạy ngang (<see cref="ScriptMatchRunner"/>, PO 22/11).</param>
public sealed record MatchJobOptions(string Script, string WorkDir, string StreamUrl, string? RecordingDir = null, string? HighlightDir = null, int MinFreeDiskGb = 20,
    string? StatsDir = null, string? LiveDir = null, string? LogoDir = null, string? LockFile = null, string? GraphicsDir = null);

/// <summary>Ảnh bài báo: {Dir}/{fixture:N}/{n}.jpg, web phục vụ ở <see cref="RequestPath"/>.</summary>
public sealed record NewsStore(string Dir)
{
    public const string RequestPath = "/tin-anh";
    /// <summary>Ảnh bàn đầu của trận (nếu có bài tin), dùng làm thumbnail dự phòng khi chưa có poster highlight.</summary>
    public string? ThumbUrl(Guid fixtureId)
    {
        var rel = $"{fixtureId:N}/1.jpg";
        return File.Exists(Path.Combine(Dir, rel)) ? $"{RequestPath}/{rel}" : null;
    }
}

/// <summary>Highlight đã cắt: {Dir}/{fixture:N}.mp4 (16:9), {fixture:N}-doc.mp4 (9:16), {fixture:N}.jpg (thumbnail); web: /highlight/{id}, /doc, /anh.</summary>
public sealed record HighlightStore(string Dir)
{
    public string PathFor(Guid fixtureId, bool vertical = false) => ScriptHighlightRenderer.PathFor(Dir, fixtureId, vertical);
    public string PosterPathFor(Guid fixtureId) => ScriptHighlightRenderer.PosterPathFor(Dir, fixtureId);
    public bool Has(Guid fixtureId, bool vertical = false) => File.Exists(PathFor(fixtureId, vertical));
    public bool HasPoster(Guid fixtureId) => File.Exists(PosterPathFor(fixtureId));
    /// <summary>URL ảnh thumbnail nếu đã có poster; null → trang tự dùng ảnh tin hoặc để trống.</summary>
    public string? PosterUrl(Guid fixtureId) => HasPoster(fixtureId) ? $"/highlight/{fixtureId}/anh" : null;
}

/// <summary>Xem trực tiếp (PO 07/11): pesmatch.py ghi thêm HLS {Dir}/{fixture:N}.m3u8 + {fixture:N}-00000.ts… (đoạn 2 s, giữ ~12 s) từ cùng bản mã hoá
/// ghi hình, chỉ giữ trận đang ghi; web phục vụ ở <see cref="RequestPath"/>/{file}.</summary>
public sealed record LiveStore(string Dir)
{
    public const string RequestPath = "/truc-tiep/hls";
    private static readonly System.Text.RegularExpressions.Regex FileName = new(@"^[0-9a-f]{32}(\.m3u8|-\d{5,}\.ts)\z");

    public static string PlaylistUrl(Guid fixtureId) => $"{RequestPath}/{fixtureId:N}.m3u8";
    /// <summary>Tên playlist/đoạn hợp lệ → đường dẫn; tên khác (../, file .tmp đang ghi) → null.</summary>
    public string? PathFor(string file) => FileName.IsMatch(file) ? Path.Combine(Dir, file) : null;
}

/// <summary>
/// Việc chạy nền của web (đá trận BOT, trận của PO, đóng kỳ CN): mỗi lúc 1 việc vì chỉ có 1 container trận (pes17-bench).
/// Nhật ký sống từ sự kiện JSON của pesmatch.py; trang đăng ký <see cref="Changed"/> để vẽ lại.
/// </summary>
public sealed class MatchJobService(IDbContextFactory<ClubsDbContext> dbs, IOptionFileStore store, TimeProvider clock, MatchJobOptions o, ILogger<MatchJobService> log,
    IArticleWriter articles, IFrameGrabber frames, NewsStore news, StreamPass pass)
{
    private readonly object _gate = new();
    private readonly List<string> _log = [];
    private CancellationTokenSource? _cts;
    private Dictionary<Guid, string> _names = [];
    private TaskCompletionSource? _go;
    private SquadSync? _sync;
    private CancellationTokenSource? _stop;
    private Guid? _declined;
    private Guid? _botMatchday;   // vòng của việc "Đá các trận BOT" đang chạy (để đá tiếp sau trận admin chen ngang)

    /// <summary>Chuẩn bị sẵn mà quá hạn này chưa bấm Đá → huỷ, trả máy (PO 05/11: 30 phút).</summary>
    public static readonly TimeSpan PrepareTimeout = TimeSpan.FromMinutes(30);

    public MatchJobOptions Options => o;
    /// <summary>Link stream cho PO ở trận của mình: máy chế độ token (18/11) → link có vé riêng trận (P1, ô 1); máy chưa bật token → <see cref="MatchJobOptions.StreamUrl"/>.</summary>
    public string PlayLink => HumanFixture is { } h && pass.Link(h.FixtureId, true) is { } l ? l : o.StreamUrl;
    public string? Title { get; private set; }
    public bool Running { get; private set; }
    /// <summary>Bước vào trận của việc đang chạy: boot / menu / teams / ready (chuẩn bị sẵn) / loading (pesmatch.py "stage") → kickoff (màn chờ /vao-tran).</summary>
    public string? Stage { get; private set; }
    /// <summary>Trận của PO gần nhất (trang kết quả /vao-tran sau khi hết giờ).</summary>
    public (Guid FixtureId, MatchSide Side)? HumanFixture { get; private set; }
    public event Action? Changed;
    /// <summary>Lúc vào bước hiện tại (<see cref="Stage"/>).</summary>
    public DateTimeOffset StageAt { get; private set; }
    /// <summary>Trận của bước hiện tại (sự kiện pesmatch.py gần nhất); null = việc chưa vào trận nào.</summary>
    public Guid? CurrentFixtureId { get; private set; }
    /// <summary>Trận đã bắt đầu ghi hình trực tiếp (sự kiện "live", <see cref="LiveStore"/>); lần chạy mới ("boot") → null tới khi ghi lại.</summary>
    public Guid? LiveFixtureId { get; private set; }

    // Giây còn lại tới giao bóng tính từ lúc vào mỗi bước. Mặc định đo 05/11 (4 trận BOT: boot→menu ~28 s, menu→chọn đội ~50,
    // chọn đội→tải ~35–50, tải→giao bóng ~21); sau mỗi trận PO vào được tới giao bóng thì chỉnh dần theo thực đo (chỉ trong bộ nhớ).
    // "warm" (vào trận nhanh, PO 16/11): tính từ lúc bấm Sẵn sàng; thử pestest-1 16/11: go → chọn xong 2 đội 39 s (ghi bộ nhớ 2 s).
    private static readonly Dictionary<string, double> Eta = new() { [""] = 245, ["boot"] = 240, ["menu"] = 235, ["warm"] = 220, ["teams"] = 215, ["ready"] = 150, ["loading"] = 150 };   // đo PES17 19/11 (1 trận BOT), tự học thêm sau mỗi trận
    private readonly Dictionary<string, DateTimeOffset> _stageTimes = [];

    /// <summary>Ước lượng giây từ lúc bấm Sẵn sàng tới giao bóng.</summary>
    public int SecondsToKickoffEstimate { get { lock (_gate) return (int)Math.Round(Eta[""]); } }
    /// <summary>Ước lượng giây từ lúc bấm Sẵn sàng tới giao bóng khi game đã dựng sẵn ("warm").</summary>
    public int SecondsToKickoffEstimateWarm { get { lock (_gate) return (int)Math.Round(Eta["warm"]); } }

    /// <summary>Đếm ngược vào trận PO (màn chờ /vao-tran): giây ước lượng tới giao bóng; null = không trong lúc vào trận.</summary>
    public int? SecondsToKickoff
    {
        get
        {
            if (!Running || Title != "Trận của tôi" || Ready || Stage is "kickoff" or "fulltime") return null;
            lock (_gate) return (int)Math.Max(0, Math.Ceiling(Eta.GetValueOrDefault(Stage ?? "", 21) - (clock.GetUtcNow() - StageAt).TotalSeconds));
        }
    }
    /// <summary>Trận PO đang chuẩn bị sẵn (game đi menu / chờ ở màn Thiết lập), chưa bấm Đá.</summary>
    public bool Preparing => Running && _go is { Task.IsCompleted: false };
    /// <summary>Lúc tự huỷ dựng sẵn (bắt đầu chuẩn bị + <see cref="PrepareTimeout"/>); chỉ có nghĩa khi <see cref="Preparing"/>.</summary>
    public DateTimeOffset? PrepareUntil { get; private set; }
    /// <summary>Game đã dựng sẵn, chờ ở màn Chọn đội ("warm"): bấm Sẵn sàng → ghi đội hình vào bộ nhớ, ~95 s tới giao bóng.</summary>
    public bool Ready => Preparing && Stage is "warm" or "ready";
    /// <summary>Đội hình đã chốt: đã bấm Sẵn sàng (hoặc vào thẳng không chuẩn bị) → khoá sửa đội hình trên web.
    /// Lúc game đang dựng sẵn (PO 16/11) vẫn sửa được: bấm Sẵn sàng mới dựng OF và ghi vào game.</summary>
    public bool LineupLocked => Running && Title == "Trận của tôi" && !Preparing;
    /// <summary>Trận PO đã huỷ chuẩn bị / quá hạn / lỗi lúc chuẩn bị → trang vòng không tự chuẩn bị lại (tránh lặp).</summary>
    public bool Declined(Guid fixtureId) => _declined == fixtureId;

    public IReadOnlyList<string> Log { get { lock (_gate) return [.. _log]; } }

    /// <summary>Đang đá BOT, chưa xin dừng → hiện nút "Dừng sau trận này".</summary>
    public bool CanStopAfterCurrent => Running && _stop is { IsCancellationRequested: false };
    /// <summary>Đã xin dừng, trận BOT đang đá chưa xong.</summary>
    public bool StopPending => Running && _stop is { IsCancellationRequested: true };

    // Đo PES17 19/11 (1 trận BOT, Match Time 5): giao bóng → hết giờ 13,8 phút (game chạy chậm hơn thời gian thực); hết giờ → xong ~0,2 phút.
    private const double BotInPlayMinutes = 14, BotAfterMinutes = 0.5;
    /// <summary>Trận có người (Match Time 10 = 5 phút/hiệp): giao bóng → hết giờ, ƯỚC LƯỢNG (chưa đo; 10 phút đá + nghỉ giữa hiệp + quay chậm).</summary>
    public const double HumanInPlayMinutes = 16;

    /// <summary>Đá BOT: ước lượng phút tới khi trận đang đá xong; null = không đá BOT.</summary>
    public int? BotMinutesLeft => Title == "Đá các trận BOT" ? MatchMinutesLeft : null;

    /// <summary>Trận đang đá trên máy game (BOT hoặc trận của HLV): ước lượng phút tới khi trận xong (trang Máy chơi, PO 22/11);
    /// null = không có trận / trận của HLV đang chuẩn bị sẵn, chờ bấm Đá (tự huỷ lúc <see cref="PrepareUntil"/>).</summary>
    public int? MatchMinutesLeft
    {
        get
        {
            if (!Running || Title is not ("Đá các trận BOT" or "Trận của tôi") || Preparing) return null;
            var inPlay = Title == "Trận của tôi" ? HumanInPlayMinutes : BotInPlayMinutes;
            lock (_gate)
            {
                var since = (clock.GetUtcNow() - StageAt).TotalMinutes;
                var left = Stage switch
                {
                    "fulltime" => BotAfterMinutes - since,
                    "kickoff" => inPlay - since + BotAfterMinutes,
                    _ => Math.Max(0, Eta.GetValueOrDefault(Stage ?? "", 21) / 60 - since) + inPlay + BotAfterMinutes,
                };
                return (int)Math.Max(1, Math.Ceiling(left));
            }
        }
    }

    // Đo PES17 19/11 (1 trận BOT, Match Time 5): khởi động → xong 1075 s ≈ 18 phút/trận (khởi động 4 phút + đá 13,8 phút).
    public const double BotMatchMinutes = 18;

    /// <summary>Đá BOT: ước lượng phút tới khi xong trận đang đá và <paramref name="queued"/> trận BOT chưa đá; null = không đá BOT.</summary>
    public int? BotQueueMinutesLeft(int queued) => BotMinutesLeft is { } m ? m + (int)Math.Ceiling(queued * BotMatchMinutes) : null;

    public bool PlayBots(Guid matchdayId)
    {
        var stop = new CancellationTokenSource();
        return Start("Đá các trận BOT", async ct =>
        {
            var orch = Orchestrator();
            foreach (var id in await orch.ReleaseStaleAttemptsAsync(matchdayId, ct)) Write($"{Name(id)}: lần chạy trước bị ngắt giữa trận (web khởi động lại?) → đá lại");
            var r = await orch.PlayBotFixturesAsync(matchdayId, ct, stop.Token);
            foreach (var f in r.Fixtures)
            {
                Write($"{Name(f.FixtureId)}: {(f.Played ? "xong" : $"CHƯA ĐÁ ({f.LastError})")} sau {f.Attempts} lần");
                if (f.HighlightError is not null) Write($"  Highlight lỗi: {f.HighlightError}");
                if (f.StatsError is not null) Write($"  Thống kê lỗi: {f.StatsError}");
                if (f.PressError is not null) Write($"  Bài tường thuật lỗi: {f.PressError}");
            }
            if (r.LowDiskFreeBytes is { } free)
                Write($"DỪNG: ổ ghi hình còn {free / (double)(1L << 30):0.#} GB (dưới {o.MinFreeDiskGb} GB), các trận chưa đá giữ nguyên lịch. Dọn bớt ổ rồi bấm lại \"Đá các trận BOT\".");
            if (r.Stopped) Write("ĐÃ DỪNG theo yêu cầu, các trận BOT chưa đá giữ nguyên lịch. Bấm lại \"Đá các trận BOT\" để đá tiếp.");
            if (r.WaitingForHuman.Count > 0) Write($"Chờ PO: {string.Join(", ", r.WaitingForHuman.Select(Name))}");
            if (r.Settlement is not null) Write("Vòng đã chốt.");
        }, () => { _stop = stop; _botMatchday = matchdayId; });
    }

    public const string SimTitle = "Mô phỏng các trận";

    public const string RolloverTitle = "Sang mùa mới";
    public bool NextSeason() => Start(RolloverTitle, async ct =>
    {
        await using var db = await dbs.CreateDbContextAsync(ct);
        var r = await new SeasonRolloverService(db, store, clock).NextSeasonAsync(Random.Shared.Next(1, 10_000), "career", Write, ct);
        Write($"Xong: {r.Season} — giải nghệ {r.Retired}, BOT ký thay {r.Replaced}, {r.Start.Slots} khung / {r.Start.Matches} trận.");
    });

    /// <summary>
    /// Career (PO 19/11, 20/11 "đá trước, tự mô phỏng sau"): mô phỏng khung này sang khung khác, dừng ở khung có trận kế tiếp của người chơi
    /// (trước khi mô phỏng trận khác của khung đó) / hết mùa — <see cref="Pes17CareerService.ContinueAsync"/>.
    /// <paramref name="mineNow"/> = mô phỏng luôn trận của người chơi ở khung đang mở rồi chạy tiếp; <paramref name="includeMine"/> + <paramref name="maxSlots"/>
    /// = "Chờ 1 vòng" khi chưa có CLB.
    /// </summary>
    public bool SimulateCareer(bool mineNow = false, bool includeMine = false, int maxSlots = 500) => Start(SimTitle, async ct =>
    {
        await using var db = await dbs.CreateDbContextAsync(ct);
        var career = new Pes17CareerService(db, store, clock);
        var r = await career.ContinueAsync(maxSlots, Write, includeMine, mineNow, ct, HotLive ? PlayHotAsync : null);
        foreach (var e in r.Errors.Take(5)) Write($"LỖI {e}");
        Write($"Xong: mô phỏng {r.Simulated} trận" + (r.SeasonFinished ? " — mùa giải đã kết thúc." : "."));
        if (r.MyFixtureId is { } f) Write($"Tới trận của bạn: {Name(f)}");
    });

    /// <summary>
    /// Chế độ ghi hình (file record-mode cạnh match.lock, tools/match17/tools/rec17.py): "hot" (PO 23/11) = chỉ ghi trận cầu đinh;
    /// "live" = như hot + mỗi khung career đá thật 1 trận cầu đinh BOT trên máy game để phát ở trang Trực tiếp (người bấm Tiếp tục chờ ~15 phút/trận).
    /// Đọc mỗi lần dùng: đổi file là có hiệu lực, không cần khởi động lại web.
    /// </summary>
    public string RecordMode
    {
        get
        {
            if (o.LockFile is null || o.RecordingDir is null) return "off";
            try { return File.ReadAllText(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(o.LockFile))!, "record-mode")).Trim().ToLowerInvariant() is ("bot" or "all" or "hot" or "live") and var m ? m : "off"; }
            catch (IOException) { return "off"; }
        }
    }

    public bool HotLive => RecordMode == "live";

    /// <summary>Trận cầu đinh BOT trong lúc "Tiếp tục" career: đá thật (COM–COM), ghi hình + HLS; không ghi được kết quả thì career mô phỏng trận đó.</summary>
    private async Task PlayHotAsync(HotFixture h, CancellationToken ct)
    {
        _names[h.FixtureId] = $"{h.HomeName} – {h.AwayName}";
        var r = await Orchestrator().PlayFixtureAsync(h.FixtureId, null, ct: ct);
        Write(r.Played ? $"Trận cầu đinh đã ghi kết quả: {h.HomeName} – {h.AwayName}." : $"Trận cầu đinh chưa ghi được kết quả ({r.LastError}) — mô phỏng thay.");
    }

    /// <summary>Đang đá BOT → admin được chen ngang (<see cref="PlayHumanNowAsync"/>).</summary>
    public bool CanPreemptBots => Running && Title == "Đá các trận BOT";

    /// <summary>PO 15/11 (chọn A, chỉ admin): bấm Sẵn sàng lúc BOT đang đá → ngắt ngay trận BOT (lượt Aborted, trận về lịch, đá lại sau),
    /// vào trận của PO, đá xong tự đá tiếp các trận BOT của vòng đó. Admin bấm Huỷ trận của PO thì không tự đá tiếp.</summary>
    public async Task<bool> PlayHumanNowAsync(Guid fixtureId, MatchSide side)
    {
        Guid? resume = null;
        if (CanPreemptBots)
        {
            resume = _botMatchday;
            Write("Admin vào trận: ngắt trận BOT đang đá (trận đó về lịch, đá lại sau).");
            Cancel();
            for (var i = 0; i < 240 && Running; i++) await Task.Delay(250);   // chờ script BOT thoát (tối đa 60s)
            if (Running) return false;
        }
        CancellationTokenSource? mine = null;
        var ok = PlayHuman(fixtureId, side, then: resume is { } md ? () => { if (mine is { IsCancellationRequested: false }) PlayBots(md); } : null);
        if (ok) mine = _cts;
        return ok;
    }

    /// <summary>Đá BOT: xong trận đang đá thì dừng, nhường máy cho trận của PO (khác Huỷ: không bỏ ngang trận). Trận chưa đá giữ lịch.</summary>
    public void StopAfterCurrent()
    {
        if (!CanStopAfterCurrent) return;
        _stop!.Cancel();
        Write("Sẽ dừng sau trận đang đá (nhường máy cho trận của tôi).");
    }

    /// <param name="prepare">true: chuẩn bị sẵn tới màn Thiết lập, chờ <see cref="Go"/> (tối đa <see cref="PrepareTimeout"/>).</param>
    /// <param name="career">Career (PO 20/11): trận ghi được kết quả → tự mô phỏng phần còn lại của khung tới trận kế tiếp (<see cref="SimulateCareer"/>).</param>
    public bool PlayHuman(Guid fixtureId, MatchSide side, bool prepare = false, Action? then = null, bool career = false)
    {
        var go = prepare ? new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) : null;
        var sync = prepare ? new SquadSync() : null;
        var played = false;
        return Start("Trận của tôi", async ct =>
        {
            HumanFixture = (fixtureId, side);
            if (go is not null)
            {
                _ = ExpireAsync(go.Task, ct);
                Write($"Đang chuẩn bị trận — game tự đi menu và chờ sẵn, bấm Đá là vào (tự huỷ sau {PrepareTimeout.TotalMinutes:0} phút).");
            }
            if (pass.Enabled)
                try { await pass.OpenAsync(fixtureId, ct); }
                catch (Exception e) when (e is not OperationCanceledException) { Write($"Cấp vé stream lỗi: {e.Message}"); }
            if (go is null) Write($"Mở stream — game đang tự vào trận, PO điều khiển đội {(side == MatchSide.Home ? "nhà" : "khách")}.");
            try
            {
                var r = await Orchestrator().PlayFixtureAsync(fixtureId, side, ct: ct, go: go?.Task, sync: sync);
                Write(r.Played ? "Trận đã ghi kết quả." : $"Chưa ghi kết quả: {r.LastError} (sau {r.Attempts} lần)");
                played = r.Played;
                if (r.HighlightError is not null) Write($"Highlight lỗi: {r.HighlightError}");
                if (r.StatsError is not null) Write($"Thống kê lỗi: {r.StatsError}");
                if (r.PressError is not null) Write($"Bài tường thuật lỗi: {r.PressError}");
            }
            finally
            {
                if (go is { Task.IsCompleted: false }) _declined = fixtureId;
                if (pass.Enabled) await pass.CloseAsync(fixtureId);
            }
        }, () => { _go = go; _sync = sync; _declined = null; }, () => { then?.Invoke(); if (career && played) SimulateCareer(); });
    }

    /// <summary>HLV vừa lưu đội hình/chiến thuật (LineupBoard): đang dựng sẵn chờ Sẵn sàng → game cài lại đội hình ngay (PES17, PO 21/11).</summary>
    public void SquadChanged()
    {
        if (Preparing) _sync?.Changed();
    }

    /// <summary>Bấm Đá khi đã chuẩn bị: game vào trận ngay (chưa tới màn Thiết lập thì đi tiếp vào thẳng).</summary>
    public void Go()
    {
        lock (_gate)
            if (_go is { Task.IsCompleted: false } && Stage == "warm")   // đếm ngược + học ETA "warm" tính từ lúc bấm (bỏ thời gian chờ)
            {
                StageAt = clock.GetUtcNow();
                _stageTimes.Clear();
                _stageTimes["warm"] = StageAt;
            }
        _go?.TrySetResult();
        Changed?.Invoke();
    }

    private async Task ExpireAsync(Task go, CancellationToken ct)
    {
        PrepareUntil = clock.GetUtcNow() + PrepareTimeout;
        try { await Task.WhenAny(go, Task.Delay(PrepareTimeout, clock, ct)); } catch (OperationCanceledException) { return; }
        if (go.IsCompleted || ct.IsCancellationRequested) return;
        Write($"Quá {PrepareTimeout.TotalMinutes:0} phút chưa bấm Đá — huỷ chuẩn bị, trả máy.");
        Cancel();
    }

    /// <summary>Trận kẹt "Đang đá" vì web khởi động lại giữa trận (kể cả trận của PO) → về lịch, đá lại bằng nút như thường.</summary>
    public bool ReleaseStale(Guid matchdayId) => Start("Đặt lại trận kẹt", async ct =>
    {
        var ids = await Orchestrator().ReleaseStaleAttemptsAsync(matchdayId, ct);
        Write(ids.Count == 0 ? "Không có trận nào kẹt." : $"Đã đặt lại {ids.Count} trận: {string.Join(", ", ids.Select(Name))}.");
    });

    /// <summary>Khoá vòng (đóng kỳ CN nếu có); khoá được thì các trận BOT tự đá lần lượt (PO 07/11: BOT không chờ trận của PO,
    /// PO nhường máy bằng "Dừng sau trận này").</summary>
    public bool CloseWindow(Guid matchdayId)
    {
        var locked = false;
        return Start("Đóng kỳ chuyển nhượng", async ct =>
        {
            await using var db = await dbs.CreateDbContextAsync(ct);
            var r = await new CompetitionService(db, store, clock).CloseTransferWindowAsync(matchdayId, ct);
            if (r.Ok) Write("Đã khoá vòng, chốt đội hình cho vòng.");
            else Write($"Lỗi: {r.Error} {string.Join("; ", r.Issues?.Select(i => i.ToString()) ?? [])}");
            locked = r.Ok;
        }, then: () => { if (locked) PlayBots(matchdayId); });
    }

    public void Cancel() => _cts?.Cancel();

    private MatchOrchestrator Orchestrator() =>
        new(() => dbs.CreateDbContext(), store, clock, new ScriptMatchRunner(o.Script, o.WorkDir, OnEvent, o.RecordingDir, o.LiveDir, logoDir: o.LogoDir, lockFile: o.LockFile, graphicsDir: o.GraphicsDir),
            new MatchOrchestratorOptions { FreeDiskBytes = o.RecordingDir is { } rec ? () => FreeBytes(rec) : null, MinFreeDiskBytes = (long)o.MinFreeDiskGb << 30, StatsDir = o.StatsDir },
            null,   // PO 12/11: bỏ highlight từng trận; bàn thắng cắt từ bản ghi trong RoundReelService (đoạn từng bàn + clip vòng)
            new MatchPress(() => dbs.CreateDbContext(), articles, frames, news.Dir, clock));

    /// <summary>Trận giải người vs người trên máy 2 người (FriendlyWorker, PO 17/11): ghi hình + bài báo như trận giải, không phát trực tiếp,
    /// chỉ dùng <paramref name="container"/> (lượt Running sót chỉ dọn trên máy đó).</summary>
    public MatchOrchestrator DuelOrchestrator(string container, Action<MatchRunRequest, JsonElement> onEvent) =>
        new(() => dbs.CreateDbContext(), store, clock, new ScriptMatchRunner(o.Script, o.WorkDir, onEvent, o.RecordingDir, logoDir: o.LogoDir, lockFile: o.LockFile, graphicsDir: o.GraphicsDir),
            new MatchOrchestratorOptions { Containers = [container], FreeDiskBytes = o.RecordingDir is { } rec ? () => FreeBytes(rec) : null,
                MinFreeDiskBytes = (long)o.MinFreeDiskGb << 30, StatsDir = o.StatsDir },
            null, new MatchPress(() => dbs.CreateDbContext(), articles, frames, news.Dir, clock));

    /// <summary>Số byte trống của ổ chứa <paramref name="dir"/> (thư mục chưa tạo → thư mục cha gần nhất).</summary>
    private static long FreeBytes(string dir)
    {
        var d = new DirectoryInfo(Path.GetFullPath(dir));
        while (!d.Exists && d.Parent is { } parent) d = parent;
        return new DriveInfo(d.FullName).AvailableFreeSpace;
    }

    /// <param name="then">Chạy sau khi việc kết thúc (đã nhả máy), vd khoá vòng xong thì đá BOT.</param>
    private bool Start(string title, Func<CancellationToken, Task> work, Action? init = null, Action? then = null)
    {
        lock (_gate)
        {
            if (Running) return false;
            Running = true;
            Title = title;
            Stage = null;
            CurrentFixtureId = null;
            LiveFixtureId = null;
            _log.Clear();
            _cts = new CancellationTokenSource();
            _go = null;
            _sync = null;
            _stop = null;
            StageAt = clock.GetUtcNow();
            _stageTimes.Clear();
            _stageTimes[""] = StageAt;
            init?.Invoke();
        }
        var ct = _cts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                _names = await FixtureNamesAsync();
                Write($"Bắt đầu: {title}");
                await work(ct);
            }
            catch (OperationCanceledException) { Write("Đã huỷ."); }
            catch (Exception e) { log.LogError(e, "Job {Title}", title); Write($"Lỗi: {e.Message}"); }
            finally
            {
                lock (_gate) Running = false;
                Write("Kết thúc.");
            }
            then?.Invoke();
        });
        Changed?.Invoke();
        return true;
    }

    private void OnEvent(MatchRunRequest r, JsonElement e)
    {
        var name = Name(r.FixtureId);
        string S(string p) => e.TryGetProperty(p, out var v) ? v.ToString() : "?";
        if (S("event") is "stage" or "kickoff" or "fulltime")
        {
            var now = clock.GetUtcNow();
            lock (_gate)
            {
                Stage = S("event") == "stage" ? S("stage") : S("event");
                StageAt = now;
                CurrentFixtureId = r.FixtureId;
                if (Stage == "boot") LiveFixtureId = null;
                if (Stage == "boot" && _stageTimes.ContainsKey("boot")) _stageTimes.Clear();   // thử lại → lần này không dùng để chỉnh ước lượng
                if (Stage == "kickoff" && r.HumanSide is not null && _stageTimes.Count > 0 && !_stageTimes.ContainsKey("ready"))
                    foreach (var (st, at) in _stageTimes) Eta[st] = (Eta.GetValueOrDefault(st, 21) + (now - at).TotalSeconds) / 2;
                _stageTimes.TryAdd(Stage, now);
            }
            Changed?.Invoke();
        }
        if (S("event") == "live")
        {
            lock (_gate) LiveFixtureId = r.FixtureId;
            Changed?.Invoke();
        }
        var text = S("event") switch
        {
            "kickoff" => $"{name}: bắt đầu",
            "goal" => $"{name}: BÀN THẮNG đội {(S("side") == "home" ? "nhà" : "khách")} phút {S("minute")}",
            "fulltime" => $"{name}: hết giờ {S("home")}-{S("away")}, đang đọc thống kê + điểm cầu thủ",
            "stats_error" => $"{name}: không đọc được thống kê ({S("reason")})",
            "penalties" => $"{name}: loạt penalty {S("home")}-{S("away")}",
            "penalties_error" => $"{name}: không đọc được loạt penalty ({S("reason")}), admin nhập sau",
            "end" => $"{name}: hết trận {S("home")}-{S("away")}",
            "error" => $"{name}: lỗi {S("reason")}",
            "nav_retry" => $"{name}: vào trận lại lần {S("attempt")} ({S("reason")})",
            "audio_error" => $"{name}: ghi hình không có tiếng ({S("reason")})",
            _ => null,
        };
        if (text is not null) Write(text);
    }

    private void Write(string line)
    {
        lock (_gate) _log.Add($"{DateTime.Now:HH:mm:ss} {line}");
        Changed?.Invoke();
    }

    private string Name(Guid fixtureId) => _names.GetValueOrDefault(fixtureId, fixtureId.ToString()[..8]);

    private async Task<Dictionary<Guid, string>> FixtureNamesAsync()
    {
        await using var db = await dbs.CreateDbContextAsync();
        return await (from f in db.Fixtures
                      join h in db.Clubs on f.HomeClubId equals h.Id
                      join a in db.Clubs on f.AwayClubId equals a.Id
                      select new { f.Id, Text = h.Name + " – " + a.Name }).ToDictionaryAsync(x => x.Id, x => x.Text);
    }
}
