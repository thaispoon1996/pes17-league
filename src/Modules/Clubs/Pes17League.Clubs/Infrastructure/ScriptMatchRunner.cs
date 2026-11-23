using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using Pes17League.Clubs.Application;

namespace Pes17League.Clubs.Infrastructure;

/// <summary>
/// Chạy trận bằng tools/match/pesmatch.py (host có docker):
/// <c>pesmatch.py run CONTAINER OF_PATH HOME_IDX AWAY_IDX [home|away]</c> — nạp OF, vào trận, đá hết, in sự kiện JSON mỗi dòng
/// (kickoff / goal side,half 0|1 (hiệp phụ 2|3, phút 91–120),minute,wall,slot|null / penalties home,away (loạt penalty, trước end) /
/// ratings home[],away[] (trước end) / moment kind,side,slot,out?,half,minute,wall (từng pha, B3) / stats blocks{tên: addr,hex} (hết giờ, B3) /
/// end home,away (sau 120', không tính penalty) / error reason / nav_retry attempt,reason).
/// <see cref="MatchRunRequest.ExtraTime"/>: PESMATCH_EXTRA_TIME=1 (Thiết lập bật hiệp phụ + penalty).
/// <paramref name="onEvent"/>: nhận mọi sự kiện (theo dõi trực tiếp trên web).
/// <see cref="MatchRunRequest.Go"/>: PESMATCH_WAIT_GO=1, game chờ ở màn Thiết lập ("stage":"ready") tới khi ghi "go" vào stdin.
/// <see cref="MatchRunRequest.Refresh"/>: PESMATCH_WARM=1, "go FILE" = đội hình lúc bấm; <see cref="MatchRunRequest.Sync"/>: đội hình đổi lúc chờ
/// → yên <see cref="SyncQuiet"/> thì gửi "sync FILE" (PES17: game cài lại ngay, không đợi tới lúc bấm Sẵn sàng).
/// <paramref name="recordingDir"/>: có → ghi hình cả trận (PESMATCH_REC, C7) vào {recordingDir}/{fixture}-{giờ UTC}.mkv; "wall" = giây trong video.
/// <paramref name="liveDir"/>: có (cùng ghi hình) → thêm HLS xem trực tiếp (PESMATCH_LIVE) {liveDir}/{fixture}.m3u8, sự kiện "live" khi bắt đầu ghi.
/// <paramref name="logoDir"/>: thư mục logo tải lên (LogoStore) — trận có <see cref="MatchRunRequest.CompetitionLogo"/> và file có thật
/// → PESMATCH_LOGO = đường dẫn file (bộ chạy PES17 đặt logo lên màn hình chơi).
/// <see cref="MatchRunRequest.Hot"/>: PESMATCH_HOT=1 (trận cầu đinh, PO 23/11).
/// <paramref name="graphicsDir"/>: thư mục đồ hoạ của web (GraphicsStore, PO 23/11) → PESMATCH_GRAPHICS + PESMATCH_COMPETITION
/// (<see cref="MatchRunRequest.CompetitionId"/>): bộ chạy cài gói menu compe/{giải}.cpk của giải, giải chưa có gói → gỡ gói đang cài.
/// <paramref name="lockFile"/>: khoá "đang đá" của bộ chạy (PESMATCH_LOCK, nội dung "PID trận"). Bộ chạy bị dừng ngang (web dừng ngay sau "end",
/// huỷ, lỗi) không kịp tự xoá → xoá hộ khi khoá còn ghi PID của nó, kẻo sync.sh tưởng vẫn đang đá (PO 22/11).
/// </summary>
public sealed class ScriptMatchRunner(string scriptPath, string workDir, Action<MatchRunRequest, JsonElement>? onEvent = null, string? recordingDir = null,
    string? liveDir = null, TimeSpan? syncQuiet = null, string? logoDir = null, string? lockFile = null, string? graphicsDir = null)
    : IMatchRunner
{
    /// <summary>Gom thay đổi đội hình: HLV thường chỉnh liền nhiều lần, mỗi lần cài lại PES17 phải khởi động lại game (~1,5–2 phút).</summary>
    public static readonly TimeSpan SyncQuiet = TimeSpan.FromSeconds(15);

    public async Task<MatchRunOutcome> RunAsync(MatchRunRequest r, CancellationToken ct)
    {
        Directory.CreateDirectory(workDir);
        var of = Path.Combine(workDir, $"{r.FixtureId:N}.of");
        await File.WriteAllBytesAsync(of, r.OptionFile, ct);

        var psi = new ProcessStartInfo("python3") { RedirectStandardOutput = true, UseShellExecute = false };
        foreach (var a in new[] { scriptPath, "run", r.ContainerName, of, r.HomePesClubIndex.ToString(), r.AwayPesClubIndex.ToString() })
            psi.ArgumentList.Add(a);
        if (r.BothHuman) psi.ArgumentList.Add("both");
        else if (r.HumanSide is { } side) psi.ArgumentList.Add(side == MatchSide.Home ? "home" : "away");
        if (r.Go is not null) { psi.Environment[r.Refresh is null ? "PESMATCH_WAIT_GO" : "PESMATCH_WARM"] = "1"; psi.RedirectStandardInput = true; }
        if (r.ExtraTime) psi.Environment["PESMATCH_EXTRA_TIME"] = "1";
        if (recordingDir is not null) psi.Environment["PESMATCH_REC"] = Path.Combine(Path.GetFullPath(recordingDir), $"{r.FixtureId:N}-{DateTime.UtcNow:yyyyMMddHHmmss}.mkv");
        if (liveDir is not null) psi.Environment["PESMATCH_LIVE"] = Path.Combine(Path.GetFullPath(liveDir), $"{r.FixtureId:N}.m3u8");
        if (logoDir is not null && r.CompetitionLogo is { Length: > 0 } logo && Path.Combine(Path.GetFullPath(logoDir), Path.GetFileName(logo)) is var lf && File.Exists(lf))
            psi.Environment["PESMATCH_LOGO"] = lf;
        if (lockFile is not null) psi.Environment["PESMATCH_LOCK"] = Path.GetFullPath(lockFile);
        if (r.Hot) psi.Environment["PESMATCH_HOT"] = "1";   // trận cầu đinh: record-mode = hot chỉ ghi trận này (tools/match17/tools/rec17.py)
        if (graphicsDir is not null)
        {
            psi.Environment["PESMATCH_GRAPHICS"] = Path.GetFullPath(graphicsDir);
            psi.Environment["PESMATCH_COMPETITION"] = r.CompetitionId?.ToString("N") ?? "";
            psi.Environment["PESMATCH_COMPETITION_NAME"] = r.CompetitionName ?? "";   // thay chữ "Exhibition" trên dải tiêu đề (tools/pes17db/compename.py)
        }

        using var p = Process.Start(psi)!;
        var pid = p.Id;
        var stdin = new SemaphoreSlim(1, 1);
        async Task Send(string line)
        {
            await stdin.WaitAsync(CancellationToken.None);
            try { p.StandardInput.WriteLine(line); p.StandardInput.Flush(); } catch { } finally { stdin.Release(); }   // tiến trình đã thoát → bỏ qua
        }
        using var running = CancellationTokenSource.CreateLinkedTokenSource(ct);   // hết trận → dừng vòng sync
        if (r is { Sync: { } sync, Refresh: { } refresh, Go: { } go })
            _ = SyncAsync(sync, refresh, go, running.Token);
        async Task SyncAsync(SquadSync sync, Func<CancellationToken, Task<byte[]>> refresh, Task go, CancellationToken c)
        {
            try
            {
                for (var n = 1; !go.IsCompleted; n++)
                {
                    await sync.Reader.ReadAsync(c);
                    do await Task.WhenAny(Task.Delay(syncQuiet ?? SyncQuiet, c), go);
                    while (!go.IsCompleted && sync.Reader.TryRead(out _));
                    c.ThrowIfCancellationRequested();
                    if (go.IsCompleted) return;              // Sẵn sàng: "go FILE" tự dựng bản mới nhất
                    var file = Path.Combine(workDir, $"{r.FixtureId:N}.sync{n}.of");
                    await File.WriteAllBytesAsync(file, await refresh(c), c);
                    if (!go.IsCompleted) await Send($"sync {Path.GetFullPath(file)}");
                }
            }
            catch (Exception e) when (e is OperationCanceledException or ChannelClosedException) { }
            catch (Exception e)                              // dựng lỗi: bỏ lần này, lúc Sẵn sàng còn dựng lại
            {
                onEvent?.Invoke(r, JsonDocument.Parse(JsonSerializer.Serialize(new { @event = "sync_error", reason = e.Message })).RootElement);
            }
        }
        r.Go?.ContinueWith(async _ =>
        {
            var line = "go";
            if (r.Refresh is not null)                       // vào trận nhanh: OF đội hình mới nhất → pesmatch ghi vào bộ nhớ game
            {
                try
                {
                    var fresh = Path.Combine(workDir, $"{r.FixtureId:N}.go.of");
                    await File.WriteAllBytesAsync(fresh, await r.Refresh(ct), ct);
                    line = $"go {Path.GetFullPath(fresh)}";
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    onEvent?.Invoke(r, JsonDocument.Parse(JsonSerializer.Serialize(new { @event = "refresh_error", reason = e.Message })).RootElement);
                    try { p.Kill(true); } catch { }          // đội hình lỗi → không vào trận với đội hình cũ
                    return;
                }
            }
            await Send(line);
        }, TaskContinuationOptions.OnlyOnRanToCompletion);   // tiến trình đã thoát/dispose → bỏ qua
        var goals = new List<RunGoal>();
        var ratings = new List<RunRating>();
        (int Home, int Away)? penalties = null;
        string? recording = null;
        var moments = new List<RunMoment>();
        List<MemoryBlock>? stats = null;
        try
        {
            while (await p.StandardOutput.ReadLineAsync(ct) is { } line)
            {
                using var doc = JsonDocument.Parse(line);
                var e = doc.RootElement;
                onEvent?.Invoke(r, e.Clone());
                switch (e.GetProperty("event").GetString())
                {
                    case "goal":
                        goals.Add(new RunGoal(e.GetProperty("side").GetString() == "home" ? MatchSide.Home : MatchSide.Away,
                            e.GetProperty("half").GetInt32() + 1, e.GetProperty("minute").GetInt32(), e.GetProperty("wall").GetDouble(),
                            e.TryGetProperty("slot", out var slot) && slot.ValueKind == JsonValueKind.Number ? slot.GetInt32() : null));
                        break;
                    case "recording":
                        recording = e.GetProperty("path").GetString();
                        break;
                    case "ratings":
                        ratings = [.. ReadRatings(e, "home", MatchSide.Home), .. ReadRatings(e, "away", MatchSide.Away)];
                        break;
                    case "moment":
                        moments.Add(new RunMoment(e.GetProperty("side").GetString() == "home" ? MatchSide.Home : MatchSide.Away, e.GetProperty("half").GetInt32() + 1,
                            e.GetProperty("minute").GetInt32(), e.GetProperty("wall").GetDouble(), e.GetProperty("kind").GetString()!, e.GetProperty("slot").GetInt32(),
                            e.TryGetProperty("out", out var o) && o.ValueKind == JsonValueKind.Number ? o.GetInt32() : null));
                        break;
                    case "stats":
                        stats = [.. e.GetProperty("blocks").EnumerateObject().Select(b => new MemoryBlock(b.Name, b.Value.GetProperty("addr").GetInt64(),
                            Convert.FromHexString(b.Value.GetProperty("hex").GetString()!)))];
                        break;
                    case "penalties":
                        penalties = (e.GetProperty("home").GetInt32(), e.GetProperty("away").GetInt32());
                        break;
                    case "end":
                        return new MatchRunOutcome(true, e.GetProperty("home").GetInt32(), e.GetProperty("away").GetInt32(), goals, RecordingPath: recording, Ratings: ratings,
                            Penalties: penalties, Moments: moments, StatsBlocks: stats);
                    case "error":
                        return new MatchRunOutcome(false, 0, 0, goals, e.GetProperty("reason").GetString(), recording);
                }
            }
            return new MatchRunOutcome(false, 0, 0, goals, "script_exited", recording);
        }
        finally
        {
            running.Cancel();
            if (!p.HasExited) p.Kill(entireProcessTree: true);
            DropLock(pid);
            File.Delete(of);
            foreach (var f in Directory.EnumerateFiles(workDir, $"{r.FixtureId:N}.*.of"))
                try { File.Delete(f); } catch (IOException) { }
        }
    }

    private void DropLock(int pid)
    {
        if (lockFile is null) return;
        try
        {
            if (File.Exists(lockFile) && File.ReadAllText(lockFile).StartsWith($"{pid} ", StringComparison.Ordinal)) File.Delete(lockFile);
        }
        catch (IOException) { }   // khoá là phụ: lỗi đọc/xoá không làm hỏng kết quả trận
    }

    /// <summary>Dòng memory: player (ID PES), slot, rating|null; dòng chụp màn: shirt, rating.</summary>
    private static IEnumerable<RunRating> ReadRatings(JsonElement e, string key, MatchSide side)
    {
        int? Int(JsonElement x, string k) => x.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;
        var i = 0;
        foreach (var x in e.GetProperty(key).EnumerateArray())
        {
            var rating = x.GetProperty("rating") is { ValueKind: JsonValueKind.Number } v ? v.GetDecimal() : (decimal?)null;
            yield return new RunRating(side, Int(x, "slot") ?? i, Int(x, "player"), Int(x, "shirt"), rating);
            i++;
        }
    }
}
