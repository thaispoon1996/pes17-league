using System.Buffers.Binary;

namespace Pes17League.OptionFile;

/// <summary>
/// Giải mã / mã hoá option file PES6 PC (KONAMI-WIN32PES6OPT).
/// Dữ kiện định dạng (khoá, vị trí khối, cách tính checksum) lấy từ PES-Editor-6 (GPL-3.0),
/// xác minh độc lập bằng oracle — xem docs/of-module-design.md mục 2. Mã ở đây viết mới.
/// </summary>
public static class OptionFileCodec
{
    public const int Length = 1_191_936;

    internal static readonly int[] BlockStart = [12, 5144, 9544, 14288, 37116, 657956, 751472, 763804, 911144, 1170520];
    internal static readonly int[] BlockSize = [4844, 1268, 4730, 22816, 620000, 93501, 12320, 147328, 259364, 21032];

    private const uint Mask = 0x7AB3684C;
    private static readonly (uint[] Key, byte[] KeyPc) Keys = LoadKeys();

    public static OptionFileImage Decode(ReadOnlySpan<byte> file)
    {
        if (file.Length != Length)
            throw new InvalidDataException($"Option file phải dài {Length} byte, nhận {file.Length}.");
        var data = file.ToArray();
        XorPc(data);
        for (var b = 1; b < 10; b++) Transform(data, b, decrypt: true);
        return new OptionFileImage(data);
    }

    public static byte[] Encode(OptionFileImage image)
    {
        var data = image.Data.ToArray();
        for (var b = 1; b < 10; b++) Transform(data, b, decrypt: false);
        // checksum tính trên dữ liệu đã mã hoá, trước bước XOR keyPC
        for (var b = 0; b < 10; b++)
        {
            uint sum = 0;
            foreach (var w in Words(data, b)) sum += w;
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(BlockStart[b] - 8), sum);
        }
        XorPc(data);
        return data;
    }

    // Khối 1–9 mã hoá theo từng word 32-bit LE; byte lẻ cuối khối (nếu có) giữ nguyên.
    private static void Transform(byte[] data, int block, bool decrypt)
    {
        var span = data.AsSpan(BlockStart[block], BlockSize[block] / 4 * 4);
        for (var j = 0; j * 4 < span.Length; j++)
        {
            var w = BinaryPrimitives.ReadUInt32LittleEndian(span[(j * 4)..]);
            var k = Keys.Key[j % Keys.Key.Length];
            w = decrypt ? ((w - k) + Mask) ^ Mask : k + ((w ^ Mask) - Mask);
            BinaryPrimitives.WriteUInt32LittleEndian(span[(j * 4)..], w);
        }
    }

    private static IEnumerable<uint> Words(byte[] data, int block)
    {
        for (var j = 0; j < BlockSize[block] / 4; j++)
            yield return BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(BlockStart[block] + j * 4));
    }

    private static void XorPc(byte[] data)
    {
        for (var i = 0; i < data.Length; i++) data[i] ^= Keys.KeyPc[i % 256];
    }

    private static (uint[], byte[]) LoadKeys()
    {
        using var s = typeof(OptionFileCodec).Assembly.GetManifestResourceStream("Pes17League.OptionFile.keys.bin")
            ?? throw new InvalidOperationException("Thiếu resource keys.bin");
        var raw = new byte[446 * 4 + 256];
        s.ReadExactly(raw);
        var key = new uint[446];
        for (var i = 0; i < key.Length; i++) key[i] = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(i * 4));
        return (key, raw[(446 * 4)..]);
    }
}
