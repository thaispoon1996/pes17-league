using System.Diagnostics;
using System.Text.Json;
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
/// <paramref name="recordingDir"/>: có → ghi hình cả trận (PESMATCH_REC, C7) vào {recordingDir}/{fixture}-{giờ UTC}.mkv; "wall" = giây trong video.
/// <paramref name="liveDir"/>: có (cùng ghi hình) → thêm HLS xem trực tiếp (PESMATCH_LIVE) {liveDir}/{fixture}.m3u8, sự kiện "live" khi bắt đầu ghi.
/// </summary>
public sealed class ScriptMatchRunner(string scriptPath, string workDir, Action<MatchRunRequest, JsonElement>? onEvent = null, string? recordingDir = null,
    string? liveDir = null)
    : IMatchRunner
{
    public async Task<MatchRunOutcome> RunAsync(MatchRunRequest r, CancellationToken ct)
    {
        Directory.CreateDirectory(workDir);
        var of = Path.Combine(workDir, $"{r.FixtureId:N}.of");
        await File.WriteAllBytesAsync(of, r.OptionFile, ct);

        var psi = new ProcessStartInfo("python3") { RedirectStandardOutput = true, UseShellExecute = false };
        foreach (var a in new[] { scriptPath, "run", r.ContainerName, of, r.HomePesClubIndex.ToString(), r.AwayPesClubIndex.ToString() })
            psi.ArgumentList.Add(a);
        if (r.HumanSide is { } side) psi.ArgumentList.Add(side == MatchSide.Home ? "home" : "away");
        if (r.Go is not null) { psi.Environment["PESMATCH_WAIT_GO"] = "1"; psi.RedirectStandardInput = true; }
        if (r.ExtraTime) psi.Environment["PESMATCH_EXTRA_TIME"] = "1";
        if (recordingDir is not null) psi.Environment["PESMATCH_REC"] = Path.Combine(Path.GetFullPath(recordingDir), $"{r.FixtureId:N}-{DateTime.UtcNow:yyyyMMddHHmmss}.mkv");
        if (liveDir is not null) psi.Environment["PESMATCH_LIVE"] = Path.Combine(Path.GetFullPath(liveDir), $"{r.FixtureId:N}.m3u8");

        using var p = Process.Start(psi)!;
        r.Go?.ContinueWith(_ => { try { p.StandardInput.WriteLine("go"); p.StandardInput.Flush(); } catch { } },
            TaskContinuationOptions.OnlyOnRanToCompletion);   // tiến trình đã thoát/dispose → bỏ qua
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
            if (!p.HasExited) p.Kill(entireProcessTree: true);
            File.Delete(of);
        }
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
