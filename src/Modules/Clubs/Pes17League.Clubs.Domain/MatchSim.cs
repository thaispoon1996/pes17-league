namespace Pes17League.Clubs.Domain;

/// <summary>Cầu thủ đưa vào mô phỏng: <paramref name="Overall"/> là chỉ số hiệu dụng (đã trừ chấn thương / mệt), <paramref name="Slot"/> = ô đội hình (0–10 đá chính).</summary>
public sealed record SimPlayer(Guid PlayerId, Position Position, int Overall, int Slot);

/// <summary>Đội hình vào trận: 11 đá chính theo thứ tự ô, dự bị theo thứ tự ưu tiên.</summary>
public sealed record SimTeam(IReadOnlyList<SimPlayer> Starters, IReadOnlyList<SimPlayer> Bench);

/// <param name="Half">1/2 = hiệp chính, 3/4 = hiệp phụ (phút 91–120).</param>
public sealed record SimGoal(bool Home, int Half, int Minute, Guid? Scorer);

public sealed record SimPlayerLine(bool Home, Guid PlayerId, int Slot, decimal Rating, int Goals, int Shots, int ShotsOnTarget, int? InMinute, int? OutMinute);

public sealed record SimTeamLine(int Possession, int Shots, int ShotsOnTarget, int Passes, int PassesCompleted, int Fouls, int Corners, int Offsides);

public sealed record SimResult(int HomeGoals, int AwayGoals, IReadOnlyList<SimGoal> Goals, IReadOnlyList<SimPlayerLine> Players, SimTeamLine HomeStats, SimTeamLine AwayStats,
    (int Home, int Away)? Penalties);

/// <summary>
/// Mô phỏng 1 trận (career PES17, PO 19/11: trận không có PO thì mô phỏng). Bàn thắng mỗi đội ~ Poisson, kỳ vọng theo chênh OVR trung bình 11 người đá chính
/// (mỗi điểm OVR ≈ ±<see cref="PerOverall"/> log-bàn) + lợi thế sân nhà + phong độ ngẫu nhiên trong ngày. Người ghi bàn theo vị trí × OVR;
/// 3 lượt thay người; điểm cầu thủ thang PES (6,0 trung tính, bước 0,5). Trận knockout 1 lượt hoà → hiệp phụ, rồi luân lưu.
/// </summary>
public static class MatchSim
{
    public const double HomeBase = 1.50, AwayBase = 1.15, NeutralBase = 1.30, PerOverall = 0.065, DayForm = 1.5;
    public const int MissingOverall = 40;

    public static double Strength(SimTeam t)
    {
        var xi = t.Starters.Take(11).Select(p => (double)p.Overall).ToList();
        while (xi.Count < 11) xi.Add(MissingOverall);
        return xi.Average();
    }

    /// <summary>Số bàn kỳ vọng (chưa tính phong độ trong ngày).</summary>
    public static (double Home, double Away) Expected(SimTeam home, SimTeam away, bool neutral = false)
    {
        var d = Strength(home) - Strength(away);
        var (hb, ab) = neutral ? (NeutralBase, NeutralBase) : (HomeBase, AwayBase);
        return (hb * Math.Exp(PerOverall * d), ab * Math.Exp(-PerOverall * d));
    }

    /// <summary>Đội ra sân từ đội hình xếp theo ô (11 ô đầu đá chính): cầu thủ chấn thương không ra sân — người đá chính chấn thương được thay
    /// bằng dự bị khoẻ tốt nhất cùng tuyến (không có thì dự bị khoẻ tốt nhất ngoài thủ môn; thủ môn chỉ thay bằng thủ môn); hết người thì vẫn ra sân.
    /// <paramref name="tired"/> (trợ lý xoay vòng, PO 19/11): người đá chính mệt được cho nghỉ nếu có dự bị cùng tuyến không mệt, người nghỉ xuống
    /// cuối ghế dự bị. Cần cho CLB người chơi khi mô phỏng (không có HLV BOT dọn đội hình).</summary>
    public static SimTeam Lineup(IReadOnlyList<SimPlayer> squad, Func<Guid, bool> injured, Func<Guid, bool>? tired = null)
    {
        var starters = squad.Take(11).ToList();
        var bench = squad.Skip(11).Where(p => !injured(p.PlayerId)).ToList();
        var rested = new List<SimPlayer>();
        bool Fresh(SimPlayer p) => tired is null || !tired(p.PlayerId);
        for (var i = 0; i < starters.Count; i++)
        {
            var s = starters[i];
            var hurt = injured(s.PlayerId);
            if (!hurt && Fresh(s)) continue;
            var g = s.Position.Group();
            var sub = bench.Where(b => b.Position.Group() == g && Fresh(b)).MaxBy(b => b.Overall);
            if (sub is null && hurt)
                sub = bench.Where(b => b.Position.Group() == g).MaxBy(b => b.Overall)
                    ?? (g == PositionGroup.Goalkeeper ? null : bench.Where(b => b.Position.Group() != PositionGroup.Goalkeeper).MaxBy(b => b.Overall));
            if (sub is null) continue;
            bench.Remove(sub);
            starters[i] = sub with { Slot = s.Slot };
            if (!hurt) rested.Add(s with { Slot = sub.Slot });
        }
        return new SimTeam(starters, [.. bench, .. rested]);
    }

