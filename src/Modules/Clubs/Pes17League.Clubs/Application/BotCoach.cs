using Pes17League.OptionFile;

namespace Pes17League.Clubs.Application;

/// <summary>Đầu vào HLV BOT: cầu thủ đang trong squad kèm OVR mùa.</summary>
public sealed record BotPlayer(Guid PlayerId, Position Position, int Overall);

/// <summary>Kế hoạch HLV BOT v1 cho 1 CLB (không DB) — xem <c>~/pes17-ops/docs/hlv-bot-v1.md</c>.</summary>
public sealed record BotPlan(string Formation, IReadOnlyList<Guid> StarterOrder, IReadOnlyList<Guid> BenchOrder, TeamTactics Tactics);

/// <summary>
/// HLV BOT v1 thuần: chọn sơ đồ + 11 người + thiết lập tấn công + L2 từ squad.
/// Hồ sơ thiết lập theo spike 11/11 (A/B/C đã xác minh): hàng thủ A, không bẫy C; Pressure/CA = 2.
/// </summary>
public static class BotCoach
{
    static readonly string[] AttackFormations = ["4-3-3 A", "4-4-2 A", "3-4-3 A", "3-5-2 A", "4-3-3 B"];
    const string FallbackFormation = "4-5-1 B";
    const int SoftPenalty = 5;

    public static BotPlan Plan(IReadOnlyList<BotPlayer> squad)
    {
        if (squad.Count == 0) throw new ArgumentException("Squad trống.", nameof(squad));
        var candidates = AttackFormations.ToList();
        if (squad.Count(p => p.Position is Position.CF or Position.SS or Position.WF) < 2)
            candidates.Add(FallbackFormation);

        BotPlan? best = null;
        var bestScore = int.MinValue;
        var bestForwards = -1;
        foreach (var name in candidates)
        {
            var roles = Formations.Roles(name);
            var (starters, score) = Assign(squad, roles);
            if (starters is null) continue;
            var forwards = roles.Count(IsForwardRole);
            if (score > bestScore || (score == bestScore && forwards > bestForwards))
            {
                bestScore = score;
                bestForwards = forwards;
                var starterIds = starters.Select(p => p.PlayerId).ToList();
                var used = starterIds.ToHashSet();
                var bench = BenchOrder(squad, used);
                best = new BotPlan(name, starterIds, bench, TacticsFor(name, starters, roles));
            }
        }
        if (best is null)
        {
            // Fallback thô: OVR cao nhất, GK vào ô 0.
            var gk = squad.Where(p => p.Position == Position.GK).OrderByDescending(p => p.Overall).FirstOrDefault()
                     ?? squad.OrderByDescending(p => p.Overall).First();
            var rest = squad.Where(p => p.PlayerId != gk.PlayerId).OrderByDescending(p => p.Overall).Take(10).ToList();
            var xi = new[] { gk }.Concat(rest).ToList();
            while (xi.Count < 11) xi.Add(gk);
            var roles = Formations.Roles("4-3-3 A");
            var used = xi.Take(11).Select(p => p.PlayerId).ToHashSet();
            return new BotPlan("4-3-3 A", xi.Take(11).Select(p => p.PlayerId).ToList(), BenchOrder(squad, used),
                TacticsFor("4-3-3 A", xi.Take(11).ToList(), roles));
        }
        return best;
    }

    static IReadOnlyList<Guid> BenchOrder(IReadOnlyList<BotPlayer> squad, HashSet<Guid> used)
    {
        var left = squad.Where(p => !used.Contains(p.PlayerId)).OrderByDescending(p => p.Overall).ToList();
        // Giữ ≥1 GK dự bị nếu còn GK ngoài XI.
        var gkBench = left.Where(p => p.Position == Position.GK).ToList();
        if (gkBench.Count == 0) return left.Select(p => p.PlayerId).ToList();
        var firstGk = gkBench[0];
        return left.Where(p => p.PlayerId != firstGk.PlayerId).Prepend(firstGk).Select(p => p.PlayerId).ToList();
    }

    static TeamTactics TacticsFor(string formation, IReadOnlyList<BotPlayer> starters, IReadOnlyList<string> roles)
    {
        var center = 0;
        var left = 0;
        var right = 0;
        for (var i = 0; i < starters.Count && i < roles.Count; i++)
        {
            var o = starters[i].Overall;
            switch (roles[i])
            {
                case "CF" or "AM" or "SS": center += o; break;
                case "LW" or "LM" or "LWB" or "LB": left += o; break;
                case "RW" or "RM" or "RWB" or "RB": right += o; break;
            }
        }
        int circle, cross;
        if (center >= left && center >= right) { circle = 1; cross = right >= left ? 2 : 3; } // trung lộ + cánh mạnh hơn
        else if (left >= right) { circle = 3; cross = 1; }
        else { circle = 2; cross = 1; }
        return new TeamTactics(formation, BackLine: 0, Pressure: 2, OffsideTrap: 2, CounterAttack: 2,
            Positions: null, Strategies: [circle, cross, 7, 8]);
    }

