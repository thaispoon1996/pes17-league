using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Pes17League.Clubs.Application;

namespace Pes17League.Web;

/// <summary>
/// Đồ hoạ trong game (PO 23/11: "tạo chỗ để tôi sửa và upload hình từ website và đưa vào game"), trang /admin/do-hoa.
/// Thư mục <see cref="Dir"/> ($DATA/graphics), phục vụ tĩnh ở <see cref="RequestPath"/>:
/// <list type="bullet">
/// <item>bug.{png|webp} + bug.json — logo kênh (góc, cỡ, độ đậm): góc video trang Trực tiếp + in vào đoạn bàn thắng / clip vòng.</item>
/// <item>compe/{giải}.json — bộ menu đã chọn; compe/{giải}-bg.* — ảnh nền tải lên; compe/{giải}.cpk — gói đã dựng
/// (tools/pes17db/compe.py build). Bộ chạy trận cài gói của giải khi trận lên máy (PESMATCH_GRAPHICS, mở lại game nếu khác gói đang cài).</item>
/// <item>packs/{bộ}.png — ảnh nền của bộ menu (compe.py preview, lưu đệm).</item>
/// </list>
/// </summary>
public sealed class GraphicsStore(string dir, string compeScript, string deployDir)
{
    public const string RequestPath = "/img/dohoa";
    public const long MaxBytes = 8 << 20;
    public string Dir { get; } = dir;
    string CompeDir => Path.Combine(Dir, "compe");
    static readonly string[] Exts = [".png", ".jpg", ".webp"];
    static readonly SemaphoreSlim Gate = new(1, 1);   // 1 lần dựng / lấy ảnh mỗi lúc (đọc file game qua docker exec, ~3–8 s)
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    (DateTime At, IReadOnlyList<string> Packs)? packs;

    /// <param name="Corner">tl | tr | bl | br.</param>
    /// <param name="Size">Chiều rộng logo, % chiều rộng khung hình.</param>
    /// <param name="Opacity">Độ đậm, %.</param>
    public sealed record Bug(string Corner = "tr", int Size = 9, int Opacity = 85)
    {
        public static readonly string[] Corners = ["tl", "tr", "bl", "br"];
        public Bug Clamp() => new(Corners.Contains(Corner) ? Corner : "tr", Math.Clamp(Size, 4, 20), Math.Clamp(Opacity, 20, 100));
    }

    /// <param name="Pack">Bộ menu SiderX ("England/Premier League"); null = giao diện gốc của game.</param>
    /// <param name="Bg">Gói có ảnh nền tải lên.</param>
    public sealed record Built(string? Pack, bool Bg, DateTimeOffset At, long Bytes, string Sha);

    // ——— logo kênh ———

    public string? BugFile() => Find(Dir, "bug");
    public string? BugWebPath() => BugFile() is { } f ? $"{RequestPath}/{Path.GetFileName(f)}?v={File.GetLastWriteTimeUtc(f).Ticks}" : null;

    public Bug BugSettings()
    {
        try { return (JsonSerializer.Deserialize<Bug>(File.ReadAllText(Path.Combine(Dir, "bug.json")), Json) ?? new()).Clamp(); }
        catch (Exception e) when (e is IOException or JsonException) { return new(); }
    }

    public void SaveBugSettings(Bug b) => WriteJson(Path.Combine(Dir, "bug.json"), b.Clamp());

    public async Task SaveBugAsync(byte[] data, CancellationToken ct = default)
    {
        var ext = LogoStore.Extension(data);
        if (ext is not (".png" or ".webp")) throw new AdminRuleException("Logo kênh phải là PNG hoặc WebP (nền trong suốt)");
        await Replace(Dir, "bug", ext, data, ct);
    }

    public void ClearBug() => Delete(Dir, "bug");

    // ——— menu theo giải ———

    public string? BgFile(Guid competition) => Find(CompeDir, $"{competition:N}-bg");
    public string? BgWebPath(Guid competition) => BgFile(competition) is { } f ? $"{RequestPath}/compe/{Path.GetFileName(f)}?v={File.GetLastWriteTimeUtc(f).Ticks}" : null;

    public Task SaveBgAsync(Guid competition, byte[] data, CancellationToken ct = default) =>
        Replace(CompeDir, $"{competition:N}-bg", LogoStore.Extension(data) ?? throw new AdminRuleException("Ảnh nền phải là PNG, JPEG hoặc WebP"), data, ct);

    public void ClearBg(Guid competition) => Delete(CompeDir, $"{competition:N}-bg");

    /// <summary>Gói đã dựng của giải (null = chưa có, bộ chạy gỡ gói đang cài khi trận của giải lên máy).</summary>
    public Built? BuiltPack(Guid competition)
    {
        var cpk = Path.Combine(CompeDir, $"{competition:N}.cpk");
        if (!File.Exists(cpk)) return null;
        try { return JsonSerializer.Deserialize<Built>(File.ReadAllText(Path.Combine(CompeDir, $"{competition:N}.json")), Json); }
        catch (Exception e) when (e is IOException or JsonException) { return null; }
    }

    /// <summary>Mã gói đang cài trên máy game ($DATA/pes17-deploy/compe.active, compe.py install); "" = không cài gói nào.</summary>
    public string Installed()
    {
        try { return File.ReadAllText(Path.Combine(deployDir, "compe.active")).Trim(); }
        catch (IOException) { return ""; }
    }

