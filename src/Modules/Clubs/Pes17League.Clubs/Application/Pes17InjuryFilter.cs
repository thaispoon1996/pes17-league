using Pes17League.Clubs.Domain;

namespace Pes17League.Clubs.Application;

/// <summary>
/// PO 27/11 (chọn "A"): cầu thủ đang chấn thương (<see cref="PlayerCondition.InjuredRounds"/> &gt; 0) không vào game được —
/// như Master League. Áp khi xuất đội hình (<see cref="Pes17SquadExportService"/>), không đổi đội hình trên web:
/// <list type="bullet">
/// <item>Đá chính (ô 0–10) bị chấn thương → người dự bị khoẻ đầu tiên (theo thứ tự ô) cùng vị trí, không có thì cùng tuyến,
/// không có nữa thì bất kỳ cầu thủ ngoài sân khoẻ (thủ môn chỉ thay bằng thủ môn). Người thay vào đúng ô đó (giữ sơ đồ).</item>
/// <item>Cầu thủ chấn thương còn lại bị gỡ khỏi danh sách đội trong game (không chọn được kể cả dự bị).</item>
/// <item>Đội không được còn dưới <see cref="MinSquad"/> người: thiếu thì giữ lại người chấn thương ở cuối danh sách (dự bị cuối).
/// Không có thủ môn khoẻ → giữ thủ môn chấn thương ở ô 0.</item>
/// </list>
/// </summary>
public static class Pes17InjuryFilter
{
    public const int MinSquad = 16;

    public sealed record Entry(Guid PlayerId, Position Position);

    static int Line(Position p) => p switch
    {
        Position.GK => 0,
        Position.SW or Position.CB or Position.SB or Position.WB => 1,
        Position.DMF or Position.CMF or Position.SMF or Position.AMF => 2,
        _ => 3,
    };

    /// <param name="squad">Cầu thủ theo thứ tự ô (ô 0 = thủ môn, 0–10 đá chính).</param>
    /// <returns>Danh sách mới theo thứ tự ô; <c>Removed</c> = người bị gỡ khỏi game.</returns>
    public static (List<Entry> Squad, List<Guid> Removed) Apply(IReadOnlyList<Entry> squad, Func<Guid, bool> injured)
    {
        var list = squad.ToList();
        if (!list.Any(e => injured(e.PlayerId))) return (list, []);
        var xi = Math.Min(11, list.Count);
        for (var i = 0; i < xi; i++)
        {
            var cur = list[i];
            if (!injured(cur.PlayerId)) continue;
            var bench = Enumerable.Range(xi, list.Count - xi).Where(j => !injured(list[j].PlayerId)).ToList();
            int? pick = cur.Position == Position.GK
                ? First(bench, j => list[j].Position == Position.GK)
                : First(bench, j => list[j].Position == cur.Position)
                  ?? First(bench, j => Line(list[j].Position) == Line(cur.Position))
                  ?? First(bench, j => list[j].Position != Position.GK);
            if (pick is { } p) (list[i], list[p]) = (list[p], list[i]);
        }
        // ô đá chính chấn thương không ai thay được thì vẫn giữ (vd. không còn thủ môn khoẻ)
        var stay = list.Where((e, i) => i < xi || !injured(e.PlayerId)).Count();
        var keep = Math.Max(0, MinSquad - stay);   // số người chấn thương phải giữ lại (cuối danh sách) cho đủ quân số
        var outList = new List<Entry>();
        var tail = new List<Entry>();
        var removed = new List<Guid>();
        for (var i = 0; i < list.Count; i++)
        {
            var e = list[i];
            if (i < xi || !injured(e.PlayerId)) { outList.Add(e); continue; }
            if (keep > 0) { tail.Add(e); keep--; }
            else removed.Add(e.PlayerId);
        }
        return ([.. outList, .. tail], removed);
    }

    static int? First(List<int> idx, Func<int, bool> ok)
    {
        foreach (var j in idx) if (ok(j)) return j;
        return null;
    }
}
