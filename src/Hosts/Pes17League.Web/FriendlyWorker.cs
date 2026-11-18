using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Application;
using Pes17League.Clubs.Infrastructure;

namespace Pes17League.Web;

/// <summary>Máy giao hữu (PO 16/11): container riêng (nhân 4-7), 2 người chung 1 máy — P1 = đội nhà (bàn phím/cảm ứng),
/// P2 = đội khách (tay cầm ô 2, link #player2). Link có vé riêng từng trận (StreamPass, PO 17/11). <paramref name="Container"/> rỗng = tắt máy (trận Queued nằm chờ).</summary>
public sealed record FriendlyMachineOptions(string Container, string PlayUrl, TimeSpan MaxMatch);

/// <summary>Giao hữu chạy nền: mỗi 3 s dọn lời mời hết hạn / phòng quá hạn chót (tự Sẵn sàng); máy rảnh → nhận trận Queued cũ nhất,
/// chạy pesmatch.py "both", ghi kết quả. Mỗi lúc 1 trận (1 container).
/// Trận giải người vs người (PO 17/11) dùng chung máy này, ưu tiên trước giao hữu.</summary>
public sealed class FriendlyWorker(IDbContextFactory<ClubsDbContext> dbs, FriendlyHub hub, TimeProvider clock, IOptionFileStore store,
    MatchJobOptions job, FriendlyMachineOptions machine, MatchJobService jobs, StreamPass pass, ILogger<FriendlyWorker> log) : BackgroundService
{
    private Task? _running;
    /// <summary>PO 17/11: giới hạn <see cref="FriendlyMachineOptions.MaxMatch"/> tính từ giao bóng (mở máy/chọn đội/chỉnh đội không tính).
    /// Trước giao bóng chỉ chặn treo máy bằng <see cref="PreKickoff"/> (pesmatch tự xử AFK sau 5 phút chưa giao bóng).</summary>
    private CancellationTokenSource? _cts;
    private bool _kicked;
    private static readonly TimeSpan PreKickoff = TimeSpan.FromMinutes(15);
    private string Timeout() => _kicked ? $"Quá {machine.MaxMatch.TotalMinutes:0} phút từ giao bóng chưa xong trận" : "Máy quá 15 phút chưa vào trận";
    /// <summary>Lỗi AFK của pesmatch (PO 17/11): afk_home / afk_away = bên đó vắng 5 phút → xử thua 0-3; afk = cả hai/không rõ → huỷ.</summary>
    private static MatchSide? AfkSide(string? error) => error switch { "afk_home" => MatchSide.Home, "afk_away" => MatchSide.Away, _ => null };
    private const string AfkBoth = "Trận đứng 5 phút, không xác định được bên vắng → huỷ";

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await RecoverAsync(ct);
        using var t = new PeriodicTimer(TimeSpan.FromSeconds(3));
        while (await t.WaitForNextTickAsync(ct))
        {
            try
            {
                await using var db = await dbs.CreateDbContextAsync(ct);
                var before = await db.FriendlyMatches.CountAsync(m => m.Status == FriendlyStatus.Invited || m.Status == FriendlyStatus.Picking, ct);
                if (before > 0)
                {
                    await new FriendlyService(db, clock).SweepAsync(ct);
                    if (await db.FriendlyMatches.CountAsync(m => m.Status == FriendlyStatus.Invited || m.Status == FriendlyStatus.Picking, ct) != before) hub.Notify();
                }
                if (machine.Container.Length > 0 && _running is null or { IsCompleted: true }
                    && (await new LeagueDuelService(db, clock).ReadyAsync(ct))
                        .FirstOrDefault(d => hub.IsOnline(d.Home) && hub.IsOnline(d.Away)).Fixture is { } duel)
                {
                    hub.LeagueFixtureId = duel.Id;
                    hub.SetDuelError(duel.Id, null);
                    hub.SetStage(duel.Id, "boot");
                    hub.Notify();
                    _running = RunDuelAsync(duel.Id, ct);
                }
                else if (machine.Container.Length > 0 && _running is null or { IsCompleted: true }
                    && await new FriendlyService(db, clock).StartNextAsync(ct) is { } m)
                {
                    hub.SetStage(m.Id, "boot");
                    hub.Notify();
                    _running = RunAsync(m, ct);
                }
            }
            catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning(e, "Giao hữu chạy nền lỗi"); }
        }
    }

    /// <summary>Web khởi động lại giữa trận → pesmatch đã mất: trận Playing bị huỷ (không ai bị xử thua).</summary>
    private async Task RecoverAsync(CancellationToken ct)
    {
        try
        {
            await using var db = await dbs.CreateDbContextAsync(ct);
            var svc = new FriendlyService(db, clock);
            foreach (var id in await db.FriendlyMatches.Where(m => m.Status == FriendlyStatus.Playing).Select(m => m.Id).ToListAsync(ct))
                await svc.FailAsync(id, "Máy giao hữu khởi động lại giữa trận", ct);
            if (machine.Container.Length > 0)
            {
                await pass.CloseAsync();                             // vé trận dở trước khi web khởi động lại hết hiệu lực
                var orch = jobs.DuelOrchestrator(machine.Container, OnEvent);
                var mds = await (from a in db.MatchAttempts join f in db.Fixtures on a.FixtureId equals f.Id
                                 where a.Outcome == MatchAttemptOutcome.Running && a.ContainerName == machine.Container select f.MatchdayId).Distinct().ToListAsync(ct);
                var duels = new LeagueDuelService(db, clock);
                foreach (var md in mds)
                    foreach (var f in await orch.ReleaseStaleAttemptsAsync(md, ct)) await duels.UnreadyAsync(f, ct);   // trận giải người vs người dở → bấm Sẵn sàng lại
            }
        }
        catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning(e, "Dọn trận giao hữu dở lỗi"); }
    }

    private async Task RunAsync(FriendlyMatch m, CancellationToken stop)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(stop);
        cts.CancelAfter(PreKickoff);
        _cts = cts; _kicked = false;
        string? error;
        MatchRunOutcome? outcome = null;
        try
        {
            MatchRunRequest req;
            await using (var db = await dbs.CreateDbContextAsync(cts.Token))
                req = await new FriendlyService(db, clock).MachineRequestAsync(m, machine.Container, store, cts.Token);
            var runner = new ScriptMatchRunner(job.Script, job.WorkDir, OnEvent);
            outcome = await runner.RunAsync(req, cts.Token);
            error = outcome.Finished ? null : AfkSide(outcome.Error) is not null ? null : outcome.Error == "afk" ? AfkBoth : $"Máy giao hữu lỗi: {outcome.Error}";
        }
        catch (OperationCanceledException) when (!stop.IsCancellationRequested) { error = Timeout(); }
        catch (Exception e) when (e is not OperationCanceledException) { error = e.Message; log.LogWarning(e, "Trận giao hữu {Id} lỗi", m.Id); }

        try
        {
            await using var db = await dbs.CreateDbContextAsync(CancellationToken.None);
            var svc = new FriendlyService(db, clock);
            if (error is null && outcome is not null && AfkSide(outcome.Error) is { } afk)
                await svc.AbandonAsync(m.Id, afk == MatchSide.Home ? m.HomeManagerId : m.AwayManagerId, afterFirstHalf: true, afk: true);   // PO 17/11: AFK 5' → xử thua 0-3
            else if (error is null && outcome is not null)
                await svc.RecordResultAsync(m.Id, outcome.HomeGoals, outcome.AwayGoals,
                    outcome.Goals.Select(g => new FriendlyGoal { Home = g.Side == MatchSide.Home, Half = g.Half, Minute = g.Minute, Slot = g.Slot }));
            else
                await svc.FailAsync(m.Id, error ?? "?");
        }
        catch (Exception e) { log.LogWarning(e, "Ghi kết quả giao hữu {Id} lỗi", m.Id); }
        await pass.CloseAsync(m.Id);
        hub.SetStage(m.Id, null);
        hub.Notify();
    }

    private async Task RunDuelAsync(Guid fixtureId, CancellationToken stop)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(stop);
        cts.CancelAfter(PreKickoff);
        _cts = cts; _kicked = false;
        string? error = null;
        try
        {
            var r = await jobs.DuelOrchestrator(machine.Container, OnEvent).PlayFixtureAsync(fixtureId, null, machine.Container, cts.Token, both: true);
            if (!r.Played && AfkSide(r.LastError) is { } afk) error = await ForfeitAsync(fixtureId, afk);
            else if (!r.Played) error = r.LastError == "afk" ? AfkBoth : r.LastError ?? "Máy chưa ghi được kết quả";
            if ((r.HighlightError ?? r.StatsError ?? r.PressError) is { } warn) log.LogInformation("Trận giải người vs người {Id}: {Warn}", fixtureId, warn);
        }
        catch (OperationCanceledException) when (!stop.IsCancellationRequested) { error = Timeout(); }
        catch (Exception e) when (e is not OperationCanceledException) { error = e.Message; log.LogWarning(e, "Trận giải người vs người {Id} lỗi", fixtureId); }
        if (error is not null)
        {
            hub.SetDuelError(fixtureId, error);
            try
            {
                await using var db = await dbs.CreateDbContextAsync(CancellationToken.None);
                await new LeagueDuelService(db, clock).UnreadyAsync(fixtureId);
            }
            catch (Exception e) { log.LogWarning(e, "Bỏ Sẵn sàng trận {Id} lỗi", fixtureId); }
        }
        await pass.CloseAsync(fixtureId);
        hub.LeagueFixtureId = null;
        hub.SetStage(fixtureId, null);
        hub.Notify();
    }

    /// <summary>Trận giải người vs người, 1 bên AFK 5' (PO 17/11): bên vắng xử thua 0-3 (ghi như Ban tổ chức nhập), đánh dấu vắng
    /// (không reset chuỗi vắng). Trả lỗi nếu ghi không được (trận về chờ Sẵn sàng lại).</summary>
    private async Task<string?> ForfeitAsync(Guid fixtureId, MatchSide absent)
    {
        try
        {
            await using var db = await dbs.CreateDbContextAsync(CancellationToken.None);
            var f = await db.Fixtures.SingleAsync(x => x.Id == fixtureId);
            if (absent == MatchSide.Home) f.HomeAbsent = true; else f.AwayAbsent = true;
            await db.SaveChangesAsync();
            var r = await new CompetitionService(db, store, clock).RecordResultAsync(fixtureId, absent == MatchSide.Home ? 0 : 3, absent == MatchSide.Home ? 3 : 0, [],
                ResultSource.Admin, ct: CancellationToken.None);
            return r.Ok ? null : $"Xử thua AFK lỗi: {r.Error}";
        }
        catch (Exception e) { log.LogWarning(e, "Xử thua AFK trận {Id} lỗi", fixtureId); return $"Xử thua AFK lỗi: {e.Message}"; }
    }

    private void OnEvent(MatchRunRequest r, JsonElement e)
    {
        var ev = e.GetProperty("event").GetString();
        if (ev == "kickoff" && !_kicked) { _kicked = true; try { _cts?.CancelAfter(machine.MaxMatch); } catch (ObjectDisposedException) { } }
        // PO 18/11: vé/link chơi chỉ cấp khi sân đã chờ giao bóng VÀ máy đổi xong camera (pesmatch stage 'pitch'). Trước đó người chơi
        // bấm chen lúc máy đi bảng Tạm dừng → kẹt menu "Thiết lập Người chơi", trận bị huỷ. 'goal' = dự phòng (bóng đã lăn).
        var stage = ev == "stage" ? e.GetProperty("stage").GetString() : ev == "goal" ? "kickoff" : null;
        if (stage == "pitch") stage = "kickoff";
        if (stage is not null && hub.SetStage(r.FixtureId, stage)) hub.Notify();
        if (stage == "kickoff" && !pass.Issued(r.FixtureId)) _ = OpenPassAsync(r.FixtureId);
    }

    private async Task OpenPassAsync(Guid id)
    {
        try { await pass.OpenAsync(id, CancellationToken.None); hub.Notify(); }
        catch (Exception e) { log.LogWarning(e, "Cấp vé máy giao hữu {Id} lỗi", id); }
    }
}
