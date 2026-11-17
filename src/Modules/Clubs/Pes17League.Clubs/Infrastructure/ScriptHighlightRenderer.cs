using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Pes17League.Clubs.Application;

namespace Pes17League.Clubs.Infrastructure;

/// <summary>
/// Highlight bằng tools/match/highlight.py (ffmpeg trong image pes17league-rec:local): <c>highlight.py RECORDING OUT.mp4 OUT_DOC.mp4 &lt; clips.json</c>
/// → {outDir}/{fixture:N}.mp4 (16:9), {fixture:N}-doc.mp4 (9:16), {fixture:N}.jpg (thumbnail gần bàn đầu).
/// Xong thì xoá bản ghi cả trận quá <paramref name="keepDays"/> ngày (C7: giữ 7 ngày).
/// </summary>
public sealed class ScriptHighlightRenderer(string scriptPath, string outDir, int keepDays = 7) : IHighlightRenderer
{
    const string Image = "pes17league-rec:local";

    public static string PathFor(string outDir, Guid fixtureId, bool vertical = false) =>
        Path.Combine(outDir, vertical ? $"{fixtureId:N}-doc.mp4" : $"{fixtureId:N}.mp4");

    public static string PosterPathFor(string outDir, Guid fixtureId) => Path.Combine(outDir, $"{fixtureId:N}.jpg");

    public async Task RenderAsync(Guid fixtureId, string recordingPath, IReadOnlyList<HighlightClip> clips, CancellationToken ct)
    {
        Directory.CreateDirectory(outDir);
        var psi = new ProcessStartInfo("python3") { RedirectStandardInput = true, RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
        var dir = Path.GetFullPath(outDir);
        foreach (var a in new[] { scriptPath, recordingPath, PathFor(dir, fixtureId), PathFor(dir, fixtureId, vertical: true) }) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        await p.StandardInput.WriteAsync(JsonSerializer.Serialize(clips.Select(c => new { t = c.VideoOffsetSec, caption = c.Caption, pre = c.Pre, post = c.Post })));
        p.StandardInput.Close();
        var err = p.StandardError.ReadToEndAsync(ct);
        await p.StandardOutput.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        if (p.ExitCode != 0) throw new InvalidOperationException($"highlight.py: {(await err).Trim()}");

        var at = clips.Count > 0 ? Math.Max(0.5, clips[0].Pre + 2) : 2.0;   // ~2 s sau bàn đầu trong video highlight
        await GrabPosterAsync(dir, fixtureId, at, ct);

        foreach (var f in new DirectoryInfo(Path.GetDirectoryName(recordingPath)!).EnumerateFiles("*.mkv"))
            if (f.LastWriteTimeUtc < DateTime.UtcNow.AddDays(-keepDays)) f.Delete();
    }

    /// <summary>Lấy 1 khung từ highlight 16:9 đã cắt. Lỗi ffmpeg bỏ qua — trang dùng ảnh tin dự phòng.</summary>
    public static async Task GrabPosterAsync(string outDir, Guid fixtureId, double atSec, CancellationToken ct)
    {
        var mp4 = PathFor(outDir, fixtureId);
        if (!File.Exists(mp4)) return;
        var jpgName = Path.GetFileName(PosterPathFor(outDir, fixtureId));
        var psi = new ProcessStartInfo("docker") { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
        foreach (var a in new[]
        {
            "run", "--rm", "-v", $"{Path.GetFullPath(outDir)}:/w", "-u", UidGid(outDir),
            Image, "ffmpeg", "-loglevel", "error", "-ss", atSec.ToString("0.###", CultureInfo.InvariantCulture),
            "-i", $"/w/{Path.GetFileName(mp4)}", "-frames:v", "1", "-q:v", "3", "-y", $"/w/{jpgName}",
        }) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        await p.WaitForExitAsync(ct);
    }

    static string UidGid(string dir)
    {
        try
        {
            var psi = new ProcessStartInfo("stat", $"-c %u:%g {dir}") { RedirectStandardOutput = true, UseShellExecute = false };
            using var p = Process.Start(psi)!;
            var s = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit();
            return p.ExitCode == 0 && s.Contains(':') ? s : "1000:1000";
        }
        catch { return "1000:1000"; }
    }
}
