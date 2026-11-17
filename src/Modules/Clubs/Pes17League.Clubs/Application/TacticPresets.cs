using Pes17League.OptionFile;

namespace Pes17League.Clubs.Application;

/// <summary>FM đợt 10: phong cách chiến thuật dựng sẵn — 1 chạm đặt sơ đồ + 4 thiết lập A/B/C + 4 chiến thuật L2.
/// Hàng thủ A = dâng cao, B = lùi sâu; Bẫy việt vị A = bẫy, C = không bẫy (menu game 11/11). Pressure/CounterAttack không hiện trong menu PES6.
/// L2 chỉ số theo <see cref="TeamStrategies.Names"/>: 1 trung lộ, 5 đảo cánh, 7 pressing, 8 phòng ngự phản công, 9 bẫy việt vị.</summary>
public static class TacticPresets
{
    public sealed record Preset(string Key, string Name, string Note, TeamTactics Tactics);

    public static readonly IReadOnlyList<Preset> All =
    [
        new("tan-cong", "Tấn công", "4-3-3, dâng cao, không bẫy việt vị",
            new("4-3-3 A", BackLine: 0, Pressure: 2, OffsideTrap: 2, CounterAttack: 2, Strategies: [1, 5, 7, 8])),
        new("can-bang", "Cân bằng", "4-4-2, đội hình giữa",
            new("4-4-2 A", BackLine: 1, Pressure: 1, OffsideTrap: 1, CounterAttack: 1, Strategies: [1, 5, 7, 8])),
        new("phan-cong", "Phòng ngự phản công", "4-5-1, lùi sâu, chờ phản công",
            new("4-5-1 B", BackLine: 1, Pressure: 0, OffsideTrap: 2, CounterAttack: 2, Strategies: [8, 1, 5, 7])),
        new("pressing", "Pressing tầm cao", "4-3-3, dâng cao, bẫy việt vị",
            new("4-3-3 B", BackLine: 0, Pressure: 2, OffsideTrap: 0, CounterAttack: 0, Strategies: [7, 9, 1, 5])),
    ];

    /// <summary>Áp phong cách: sơ đồ + thiết lập (xoá vị trí tự do khi đổi sơ đồ) + 4 L2. Đội trưởng/đá phạt giữ nguyên.</summary>
    public static async Task<TacticsError?> ApplyAsync(TacticsService svc, Guid clubId, string key, CancellationToken ct = default)
    {
        var p = All.Single(x => x.Key == key).Tactics;
        if (await svc.SetAsync(clubId, p, ct) is { } err) return err;
        if (await svc.SetPositionsAsync(clubId, null, ct) is { } pe) return pe;
        for (var b = 0; b < 4; b++)
            if (await svc.SetStrategyAsync(clubId, b, p.Strategies![b], ct) is { } e) return e;
        return null;
    }

    /// <summary>Phong cách đang khớp với chiến thuật hiện tại (null = tự chỉnh).</summary>
    public static Preset? Match(ClubTactics? t) => t is null ? null : All.FirstOrDefault(p =>
        p.Tactics.Formation == t.Formation && p.Tactics.BackLine == t.BackLine && p.Tactics.OffsideTrap == t.OffsideTrap
        && p.Tactics.Pressure == t.Pressure && p.Tactics.CounterAttack == t.CounterAttack
        && (TacticsService.StrategiesOf(t) ?? []).SequenceEqual(p.Tactics.Strategies!) && TacticsService.PositionsOf(t) is null);
}
