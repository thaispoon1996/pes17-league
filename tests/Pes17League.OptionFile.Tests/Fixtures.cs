namespace Pes17League.OptionFile.Tests;

/// <summary>
/// File OF thật (patch PES6VN, dữ liệu Konami) không commit lên repo. Đặt biến PES_OF_FIXTURES trỏ tới thư mục chứa:
/// orig.OPT (gốc), atk36.OPT / work3-5.OPT (do PES Editor ghi), stats.csv (editor xuất từ atk36),
/// spike-ingame.OPT / transfer-ingame.OPT (đã kiểm trong game). Thiếu thì test bị skip.
/// </summary>
public static class Fixtures
{
    public static string? Dir => Environment.GetEnvironmentVariable("PES_OF_FIXTURES") is { } d && Directory.Exists(d) ? d : null;

    public static string Path(string name) => System.IO.Path.Combine(Dir ?? throw new InvalidOperationException("PES_OF_FIXTURES"), name);
    public static byte[] Bytes(string name) => File.ReadAllBytes(Path(name));
    public static OptionFileImage Image(string name) => OptionFileCodec.Decode(Bytes(name));
}

public sealed class FixtureFactAttribute : FactAttribute
{
    public FixtureFactAttribute()
    {
        if (Fixtures.Dir is null) Skip = "Không có PES_OF_FIXTURES";
    }
}
