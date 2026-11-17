using Pes17League.Clubs.Application;

namespace Pes17League.Web;

/// <summary>Logo admin tải lên (bước 3b): lưu ở <see cref="Dir"/> ($DATA/logos, ngoài thư mục publish), phục vụ tĩnh ở <see cref="RequestPath"/>.</summary>
public sealed record LogoStore(string Dir)
{
    public const string RequestPath = "/img/upload";
    public const long MaxBytes = 1 << 20;

    /// <summary>Đuôi file theo byte đầu (không tin tên/kiểu trình duyệt gửi, không nhận SVG); null = không phải PNG/JPEG/WebP.</summary>
    public static string? Extension(ReadOnlySpan<byte> b) =>
        b.StartsWith((byte[])[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]) ? ".png"
        : b.StartsWith((byte[])[0xFF, 0xD8, 0xFF]) ? ".jpg"
        : b.Length >= 12 && b[..4].SequenceEqual("RIFF"u8) && b[8..12].SequenceEqual("WEBP"u8) ? ".webp"
        : null;

    /// <summary>Lưu file mới (tên ngẫu nhiên → trình duyệt không giữ ảnh cũ), trả đường dẫn web.</summary>
    public async Task<string> SaveAsync(string prefix, byte[] data, CancellationToken ct = default)
    {
        var ext = Extension(data) ?? throw new AdminRuleException("Logo phải là ảnh PNG, JPEG hoặc WebP");
        Directory.CreateDirectory(Dir);
        var file = $"{prefix}-{Guid.NewGuid().ToString("N")[..8]}{ext}";
        await File.WriteAllBytesAsync(Path.Combine(Dir, file), data, ct);
        return $"{RequestPath}/{file}";
    }
}
