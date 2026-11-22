using System.Collections.Concurrent;
using System.Diagnostics;

namespace Pes17League.Web;

/// <summary>Tình trạng 1 máy game (trang /may-choi, PO 22/11: "để người dùng biết máy nào rảnh có thể vào chơi").</summary>
public enum MachineState { Free, Playing, Preparing, Busy, Offline, Unknown }

/// <param name="ContainerUp">Container máy đang chạy; null = không hỏi được docker (vd bản thử không gắn docker.sock).</param>
/// <param name="GameUp">PES2017.exe đang chạy trong máy; <paramref name="Crashed"/>: còn cửa sổ lỗi Wine (winedbg).</param>
public sealed record MachineProbe(bool? ContainerUp, bool? GameUp, bool Crashed = false);

/// <param name="Label">Chữ trạng thái ngắn ("Rảnh", "Đang đá"…); <paramref name="Detail"/>: 1 dòng giải thích cho người chơi.</param>
/// <param name="MinutesLeft">Ước lượng phút tới khi máy rảnh; null = không ước lượng được.</param>
public sealed record MachineStatus(int No, string Container, MachineState State, string Label, string Detail, int? MinutesLeft = null, Guid? FixtureId = null);

public static class GameMachines
{
    private static readonly ConcurrentDictionary<string, (DateTimeOffset At, MachineProbe Probe)> Cache = new();
    /// <summary>Hỏi docker tối đa 1 lần/15 s mỗi máy (nhiều người mở trang cùng lúc không gọi docker liên tục).</summary>
    public static readonly TimeSpan ProbeTtl = TimeSpan.FromSeconds(15);

    /// <summary>Hỏi docker: container chạy chưa, trong đó PES2017.exe / winedbg còn không. Không đổi gì trên máy.</summary>
    public static async Task<MachineProbe> ProbeAsync(string container, TimeProvider clock, CancellationToken ct = default)
    {
        if (Cache.TryGetValue(container, out var c) && clock.GetUtcNow() - c.At < ProbeTtl) return c.Probe;
        var probe = await ProbeNowAsync(container, ct);
        Cache[container] = (clock.GetUtcNow(), probe);
        return probe;
    }

    private static async Task<MachineProbe> ProbeNowAsync(string container, CancellationToken ct)
    {
        var inspect = await DockerAsync(["inspect", "-f", "{{.State.Running}}", container], ct);
        if (inspect is null) return new(null, null);
        if (inspect.Value.Exit != 0 || inspect.Value.Out.Trim() != "true") return new(false, false);
        var ps = await DockerAsync(["exec", container, "sh", "-c", "pgrep -f 'PES2017.ex[e]' >/dev/null && echo game; pgrep -f 'winedb[g]' >/dev/null && echo crash; true"], ct);
        if (ps is null || ps.Value.Exit != 0) return new(true, null);
        return new(true, ps.Value.Out.Contains("game"), ps.Value.Out.Contains("crash"));
    }

    /// <summary>null = không chạy được docker / không nối được daemon (khác: container không có → Exit ≠ 0).</summary>
    private static async Task<(int Exit, string Out)?> DockerAsync(string[] args, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo("docker", args) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            using var p = Process.Start(psi)!;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            var output = p.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = p.StandardError.ReadToEndAsync(timeout.Token);
            await p.WaitForExitAsync(timeout.Token);
            var err = await error;
            if (err.Contains("Cannot connect to the Docker daemon") || err.Contains("permission denied")) return null;
            return (p.ExitCode, await output);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or OperationCanceledException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Máy trận (việc chạy nền của web, <see cref="MatchJobService"/>): đang có trận → bận tới hết trận; việc web không dùng game (mô phỏng, đóng kỳ…)
    /// → bận ít phút; khoá match.lock mà web không chạy gì → đang dùng ngoài web (thử nghiệm/bảo trì); còn lại rảnh.
    /// </summary>
    /// <param name="externalLock">Nội dung match.lock khi web KHÔNG chạy việc nào (bộ chạy ngoài web); null = không có khoá.</param>
    public static MachineStatus Match(int no, string container, MatchJobService jobs, MachineProbe probe, string? externalLock)
    {
        if (probe.ContainerUp == false)
            return new(no, container, MachineState.Offline, "Tắt", "Máy game đang tắt (bảo trì). Chờ ban tổ chức bật lại.");
        if (jobs.Running)
        {
            var fixture = jobs.CurrentFixtureId ?? jobs.HumanFixture?.FixtureId;
            return jobs.Title switch
            {
                "Trận của tôi" when jobs.Preparing => new(no, container, MachineState.Preparing, "Đang giữ chỗ",
                    "Game đang chờ sẵn cho 1 HLV" + (jobs.PrepareUntil is { } pu ? $", tự trả máy lúc {pu.ToLocalTime():HH:mm} nếu không vào." : "."), null, fixture),
                "Trận của tôi" => new(no, container, MachineState.Playing, "Đang đá", StageText(jobs.Stage, "Trận của HLV"), jobs.MatchMinutesLeft, fixture),
                "Đá các trận BOT" => new(no, container, MachineState.Playing, "Đang đá", StageText(jobs.Stage, "Trận BOT"), jobs.MatchMinutesLeft, fixture),
                _ => new(no, container, MachineState.Busy, "Bận ít phút", $"Web đang xử lý: {jobs.Title}. Game không dùng tới, xong là rảnh."),
            };
        }
        if (externalLock is not null)
            return new(no, container, MachineState.Busy, "Bảo trì", "Ban tổ chức đang dùng máy (thử nghiệm, cài dữ liệu).");
        if (probe.ContainerUp is null)
            return new(no, container, MachineState.Unknown, "Không rõ", "Không hỏi được máy game lúc này. Thử mở trận, máy sẽ tự kiểm tra.");
        return probe.GameUp == true && !probe.Crashed
            ? new(no, container, MachineState.Free, "Rảnh", "Vào trận được ngay: khoảng 4 phút từ lúc bấm tới giao bóng.")
            : new(no, container, MachineState.Free, "Rảnh", "Game đang nghỉ: bấm vào trận, máy tự mở game (thêm khoảng 1 phút).");
    }

    private static string StageText(string? stage, string who) => stage switch
    {
        "boot" => $"{who}: đang mở game",
        "menu" or "warm" => $"{who}: đang vào menu",
        "teams" or "ready" => $"{who}: đang chọn đội",
        "loading" => $"{who}: đang tải trận",
        "kickoff" => $"{who}: đang thi đấu",
        "fulltime" => $"{who}: hết giờ, đang lưu kết quả",
        _ => $"{who}: đang chuẩn bị",
    };

    /// <summary>
    /// Sức chứa 1 máy (số liệu đo trong <see cref="MatchJobService"/>): trận của HLV chiếm máy = mở game tới giao bóng (~4 phút)
    /// + thi đấu (Match Time 10, ước lượng) + lưu kết quả. Khung tối = 19–23 giờ.
    /// </summary>
    public static (double MinutesPerMatch, double PerHour, int PerEvening) Capacity(int secondsToKickoff)
    {
        var minutes = secondsToKickoff / 60.0 + MatchJobService.HumanInPlayMinutes + 0.5;
        return (minutes, 60 / minutes, (int)Math.Floor(4 * 60 / minutes));
    }
}
