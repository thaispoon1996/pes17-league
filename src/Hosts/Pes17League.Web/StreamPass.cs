using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace Pes17League.Web;

/// <summary>Vé vào máy giao hữu (PO 17/11): selkies chế độ token (SELKIES_MASTER_TOKEN). Mỗi trận 2 token mới —
/// đội nhà = controller (màn chính, bàn phím/cảm ứng), đội khách = viewer ô tay cầm 2 (PES17, 18/11) (chỉ gửi tay cầm, không chiếm màn chính
/// nên 2 trình duyệt không đá nhau). Cấp khi trận bắt đầu tải (stage loading) — trước đó máy đang mở game/menu, người chơi không cần thấy. Hết trận thu hồi → link cũ hết hiệu lực. Không có token thì không vào được máy.
/// Token đẩy vào container qua <c>pes-tokens.py</c> (đọc master token trong container, web không giữ).</summary>
public sealed class StreamPass(FriendlyMachineOptions machine, ILogger<StreamPass> log)
{
    private readonly ConcurrentDictionary<Guid, (string Home, string Away)> _tokens = new();

    /// <summary>Link chơi của 1 bên; null = máy chưa cấp vé cho trận này.</summary>
    public string? Link(Guid matchId, bool home) =>
        machine.PlayUrl.Length > 0 && _tokens.TryGetValue(matchId, out var t)
            ? $"{machine.PlayUrl}?token={(home ? t.Home : t.Away)}{(home ? "" : "#player2")}"
            : null;

    /// <summary>Máy chế độ token (có tên container).</summary>
    public bool Enabled => machine.Container.Length > 0;

    public bool Issued(Guid matchId) => _tokens.ContainsKey(matchId) || _opening.ContainsKey(matchId);
    private readonly ConcurrentDictionary<Guid, byte> _opening = new();

    public async Task OpenAsync(Guid matchId, CancellationToken ct)
    {
        if (!_opening.TryAdd(matchId, 0)) return;   // stage loading + kickoff đến sát nhau → chỉ cấp 1 lần
        try { await OpenCoreAsync(matchId, ct); }
        finally { _opening.TryRemove(matchId, out _); }
    }

    private async Task OpenCoreAsync(Guid matchId, CancellationToken ct)
    {
        var t = (Home: New(), Away: New());
        await PostAsync(new { home = t.Home, away = t.Away }, ct);
        _tokens[matchId] = t;
    }

    /// <summary>Thu hồi mọi vé (chỉ giữ vé tay cầm ảo của máy). Lỗi chỉ ghi log.</summary>
    public async Task CloseAsync(Guid? matchId = null)
    {
        if (matchId is { } id) _tokens.TryRemove(id, out _);
        try { await PostAsync(new { }, CancellationToken.None); }
        catch (Exception e) { log.LogWarning(e, "Thu hồi vé máy giao hữu lỗi"); }
    }

    private static string New() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    private async Task PostAsync(object body, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("docker", ["exec", "-i", machine.Container, "/lsiopy/bin/python3", "/usr/local/bin/pes-tokens.py"])
        { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Không chạy được docker");
        await p.StandardInput.WriteAsync(JsonSerializer.Serialize(body));
        p.StandardInput.Close();
        var err = p.StandardError.ReadToEndAsync(ct);
        await p.StandardOutput.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        var e = (await err).Trim();
        if (p.ExitCode != 0) throw new InvalidOperationException($"Cấp vé máy giao hữu lỗi: {(e.Length > 200 ? e[^200..] : e)}");
    }
}
