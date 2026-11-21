using System.Text;
using Pes17League.Clubs.Application;
using Pes17League.Clubs.Infrastructure;

namespace Pes17League.Clubs.Tests;

/// <summary>Cần python3 (image sdk:8.0 không có → skip; chạy trong image pes17league-cli:local).</summary>
public sealed class PythonFactAttribute : FactAttribute
{
    public PythonFactAttribute()
    {
        if (!(Environment.GetEnvironmentVariable("PATH") ?? "").Split(':').Any(d => File.Exists(Path.Combine(d, "python3")))) Skip = "Không có python3";
    }
}

/// <summary>Vào trận nhanh PES17 (PO 21/11): đội hình đổi lúc chờ → gom rồi gửi "sync FILE"; Sẵn sàng → "go FILE" bản mới nhất.</summary>
public sealed class ScriptMatchRunnerTests : IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("pesrun").FullName;

    public void Dispose() => Directory.Delete(dir, true);

    // bộ chạy giả: báo ready, ghi "lệnh nội-dung-file" của mỗi dòng stdin vào OF.stdin; go → hết trận 1-0
    private const string Fake = """
        import sys, json
        of = sys.argv[3]
        print(json.dumps({"event": "stage", "stage": "ready"}), flush=True)
        for line in sys.stdin:
            cmd, path = line.split()
            open(of + ".stdin", "a").write(cmd + " " + open(path).read() + "\n")
            if cmd == "go":
                print(json.dumps({"event": "end", "home": 1, "away": 0}), flush=True)
                break
        """;

    [PythonFact]
    public async Task Warm_sync_is_debounced_and_go_sends_latest_squads()
    {
        var script = Path.Combine(dir, "fake.py");
        File.WriteAllText(script, Fake);
        var ready = new TaskCompletionSource();
        var runner = new ScriptMatchRunner(script, dir, (_, e) => { if (e.GetProperty("event").GetString() == "stage") ready.TrySetResult(); },
            syncQuiet: TimeSpan.FromMilliseconds(600));
        var go = new TaskCompletionSource();
        var sync = new SquadSync();
        var n = 0;
        var req = new MatchRunRequest(Guid.NewGuid(), "pes17-bench", "v0"u8.ToArray(), 1, 2, MatchSide.Away, go.Task,
            Refresh: _ => Task.FromResult(Encoding.UTF8.GetBytes($"v{Interlocked.Increment(ref n)}")), Sync: sync);
        var run = runner.RunAsync(req, CancellationToken.None);
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(20));

        for (var i = 0; i < 3; i++) { sync.Changed(); await Task.Delay(50); }   // chỉnh liền 3 lần → 1 lần sync
        await Task.Delay(2500);
        sync.Changed();                                                          // chỉnh rồi bấm Sẵn sàng ngay → không sync, go mang bản mới
        await Task.Delay(50);
        go.SetResult();

        var outcome = await run.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.True(outcome.Finished);
        Assert.Equal(["sync v1", "go v2"], File.ReadAllLines(Path.Combine(dir, $"{req.FixtureId:N}.of.stdin")));
        Assert.Empty(Directory.GetFiles(dir, "*.of"));                          // OF + bản sync/go đã dọn
    }
}