    /// <param name="knockout">Trận loại trực tiếp 1 lượt: hoà sau 90' → hiệp phụ 30', vẫn hoà → luân lưu.</param>
    /// <param name="neutral">Sân trung lập (chung kết): không lợi thế sân nhà.</param>
    public static SimResult Play(SimTeam home, SimTeam away, Random rng, bool knockout = false, bool neutral = false)
    {
        var (eh, ea) = Expected(home, away, neutral);
        var form = Normal(rng) * DayForm * PerOverall;   // phong độ trong ngày: dương nghiêng về đội nhà
        eh *= Math.Exp(form);
        ea *= Math.Exp(-form);

        var subsH = Subs(home, rng);
        var subsA = Subs(away, rng);
        var goals = new List<SimGoal>();
        AddGoals(goals, true, Poisson(eh, rng), 1, 90, home, subsH, rng);
        AddGoals(goals, false, Poisson(ea, rng), 1, 90, away, subsA, rng);
        (int, int)? pens = null;
        if (knockout && goals.Count(g => g.Home) == goals.Count(g => !g.Home))
        {
            AddGoals(goals, true, Poisson(eh / 3, rng), 91, 120, home, subsH, rng);
            AddGoals(goals, false, Poisson(ea / 3, rng), 91, 120, away, subsA, rng);
            if (goals.Count(g => g.Home) == goals.Count(g => !g.Home)) pens = Shootout(rng);
        }
        goals.Sort((a, b) => a.Minute.CompareTo(b.Minute));
        int hg = goals.Count(g => g.Home), ag = goals.Count(g => !g.Home);

        var shotsH = Shots(eh, hg, rng);
        var shotsA = Shots(ea, ag, rng);
        var players = new List<SimPlayerLine>();
        players.AddRange(Lines(true, home, subsH, goals, hg, ag, shotsH, rng));
        players.AddRange(Lines(false, away, subsA, goals, ag, hg, shotsA, rng));
        var poss = (int)Math.Clamp(Math.Round(50 + (Strength(home) - Strength(away)) * 0.9 + (neutral ? 0 : 2) + Normal(rng) * 4), 28, 72);
        return new SimResult(hg, ag, goals, players, TeamLine(poss, shotsH, rng), TeamLine(100 - poss, shotsA, rng), pens);
    }

    /// <summary>Luân lưu 5 lượt rồi đá luân phiên tới khi phân thắng bại; mỗi quả vào 75%.</summary>
    public static (int Home, int Away) Shootout(Random rng)
    {
        int h = 0, a = 0;
        for (var i = 0; i < 5; i++)
        {
            if (rng.NextDouble() < 0.75) h++;
            if (h > a + (5 - i)) return (h, a);   // khách không còn đuổi kịp
            if (rng.NextDouble() < 0.75) a++;
            if (a > h + (4 - i) || h > a + (4 - i)) return (h, a);
        }
        while (true)
        {
            var hs = rng.NextDouble() < 0.75;
            var @as = rng.NextDouble() < 0.75;
            h += hs ? 1 : 0;
            a += @as ? 1 : 0;
            if (hs != @as) return (h, a);
        }
    }

    sealed record Sub(SimPlayer Out, SimPlayer In, int Minute);

    /// <summary>Tối đa 3 lượt thay người phút 55–85: dự bị cùng nhóm vị trí (không có thì người đầu danh sách), không thay thủ môn.</summary>
    static List<Sub> Subs(SimTeam t, Random rng)
    {
        var bench = t.Bench.Where(p => p.Position != Position.GK).ToList();
        var outfield = t.Starters.Take(11).Where(p => p.Position != Position.GK).ToList();
        var subs = new List<Sub>();
        var n = Math.Min(3, Math.Min(bench.Count, outfield.Count));
        for (var i = 0; i < n; i++)
        {
            var o = outfield[rng.Next(outfield.Count)];
            outfield.Remove(o);
            var inn = bench.FirstOrDefault(b => b.Position.Group() == o.Position.Group()) ?? bench[0];
            bench.Remove(inn);
            subs.Add(new Sub(o, inn, rng.Next(55, 86)));
        }
        return subs;
    }

    static readonly Dictionary<Position, double> ScoreWeight = new()
    {
        [Position.CF] = 1.0, [Position.SS] = 0.85, [Position.WF] = 0.75, [Position.AMF] = 0.5, [Position.SMF] = 0.3, [Position.CMF] = 0.22,
        [Position.DMF] = 0.1, [Position.WB] = 0.1, [Position.SB] = 0.07, [Position.CB] = 0.1, [Position.SW] = 0.05, [Position.GK] = 0.002,
    };

