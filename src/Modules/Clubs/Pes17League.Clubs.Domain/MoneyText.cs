using System.Globalization;

namespace Pes17League.Clubs.Domain;

/// <summary>Cách ghi tiền thống nhất (PO 20/11): biểu tượng €, triệu = M, nghìn = K (tỷ = B), dấu chấm thập phân.
/// 8.150.000 → "€8.15M"; 35.500 → "€35.5K"; 1.250.000.000 → "€1.25B"; 500 → "€500"; âm → "-€1.2M".
/// Dùng cho web (Fmt.Money) lẫn chữ tự sinh (thư, tin, hòm thư); thư đã lưu trước đổi giữ cách ghi cũ.</summary>
public static class MoneyText
{
    static readonly (double Unit, string Suffix, int Decimals)[] Units = [(1e3, "K", 1), (1e6, "M", 2), (1e9, "B", 2)];

    public static string Format(long v)
    {
        var a = Math.Abs((double)v);
        var body = a.ToString("0", CultureInfo.InvariantCulture);
        if (a >= 1000)
            foreach (var (unit, suffix, decimals) in Units)
            {
                var x = Math.Round(a / unit, decimals, MidpointRounding.AwayFromZero);
                if (x < 1000 || suffix == "B")   // làm tròn chạm 1000 thì lên bậc trên: 999.999 → €1M, không ghi €1000K
                {
                    body = x.ToString(decimals == 1 ? "0.#" : "0.##", CultureInfo.InvariantCulture) + suffix;
                    break;
                }
            }
        return (v < 0 ? "-€" : "€") + body;
    }
}
