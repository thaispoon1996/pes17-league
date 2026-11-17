using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Domain;
using Pes17League.Clubs.Infrastructure;

namespace Pes17League.Web;

/// <summary>1 bàn trong clip vòng: <paramref name="No"/> = thứ tự trong clip, <paramref name="Start"/> = giây bắt đầu pha bàn đó trong clip.</summary>
public sealed record ReelGoal(Guid GoalId, Guid FixtureId, int No, double Start, string Who, string Score);

/// <summary>Clip tổng hợp bàn thắng 1 vòng của 1 giải (PO 12/11): chỉ các pha bàn thắng, không ghép highlight trận.</summary>
/// <param name="Missing">Bàn không có hình (trận nhập tay tỷ số, bản ghi đã xoá) nên thiếu trong clip.</param>
/// <param name="Format">Kiểu dựng (<see cref="RoundReelService.ReelFormat"/>); cũ hơn → dựng lại nếu còn bản ghi.</param>
public sealed record RoundReelInfo(string Key, int Round, IReadOnlyList<ReelGoal> Items, int Missing, double Seconds, string Signature, DateTimeOffset BuiltAt,
    int Format = 1)
{
    public string Url => $"{RoundReelStore.RequestPath}/{Key}";
    public string DocUrl => $"{Url}/doc";
    public string PosterUrl => $"{Url}/anh";
    public string Duration => TimeSpan.FromSeconds(Math.Round(Seconds)).ToString(Seconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss");
}

/// <summary>Clip vòng: {Dir}/{key}.mp4 (16:9), {key}-doc.mp4 (9:16), {key}.jpg, {key}.json (<see cref="RoundReelInfo"/>);
/// key = {giải hoặc mùa alpha:N}-v{số vòng}; web: /video/vong/{key}, /doc, /anh.</summary>
public sealed record RoundReelStore(string Dir)
{
    public const string RequestPath = "/video/vong";
    private static readonly System.Text.RegularExpressions.Regex KeyFormat = new(@"^[0-9a-f]{32}-v\d{1,3}\z");

    public static string Key(Guid? editionId, Guid seasonId, int round) => $"{editionId ?? seasonId:N}-v{round}";

    /// <summary>File của clip; key sai định dạng → null.</summary>
    public string? PathFor(string key, string suffix) => KeyFormat.IsMatch(key) ? Path.Combine(Dir, key + suffix) : null;

    /// <summary>Clip đã dựng; chưa có → null.</summary>
    public RoundReelInfo? Info(Guid? editionId, Guid seasonId, int round) => Info(Key(editionId, seasonId, round));

    public RoundReelInfo? Info(string key)
    {
        if (PathFor(key, ".json") is not { } json || !File.Exists(json) || !File.Exists(PathFor(key, ".mp4"))) return null;
        try { return JsonSerializer.Deserialize<RoundReelInfo>(File.ReadAllText(json)); }
        catch (JsonException) { return null; }
    }
}

/// <summary>
/// Chạy nền lúc máy trận rảnh (không đè CPU lúc đang ghi hình):
/// 1) cắt đoạn nguồn từng bàn (tools/match/goal_clips.py) từ bản ghi của lần đá xong → {GoalDir}/{goal:N}.mp4, chỉ dùng nội bộ;
/// 2) xoá bản ghi trận quá <see cref="KeepRecordingDays"/> ngày khi mọi bàn của nó đã có đoạn hoặc đã vào clip (quá <see cref="MaxRecordingDays"/> thì xoá luôn);
/// 3) khi mọi trận của 1 giải trong 1 vòng đã đá, dựng clip vòng từ các bàn (tools/match/round_reel.py); bàn/người ghi/tỷ số đổi mà còn đủ đoạn → dựng lại;
/// 4) bàn đã vào clip vòng → xoá đoạn nguồn (PO 12/11: không giữ video bàn lẻ). Sau đó clip vòng không dựng lại được nữa.
/// </summary>
public sealed class RoundReelService(IDbContextFactory<ClubsDbContext> dbs, MatchJobOptions o, RoundReelStore reels,
    MatchJobService jobs, ILogger<RoundReelService> log) : BackgroundService
{
    public const int KeepRecordingDays = 7, MaxRecordingDays = 14;
    /// <summary>1 = có phát lại chậm + số bình chọn; 2 = chỉ pha bàn thắng (PO 12/11 bỏ chiếu chậm, bỏ bình chọn).</summary>
    public const int ReelFormat = 2;
    /// <summary>Mốc bàn nằm ở giây này của đoạn nguồn (PRE của goal_clips.py); bàn trong 12 s đầu bản ghi thì sớm hơn.</summary>
    private const double ClipPre = 12;

    private readonly SemaphoreSlim _kick = new(0);
    private readonly Dictionary<string, string> _failed = [];   // key/bản ghi → chữ ký lần lỗi: không thử lại tới khi dữ liệu đổi

    /// <summary>Thư mục gốc dữ liệu (cha của reels/ và goals/), gắn vào docker của các script.</summary>
    private string Root => Path.GetDirectoryName(Path.GetFullPath(reels.Dir))!;
    private string GoalDir => Path.Combine(Root, "goals");
    private string GoalClip(Guid goalId) => Path.Combine(GoalDir, $"{goalId:N}.mp4");

    /// <summary>Đánh thức vòng quét (sau mỗi việc trận).</summary>
    public void Kick() { if (_kick.CurrentCount == 0) _kick.Release(); }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        jobs.Changed += () => { if (!jobs.Running) Kick(); };
        await Task.Delay(TimeSpan.FromSeconds(30), ct);   // web vừa khởi động: nhường chỗ
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await CutGoalClipsAsync(ct);
                await CleanRecordingsAsync(ct);
                await BuildPendingAsync(ct);
                DeleteUsedClips();
            }
            catch (Exception e) when (e is not OperationCanceledException) { log.LogError(e, "Quét clip vòng lỗi"); }
            await _kick.WaitAsync(TimeSpan.FromMinutes(30), ct);
        }
    }

    private sealed record GoalRow(Guid Id, Guid FixtureId, Guid ClubId, int Half, int Minute, double? Offset, string? Scorer);

    /// <summary>Bàn có mốc hình của các trận đã đá, theo bản ghi của lần đá xong mới nhất (còn file).</summary>
    private async Task<List<(string Recording, List<GoalRow> Goals)>> RecordedGoalsAsync(CancellationToken ct)
    {
        await using var db = await dbs.CreateDbContextAsync(ct);
        var attempts = await db.MatchAttempts.Where(a => a.Outcome == MatchAttemptOutcome.Finished && a.RecordingPath != null)
            .Select(a => new { a.FixtureId, a.RecordingPath, a.StartedAt }).ToListAsync(ct);
        var latest = attempts.GroupBy(a => a.FixtureId).ToDictionary(g => g.Key, g => g.MaxBy(a => a.StartedAt)!.RecordingPath!);
        var goals = await db.Goals.Where(g => g.VideoOffsetSec != null)
            .Select(g => new GoalRow(g.Id, g.FixtureId, g.ClubId, g.Half, g.Minute, g.VideoOffsetSec, null)).ToListAsync(ct);
        return [.. goals.Where(g => latest.ContainsKey(g.FixtureId)).GroupBy(g => latest[g.FixtureId]).Select(g => (g.Key, g.ToList()))];
    }

    /// <summary>Cắt đoạn các bàn chưa có; dừng ngay khi máy trận bận.</summary>
    private async Task CutGoalClipsAsync(CancellationToken ct)
    {
        foreach (var (rec, goals) in await RecordedGoalsAsync(ct))
        {
            var used = UsedGoals();
            var todo = goals.Where(g => !File.Exists(GoalClip(g.Id)) && !used.Contains(g.Id)).ToList();
            if (todo.Count == 0 || !File.Exists(rec)) continue;
            if (jobs.Running) return;
            var sig = string.Join(",", todo.Select(g => g.Id));
            if (_failed.TryGetValue(rec, out var s) && s == sig) continue;
            try
            {
                var sw = Stopwatch.StartNew();
                await RunAsync("goal_clips.py", [rec, GoalDir], todo.Select(g => new { id = g.Id.ToString("N"), t = g.Offset }), ct);
                _failed.Remove(rec);
                log.LogInformation("Đoạn bàn thắng: {Count} bàn từ {Rec}, {Took:0} s", todo.Count, Path.GetFileName(rec), sw.Elapsed.TotalSeconds);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _failed[rec] = sig;
                log.LogError(e, "Cắt đoạn bàn thắng từ {Rec} lỗi", rec);
            }
        }
    }

    /// <summary>Thay việc dọn của highlight trận cũ: bản ghi quá 7 ngày mà mọi bàn đã có đoạn (hoặc quá 14 ngày) → xoá.</summary>
    private async Task CleanRecordingsAsync(CancellationToken ct)
    {
        if (o.RecordingDir is null || !Directory.Exists(o.RecordingDir) || jobs.Running) return;
        var used = UsedGoals();
        var pending = (await RecordedGoalsAsync(ct))
            .Where(r => r.Goals.Any(g => !File.Exists(GoalClip(g.Id)) && !used.Contains(g.Id))).Select(r => Path.GetFullPath(r.Recording)).ToHashSet();
        foreach (var f in new DirectoryInfo(o.RecordingDir).EnumerateFiles("*.mkv"))
        {
            var age = DateTime.UtcNow - f.LastWriteTimeUtc;
            if (age < TimeSpan.FromDays(KeepRecordingDays) || (pending.Contains(f.FullName) && age < TimeSpan.FromDays(MaxRecordingDays))) continue;
            try { f.Delete(); log.LogInformation("Xoá bản ghi cũ {File}", f.Name); }
            catch (IOException e) { log.LogWarning(e, "Không xoá được {File}", f.Name); }
        }
    }

    /// <summary>Dựng các clip vòng đã đủ trận mà chưa có hoặc đã cũ; dừng ngay khi máy trận bận.</summary>
    public async Task BuildPendingAsync(CancellationToken ct)
    {
        foreach (var job in await PendingAsync(ct))
        {
            if (jobs.Running) return;   // xong việc trận sẽ được đánh thức lại
            if (_failed.TryGetValue(job.Key, out var sig) && sig == job.Signature) continue;
            try
            {
                var sw = Stopwatch.StartNew();
                var (seconds, marks) = await RenderAsync(job, ct);
                var items = job.Items.Select((g, i) => g with { Start = marks[i] }).ToList();
                var info = new RoundReelInfo(job.Key, job.Round, items, job.Missing, seconds, job.Signature, DateTimeOffset.UtcNow, ReelFormat);
                await File.WriteAllTextAsync(reels.PathFor(job.Key, ".json")!, JsonSerializer.Serialize(info), ct);
                _failed.Remove(job.Key);
                log.LogInformation("Clip vòng {Key}: {Title} vòng {Round}, {Goals} bàn, {Seconds:0} s, dựng {Took:0} s",
                    job.Key, job.Title, job.Round, items.Count, seconds, sw.Elapsed.TotalSeconds);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _failed[job.Key] = job.Signature;
                log.LogError(e, "Clip vòng {Key} lỗi", job.Key);
            }
        }
    }

    /// <summary>Các clip vòng đã dựng.</summary>
    private IEnumerable<RoundReelInfo> Built() => Directory.Exists(reels.Dir)
        ? Directory.EnumerateFiles(reels.Dir, "*.json").Select(f => reels.Info(Path.GetFileNameWithoutExtension(f))).OfType<RoundReelInfo>()
        : [];

    /// <summary>Bàn đã vào clip vòng kiểu hiện tại (không cần đoạn nguồn nữa).</summary>
    private HashSet<Guid> UsedGoals() => [.. Built().Where(r => r.Format == ReelFormat).SelectMany(r => r.Items.Select(i => i.GoalId))];

    /// <summary>Xoá đoạn nguồn của các bàn đã vào clip vòng.</summary>
    private void DeleteUsedClips()
    {
        var n = 0;
        foreach (var id in UsedGoals())
        {
            if (!File.Exists(GoalClip(id))) continue;
            try { File.Delete(GoalClip(id)); n++; }
            catch (IOException e) { log.LogWarning(e, "Không xoá được đoạn bàn {Goal}", id); }
        }
        if (n > 0) log.LogInformation("Xoá {Count} đoạn bàn thắng đã vào clip vòng", n);
    }

    private sealed record ReelJob(string Key, string Title, int Round, int Goals, int Missing, List<ReelGoal> Items, Dictionary<Guid, double> At, string Signature);

    private async Task<List<ReelJob>> PendingAsync(CancellationToken ct)
    {
        var q = new LeagueQueries(dbs);
        if (await q.SeasonAsync() is not { } season) return [];
        var editions = await q.EditionsAsync(season.Id);
        var fixtures = await q.FixturesAsync(season.Id);
        var ids = fixtures.Select(f => f.Id).ToList();
        List<GoalRow> goals;
        await using (var db = await dbs.CreateDbContextAsync(ct))
            goals = await (from g in db.Goals.Where(g => ids.Contains(g.FixtureId))
                           join p in db.Players on g.ScorerPlayerId equals p.Id into ps
                           from p in ps.DefaultIfEmpty()
                           select new GoalRow(g.Id, g.FixtureId, g.ClubId, g.Half, g.Minute, g.VideoOffsetSec, p == null ? null : p.Name)).ToListAsync(ct);
        var byFixture = goals.OrderBy(g => g.Half).ThenBy(g => g.Minute).ThenBy(g => g.Offset).ToLookup(g => g.FixtureId);

        var list = new List<ReelJob>();
        foreach (var grp in fixtures.GroupBy(f => (f.EditionId, f.Matchday)))
        {
            if (grp.Any(f => f.Status != FixtureStatus.Played)) continue;   // vòng của giải chưa đá xong
            var key = RoundReelStore.Key(grp.Key.EditionId, season.Id, grp.Key.Matchday);
            var old = reels.Info(key) is { } r && r.Format == ReelFormat ? r : null;   // kiểu cũ: coi như chưa dựng
            var inOld = old?.Items.Select(i => i.GoalId).ToHashSet() ?? [];
            var items = new List<ReelGoal>();
            var at = new Dictionary<Guid, double>();
            int total = 0, lacking = 0;   // lacking: bàn có mốc hình mà không còn đoạn nguồn
            foreach (var f in grp.OrderBy(f => season.Clubs[f.Home], StringComparer.Ordinal))
            {
                int h = 0, a = 0;
                foreach (var g in byFixture[f.Id])
                {
                    total++;
                    if (g.ClubId == f.Home) h++; else a++;
                    if (g.Offset is not { } off) continue;
                    if (!File.Exists(GoalClip(g.Id)) && !inOld.Contains(g.Id)) { lacking++; continue; }
                    at[g.Id] = Math.Min(ClipPre, off);
                    items.Add(new ReelGoal(g.Id, f.Id, items.Count + 1, 0, $"{g.Minute}' {g.Scorer ?? season.Clubs[g.ClubId]}",
                        $"{season.Clubs[f.Home]} {h}–{a} {season.Clubs[f.Away]}"));
                }
            }
            if (items.Count == 0) continue;
            if (old is null && lacking > 0 && reels.Info(key) is not null) continue;   // clip kiểu cũ đủ bàn hơn: chờ cắt đoạn, không thì giữ
            var signature = string.Join(";", items.Select(i => $"{i.GoalId:N}|{i.Who}|{i.Score}"));
            if (old is not null && signature == string.Join(";", old.Items.Select(i => $"{i.GoalId:N}|{i.Who}|{i.Score}"))) continue;
            if (items.Any(i => !File.Exists(GoalClip(i.GoalId))))
            {   // đoạn nguồn đã xoá (bản ghi hết hạn): giữ clip cũ
                if (_failed.TryAdd(key, signature)) log.LogWarning("Clip vòng {Key}: dữ liệu bàn đổi nhưng đoạn nguồn đã xoá, giữ clip cũ", key);
                continue;
            }
            var title = editions.FirstOrDefault(e => e.Id == grp.Key.EditionId)?.Name ?? season.Name;
            list.Add(new ReelJob(key, title, grp.Key.Matchday, total, total - items.Count, items, at, signature));
        }
        return [.. list.OrderBy(j => j.Round)];
    }

    /// <returns>Độ dài clip (giây) và giây bắt đầu từng bàn.</returns>
    private async Task<(double Seconds, double[] Marks)> RenderAsync(ReelJob job, CancellationToken ct)
    {
        var rel = Path.GetRelativePath(Root, Path.GetFullPath(reels.Dir));
        var goals = Path.GetRelativePath(Root, GoalDir);
        var output = await RunAsync("round_reel.py", [Root, $"{rel}/{job.Key}.mp4", $"{rel}/{job.Key}-doc.mp4"], new
        {
            title = job.Title,
            subtitle = $"Vòng {job.Round} · {job.Goals} bàn",
            outro = new[] { "Hẹn gặp lại ở vòng sau", "PES League" },
            items = job.Items.Select(i => new { src = $"{goals}/{i.GoalId:N}.mp4", at = job.At[i.GoalId], no = i.No, who = i.Who, score = i.Score }),
        }, ct);
        var r = JsonDocument.Parse(output).RootElement;
        return (r.GetProperty("seconds").GetDouble(), [.. r.GetProperty("marks").EnumerateArray().Select(m => m.GetDouble())]);
    }

    /// <summary>Chạy script trong tools/match (cạnh <see cref="MatchJobOptions.Script"/>), stdin = JSON; trả stdout.</summary>
    private async Task<string> RunAsync(string script, string[] args, object input, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("python3") { RedirectStandardInput = true, RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
        psi.ArgumentList.Add(Path.Combine(Path.GetDirectoryName(o.Script)!, script));
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        await p.StandardInput.WriteAsync(JsonSerializer.Serialize(input));
        p.StandardInput.Close();
        var err = p.StandardError.ReadToEndAsync(ct);
        var output = await p.StandardOutput.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        if (p.ExitCode != 0) throw new InvalidOperationException($"{script}: {(await err).Trim()}");
        return output;
    }
}