    static double Weight(SimPlayer p) => ScoreWeight.GetValueOrDefault(p.Position, 0.2) * Math.Pow(Math.Max(p.Overall, 30) / 75.0, 3);

    /// <summary>Người trên sân ở phút <paramref name="minute"/>.</summary>
    static IEnumerable<SimPlayer> OnPitch(SimTeam t, List<Sub> subs, int minute)
    {
        foreach (var p in t.Starters.Take(11))
            if (subs.FirstOrDefault(s => s.Out == p) is not { } s || s.Minute > minute) yield return p;
        foreach (var s in subs.Where(s => s.Minute <= minute)) yield return s.In;
    }

    static void AddGoals(List<SimGoal> goals, bool home, int n, int from, int to, SimTeam t, List<Sub> subs, Random rng)
    {
        for (var i = 0; i < n; i++)
        {
            var minute = rng.Next(from, to + 1);
            var pool = OnPitch(t, subs, minute).ToList();
            Guid? scorer = null;
            if (pool.Count > 0)
            {
                var total = pool.Sum(Weight);
                var r = rng.NextDouble() * total;
                foreach (var p in pool)
                {
                    r -= Weight(p);
                    if (r <= 0) { scorer = p.PlayerId; break; }
                }
                scorer ??= pool[^1].PlayerId;
            }
            var half = minute <= 45 ? 1 : minute <= 90 ? 2 : minute <= 105 ? 3 : 4;
            goals.Add(new SimGoal(home, half, minute, scorer));
        }
    }

    static int Shots(double expected, int goals, Random rng) => Math.Max(goals, (int)Math.Round(expected * 8 + Normal(rng) * 2.5 + 3));

    static SimTeamLine TeamLine(int poss, int shots, Random rng)
    {
        var passes = (int)Math.Round(poss * 9.5 + Normal(rng) * 25);
        return new SimTeamLine(poss, shots, Math.Max(0, (int)Math.Round(shots * 0.38 + Normal(rng))), passes, (int)Math.Round(passes * (0.74 + poss / 1000.0)),
            rng.Next(6, 17), Math.Max(0, (int)Math.Round(shots * 0.4 + Normal(rng) * 1.5)), rng.Next(0, 5));
    }

    static IEnumerable<SimPlayerLine> Lines(bool home, SimTeam t, List<Sub> subs, List<SimGoal> goals, int scored, int conceded, int shots, Random rng)
    {
        var result = scored > conceded ? 0.4 : scored < conceded ? -0.4 : 0;
        var squad = t.Starters.Take(11).Select(p => (P: p, In: (int?)null, Out: subs.FirstOrDefault(s => s.Out == p)?.Minute))
            .Concat(subs.Select(s => (P: s.In, In: (int?)s.Minute, Out: (int?)null))).ToList();
        // phân phối cú sút theo trọng số ghi bàn, cộng thêm bàn thắng của từng người
        var shotBy = squad.ToDictionary(x => x.P.PlayerId, _ => 0);
        var w = squad.Select(x => Weight(x.P) * (x.In is { } i ? (90 - i) / 90.0 : 1)).ToList();
        var extra = Math.Max(0, shots - scored);
        for (var k = 0; k < extra && w.Sum() > 0; k++)
        {
            var r = rng.NextDouble() * w.Sum();
            for (var j = 0; j < squad.Count; j++)
            {
                r -= w[j];
                if (r <= 0) { shotBy[squad[j].P.PlayerId]++; break; }
            }
        }
        foreach (var (p, inMin, outMin) in squad)
        {
            var g = goals.Count(x => x.Home == home && x.Scorer == p.PlayerId);
            var group = p.Position.Group();
            var r = 6.0 + result + (p.Overall - 75) * 0.02 + Normal(rng) * 0.45;
            r += g switch { 0 => 0, 1 => 0.9, 2 => 1.6, _ => 2.2 };
            if (group is PositionGroup.Defender or PositionGroup.Goalkeeper)
                r += conceded == 0 ? 0.5 : conceded >= 3 ? -0.5 : 0;
            if (inMin is not null) r = 6.0 + (r - 6.0) * 0.6;   // vào sân muộn: điểm ít dao động
            var rating = (decimal)(Math.Round(Math.Clamp(r, 4.0, 10.0) * 2, MidpointRounding.AwayFromZero) / 2);
            var s = shotBy[p.PlayerId] + g;
            yield return new SimPlayerLine(home, p.PlayerId, p.Slot, rating, g, s, Math.Min(s, g + rng.Next(0, Math.Max(1, s - g) + 1) / 2), inMin, outMin);
        }
    }

    static int Poisson(double lambda, Random rng)
    {
        var l = Math.Exp(-lambda);
        var k = 0;
        var p = 1.0;
        do { k++; p *= rng.NextDouble(); } while (p > l);
        return k - 1;
    }

    static double Normal(Random rng)
    {
        var u1 = 1.0 - rng.NextDouble();
        var u2 = rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
    }
}
