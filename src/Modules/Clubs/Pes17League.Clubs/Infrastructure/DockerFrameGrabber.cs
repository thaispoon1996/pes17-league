using System.Diagnostics;
using System.Globalization;
using Pes17League.Clubs.Application;

namespace Pes17League.Clubs.Infrastructure;

/// <summary>
/// Cắt jpg bằng ffmpeg trong image <see cref="Image"/> (cùng image ghi hình / highlight).
/// Một khung lỗi thì thử mốc dự phòng; vẫn lỗi thì bỏ khung đó.
/// </summary>
public sealed class DockerFrameGrabber : IFrameGrabber
{
    public const string Image = "pes17league-rec:local";

    public async Task GrabAsync(string recordingPath, IReadOnlyList<FrameGrab> frames, CancellationToken ct)
    {
        var rec = Path.GetFullPath(recordingPath);
        var recDir = Path.GetDirectoryName(rec)!;
        var recName = Path.GetFileName(rec);
        foreach (var f in frames)
        {
            ct.ThrowIfCancellationRequested();
            var dest = Path.GetFullPath(f.DestPath);
            var outDir = Path.GetDirectoryName(dest)!;
            Directory.CreateDirectory(outDir);
            var name = Path.GetFileName(dest);
            if (!await RunAsync(recDir, recName, outDir, name, f.AtSec, ct) && f.FallbackSec is { } fb)
                await RunAsync(recDir, recName, outDir, name, fb, ct);
        }
    }

    private static async Task<bool> RunAsync(string recDir, string recName, string outDir, string outName, double at, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("docker") { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
        foreach (var a in new[]
        {
            "run", "--rm", "-v", $"{recDir}:/in:ro", "-v", $"{outDir}:/out", Image,
            "ffmpeg", "-y", "-ss", at.ToString("0.##", CultureInfo.InvariantCulture), "-i", $"/in/{recName}",
            "-frames:v", "1", "-an", "-q:v", "3", $"/out/{outName}",
        }) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync(ct);
        var stderr = p.StandardError.ReadToEndAsync(ct);
        await Task.WhenAll(stdout, stderr);
        await p.WaitForExitAsync(ct);
        return p.ExitCode == 0 && new FileInfo(Path.Combine(outDir, outName)).Length > 0;
    }
}