    /// <summary>Gỡ gói của giải (giữ ảnh nền tải lên): trận sau của giải trên máy game dùng lại giao diện gốc.</summary>
    public void Remove(Guid competition)
    {
        if (!Directory.Exists(CompeDir)) return;
        File.Delete(Path.Combine(CompeDir, $"{competition:N}.cpk"));
        File.Delete(Path.Combine(CompeDir, $"{competition:N}.json"));
    }

    /// <summary>Các bộ menu SiderX trên máy game (lưu đệm 10 phút).</summary>
    public async Task<IReadOnlyList<string>> PacksAsync(CancellationToken ct = default)
    {
        if (packs is { } p && DateTime.UtcNow - p.At < TimeSpan.FromMinutes(10)) return p.Packs;
        var list = JsonSerializer.Deserialize<List<string>>(await RunAsync(["packs"], ct)) ?? [];
        packs = (DateTime.UtcNow, list);
        return list;
    }

    /// <summary>Ảnh nền đang có của bộ (null = giao diện gốc của game) — lấy từ máy game lần đầu (~5–8 s) rồi lưu đệm.</summary>
    public async Task<string?> PackBgAsync(string? pack, CancellationToken ct = default)
    {
        var name = pack is null ? "goc" : Slug(pack);
        var file = Path.Combine(Dir, "packs", name + ".png");
        if (!File.Exists(file))
        {
            var tmp = Directory.CreateTempSubdirectory("pes17-gfx-");
            try
            {
                var res = JsonDocument.Parse(await RunAsync(["preview", pack ?? "-", tmp.FullName], ct));
                if (res.RootElement.GetProperty("bg").ValueKind == JsonValueKind.Null) return null;
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.Move(Path.Combine(tmp.FullName, "bg.png"), file, true);
            }
            finally { tmp.Delete(true); }
        }
        return $"{RequestPath}/packs/{name}.png";
    }

    /// <summary>Dựng gói của giải: bộ menu (hoặc giao diện gốc) + ảnh nền tải lên (nếu có). Trận sau của giải trên máy game mới hiện.</summary>
    public async Task<Built> BuildAsync(Guid competition, string? pack, CancellationToken ct = default)
    {
        Directory.CreateDirectory(CompeDir);
        var bg = BgFile(competition);
        if (pack is null && bg is null) throw new AdminRuleException("Chọn bộ menu hoặc tải ảnh nền trước khi dựng gói");
        var spec = Path.Combine(CompeDir, $"{competition:N}.spec.json");
        await File.WriteAllTextAsync(spec, JsonSerializer.Serialize(new { pack, bg }), ct);
        var cpk = Path.Combine(CompeDir, $"{competition:N}.cpk");
        try { await RunAsync(["build", spec, cpk], ct); }
        finally { File.Delete(spec); }
        string sha;
        await using (var s = File.OpenRead(cpk)) sha = Convert.ToHexString(await SHA256.HashDataAsync(s, ct))[..16].ToLowerInvariant();   // = compe.py install
        var built = new Built(pack, bg is not null, DateTimeOffset.UtcNow, new FileInfo(cpk).Length, sha);
        WriteJson(Path.Combine(CompeDir, $"{competition:N}.json"), built);
        return built;
    }

    async Task<string> RunAsync(string[] args, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            var psi = new ProcessStartInfo("python3") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            psi.ArgumentList.Add(compeScript);
            foreach (var a in args) psi.ArgumentList.Add(a);
            psi.Environment["PES17_DATA"] = Path.GetDirectoryName(deployDir)!;
            using var p = Process.Start(psi)!;
            var output = p.StandardOutput.ReadToEndAsync(ct);
            var error = p.StandardError.ReadToEndAsync(ct);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMinutes(3));
            try { await p.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { p.Kill(true); throw new AdminRuleException("Máy game không trả lời (quá 3 phút)"); }
            if (p.ExitCode != 0)
            {
                var err = (await error).Trim();
                throw new AdminRuleException(err.StartsWith("LỖI ") ? err[4..] : $"Lỗi đọc file game: {err[^Math.Min(err.Length, 200)..]}");
            }
            return (await output).Trim();
        }
        finally { Gate.Release(); }
    }

    static string Slug(string pack) => string.Concat(pack.Select(c => char.IsAsciiLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-'));

    static string? Find(string dir, string stem) =>
        Exts.Select(e => Path.Combine(dir, stem + e)).FirstOrDefault(File.Exists);

    static async Task Replace(string dir, string stem, string ext, byte[] data, CancellationToken ct)
    {
        if (data.Length > MaxBytes) throw new AdminRuleException("Ảnh tối đa 8 MB");
        Directory.CreateDirectory(dir);
        var tmp = Path.Combine(dir, $"{stem}{ext}.tmp");
        await File.WriteAllBytesAsync(tmp, data, ct);
        Delete(dir, stem);
        File.Move(tmp, Path.Combine(dir, stem + ext), true);
    }

    static void Delete(string dir, string stem)
    {
        if (!Directory.Exists(dir)) return;
        foreach (var e in Exts) File.Delete(Path.Combine(dir, stem + e));
    }

    static void WriteJson(string path, object value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(value, value.GetType(), Json));
        File.Move(path + ".tmp", path, true);
    }
}
