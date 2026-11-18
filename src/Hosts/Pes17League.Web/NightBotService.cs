using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Application;
using Pes17League.Clubs.Infrastructure;

namespace Pes17League.Web;

/// <summary>Hạn đá vòng (PO 17/11: khung giờ dài trong ngày + BOT ban đêm, 3–4 vòng/tuần). <see cref="Days"/> = các đêm hạn chót,
/// <see cref="HourVn"/> = giờ VN bắt đầu đá BOT. Days rỗng = tắt.</summary>
public sealed record NightBotOptions(IReadOnlySet<DayOfWeek> Days, int HourVn)
{
    public static readonly TimeSpan Vn = TimeSpan.FromHours(7);

    /// <summary>Hạn chót kế tiếp (giờ VN) tính từ <paramref name="now"/>; null nếu tắt.</summary>
    public DateTimeOffset? NextDeadline(DateTimeOffset now)
    {
        if (Days.Count == 0) return null;
        var vn = now.ToOffset(Vn);
        for (var i = 0; i < 8; i++)
        {
            var d = new DateTimeOffset(vn.Date.AddDays(i).AddHours(HourVn), Vn);
            if (d > now && Days.Contains(d.DayOfWeek)) return d;
        }
        return null;
    }

    /// <summary>Hạn chót đã tới và còn trong 8 giờ sau đó (đêm đá BOT) → mốc hạn; không thì null.</summary>
    public DateTimeOffset? ActiveDeadline(DateTimeOffset now)
    {
        if (Days.Count == 0) return null;
        var vn = now.ToOffset(Vn);
        for (var i = 0; i < 2; i++)
        {
            var d = new DateTimeOffset(vn.Date.AddDays(-i).AddHours(HourVn), Vn);
            if (d <= now && now < d.AddHours(8) && Days.Contains(d.DayOfWeek)) return d;
        }
        return null;
    }

    public static string Format(DateTimeOffset d)
    {
        var v = d.ToOffset(Vn);
        var day = v.DayOfWeek == DayOfWeek.Sunday ? "CN" : $"T{(int)v.DayOfWeek + 1}";
        return $"{v:HH:mm} {day} {v:dd/MM}";
    }
}

/// <summary>Đêm hạn chót: trận HLV người chưa đá → trợ lý đá thay + đếm vắng (<see cref="AbsenceService"/>), rồi đá mọi trận còn lại của vòng đang mở.
/// Máy BOT tắt → thư Ban tổ chức 1 lần, đợi máy bật trong 8 giờ.
/// Máy đang bận (trận người đang đá) → thử lại mỗi phút. Mỗi đêm hạn chạy 1 lần (khởi động lại web giữa đêm thì chạy tiếp phần còn lại).</summary>
public sealed class NightBotService(IDbContextFactory<ClubsDbContext> dbs, MatchJobService jobs, NightBotOptions o, TimeProvider clock,
    ILogger<NightBotService> log) : BackgroundService
{
    private DateTimeOffset? _done, _marked, _warned;
    private const string Machine = "pesmatch-1";

    /// <summary>Máy BOT có đang chạy (docker inspect). Không gọi được docker → coi như chạy (để MatchJobService tự báo lỗi).</summary>
    private async Task<bool> MachineUpAsync(CancellationToken ct)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("docker", ["inspect", "-f", "{{.State.Running}}", Machine]) { RedirectStandardOutput = true, RedirectStandardError = true };
            using var p = System.Diagnostics.Process.Start(psi)!;
            var output = await p.StandardOutput.ReadToEndAsync(ct);
            await p.WaitForExitAsync(ct);
            return p.ExitCode != 0 ? false : output.Trim() == "true";
        }
        catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning(e, "Không kiểm được máy {Machine}", Machine); return true; }
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var t = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await t.WaitForNextTickAsync(ct))
        {
            try
            {
                if (o.ActiveDeadline(clock.GetUtcNow()) is not { } deadline || _done == deadline || jobs.Running) continue;
                await using var db = await dbs.CreateDbContextAsync(ct);
                var md = await (from m in db.Matchdays join s in db.Seasons on m.SeasonId equals s.Id
                                where s.Status == SeasonStatus.Active && m.Status != MatchdayStatus.Settled && m.Status != MatchdayStatus.Planned
                                orderby s.Number, m.Number select m).FirstOrDefaultAsync(ct);
                if (md is null || md.Status == MatchdayStatus.Completed) { _done = deadline; continue; }
                if (_marked != deadline)
                {
                    var rep = await new AbsenceService(db, clock).ApplyDeadlineAsync(md.Id, ct);   // người chưa đá → trợ lý; vắng → đếm, 3 cảnh cáo, 5 về BOT
                    _marked = deadline;
                    log.LogInformation("Đêm hạn {Deadline} vòng {Number}: {N} trận giao trợ lý, vắng: {Absent}, cảnh cáo: {Warned}, về BOT: {Released}",
                        NightBotOptions.Format(deadline), md.Number, rep.Assisted, string.Join(", ", rep.Absent), string.Join(", ", rep.Warned), string.Join(", ", rep.Released));
                }
                if (!await MachineUpAsync(ct))
                {
                    if (_warned != deadline)
                    {
                        _warned = deadline;
                        await new MailService(db, clock).SystemToAdminAsync($"Máy BOT ({Machine}) đang tắt — vòng {md.Number} chưa đá",
                            $"Đêm hạn {NightBotOptions.Format(deadline)}: máy {Machine} không chạy nên các trận còn lại của vòng {md.Number} chưa đá. " +
                            "Bật máy lại: trong 8 giờ sau hạn hệ thống tự đá tiếp; quá giờ thì vào Quản trị bấm \"Đá các trận BOT\".", ct);
                        log.LogWarning("Đêm hạn {Deadline}: máy {Machine} tắt, chưa đá BOT", NightBotOptions.Format(deadline), Machine);
                    }
                    continue;
                }
                if (jobs.PlayBots(md.Id))
                {
                    _done = deadline;
                    log.LogInformation("Đêm hạn {Deadline}: đá BOT vòng {Number}", NightBotOptions.Format(deadline), md.Number);
                }
            }
            catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning(e, "Đá BOT ban đêm lỗi"); }
        }
    }
}
