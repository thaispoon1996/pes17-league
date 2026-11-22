namespace Pes17League.Clubs.Application;

/// <summary>Chiều cao / cân nặng trung bình (cm, kg), làm tròn 1 chữ số; null = không có giá trị hợp lệ (&gt; 0).</summary>
public sealed record PhysiqueAvg(double? HeightCm, double? WeightKg)
{
    public static PhysiqueAvg Of(IEnumerable<(int? Height, int? Weight)> rows)
    {
        var list = rows as IList<(int? Height, int? Weight)> ?? rows.ToList();
        var hs = list.Select(r => r.Height).Where(v => v is > 0).Select(v => v!.Value).ToList();
        var ws = list.Select(r => r.Weight).Where(v => v is > 0).Select(v => v!.Value).ToList();
        return new(
            hs.Count == 0 ? null : Math.Round(hs.Average(), 1),
            ws.Count == 0 ? null : Math.Round(ws.Average(), 1));
    }
}