    static bool IsForwardRole(string role) => role is "CF" or "SS" or "LW" or "RW";

    /// <summary>Gán 11 ô: điểm = OVR (−5 nếu tạm được); không hợp → không chọn sơ đồ này nếu thiếu người.</summary>
    static (List<BotPlayer>? Starters, int Score) Assign(IReadOnlyList<BotPlayer> squad, IReadOnlyList<string> roles)
    {
        var n = squad.Count;
        if (n < 11) return (null, int.MinValue);
        // Ma trận N×N: cột 0..10 = ô; cột 11..N-1 = ghế dự bị (điểm 0).
        var score = new int[n, n];
        for (var i = 0; i < n; i++)
        for (var j = 0; j < n; j++)
        {
            if (j >= 11) { score[i, j] = 0; continue; }
            var fit = FitScore(squad[i], roles[j]);
            score[i, j] = fit ?? -1_000_000;
        }
        var assign = HungarianMaximize(score);
        var starters = new BotPlayer[11];
        var total = 0;
        for (var i = 0; i < n; i++)
        {
            var j = assign[i];
            if (j is < 0 or >= 11) continue;
            if (score[i, j] <= -1_000_000) return (null, int.MinValue);
            starters[j] = squad[i];
            total += score[i, j];
        }
        if (starters.Any(p => p is null)) return (null, int.MinValue);
        return ([.. starters], total);
    }

    static int? FitScore(BotPlayer p, string role)
    {
        var (exact, soft) = Fits(role);
        if (exact.Contains(p.Position)) return p.Overall;
        if (soft.Contains(p.Position)) return p.Overall - SoftPenalty;
        return null;
    }

    static (HashSet<Position> Exact, HashSet<Position> Soft) Fits(string role) => role switch
    {
        "GK" => ([Position.GK], []),
        "CWP" or "ASW" or "CB" => ([Position.CB, Position.SW], [Position.DMF]),
        "LB" or "RB" => ([Position.SB, Position.WB], [Position.CB]),
        "LWB" or "RWB" => ([Position.WB, Position.SB], [Position.SMF]),
        "DM" => ([Position.DMF], [Position.CMF, Position.CB]),
        "CM" => ([Position.CMF, Position.DMF], [Position.AMF]),
        "LM" or "RM" => ([Position.SMF, Position.WB], [Position.WF]),
        "AM" => ([Position.AMF], [Position.CMF, Position.SS]),
        "LW" or "RW" => ([Position.WF], [Position.SMF, Position.SS]),
        "SS" => ([Position.SS], [Position.CF, Position.AMF]),
        "CF" => ([Position.CF], [Position.SS]),
        _ => ([Position.CF], [Position.SS]),
    };

    /// <summary>Gán hàng→cột tối đa hoá tổng điểm (Hungarian trên cost = M − score).</summary>
    static int[] HungarianMaximize(int[,] score)
    {
        var n = score.GetLength(0);
        var max = 0;
        for (var i = 0; i < n; i++)
        for (var j = 0; j < n; j++)
            if (score[i, j] > max) max = score[i, j];
        var cost = new int[n, n];
        for (var i = 0; i < n; i++)
        for (var j = 0; j < n; j++)
            cost[i, j] = max - score[i, j];
        return HungarianMinimize(cost);
    }

    // Kuhn–Munkres rút gọn (n ≤ 32).
    static int[] HungarianMinimize(int[,] a)
    {
        var n = a.GetLength(0);
        var u = new int[n + 1];
        var v = new int[n + 1];
        var p = new int[n + 1];
        var way = new int[n + 1];
        for (var i = 1; i <= n; i++)
        {
            p[0] = i;
            var j0 = 0;
            var minv = new int[n + 1];
            var used = new bool[n + 1];
            Array.Fill(minv, int.MaxValue);
            do
            {
                used[j0] = true;
                var i0 = p[j0];
                var delta = int.MaxValue;
                var j1 = 0;
                for (var j = 1; j <= n; j++)
                {
                    if (used[j]) continue;
                    var cur = a[i0 - 1, j - 1] - u[i0] - v[j];
                    if (cur < minv[j]) { minv[j] = cur; way[j] = j0; }
                    if (minv[j] < delta) { delta = minv[j]; j1 = j; }
                }
                for (var j = 0; j <= n; j++)
                {
                    if (used[j]) { u[p[j]] += delta; v[j] -= delta; }
                    else minv[j] -= delta;
                }
                j0 = j1;
            } while (p[j0] != 0);
            do
            {
                var j1 = way[j0];
                p[j0] = p[j1];
                j0 = j1;
            } while (j0 != 0);
        }
        var colOfRow = new int[n];
        Array.Fill(colOfRow, -1);
        for (var j = 1; j <= n; j++)
            if (p[j] != 0) colOfRow[p[j] - 1] = j - 1;
        return colOfRow;
    }
}
