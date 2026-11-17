using System.Security.Cryptography;

namespace Pes17League.Clubs.Application;

/// <summary>Kho file OF (OF gốc mỗi patch, OF sinh mỗi vòng). DB chỉ lưu đường dẫn + sha256.</summary>
public interface IOptionFileStore
{
    /// <summary>Lưu file, trả đường dẫn tương đối dùng để đọc lại.</summary>
    Task<string> SaveAsync(string name, byte[] data, CancellationToken ct = default);
    Task<byte[]> ReadAsync(string path, CancellationToken ct = default);
}

public sealed class FileSystemOptionFileStore(string root) : IOptionFileStore
{
    public async Task<string> SaveAsync(string name, byte[] data, CancellationToken ct = default)
    {
        if (name.Contains("..") || Path.IsPathRooted(name)) throw new ArgumentException("Tên file không hợp lệ", nameof(name));
        var full = Path.Combine(root, name);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var tmp = full + ".tmp";
        await File.WriteAllBytesAsync(tmp, data, ct);
        File.Move(tmp, full, overwrite: true);   // ghi nguyên tử: container không bao giờ thấy file dở
        return name;
    }

    public Task<byte[]> ReadAsync(string path, CancellationToken ct = default) =>
        File.ReadAllBytesAsync(Path.Combine(root, path), ct);
}

internal static class Hash
{
    public static string Sha256(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
}
