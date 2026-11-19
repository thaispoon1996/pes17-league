namespace Pes17League.Clubs.Domain;

/// <summary>Cặp loại trực tiếp: lượt 1 <see cref="Lower"/> đá sân nhà, lượt 2 <see cref="Higher"/> (xếp trên ở league phase) đá sân nhà.
/// Chung kết 1 trận: <see cref="Higher"/> = đội chủ nhà theo bốc thăm.</summary>
public sealed record KnockoutTie<T>(FixtureStage Stage, T Higher, T Lower);

/// <summary>
/// Bốc thăm cúp (PO chọn 06/11, bước 5). League phase kiểu UEFA: đội chia nhóm theo hạt giống, mỗi đội gặp 2 đội mỗi nhóm (1 nhà 1 khách),
/// không gặp đội cùng nước (trừ đội cùng nhóm, PO chọn 06/11 vì nhóm theo OVR có thể dồn 4 đội một nước), tối đa 2 đối thủ cùng một nước. Loại trực tiếp theo nhánh cố định: play-off (D+1)–P, (D+2)–(P−1)…;
/// vòng đầu hạng i gặp đội thắng cặp có hạng D+1−i; nhánh xếp kiểu 1–4–2–3 nên bán kết 1/4 gặp nhau, 2/3 gặp nhau.
/// </summary>
public static class CupDraw
{
    /// <summary>Số lần bốc lại khi bế tắc (mỗi lần giới hạn số bước thử).</summary>
    private const int Attempts = 200;
    private const int StepsPerAttempt = 20_000;
    /// <summary>Số lần chia vòng lại với cùng kết quả bốc thăm trước khi bốc lại.</summary>
    private const int SplitAttempts = 20;

    /// <param name="seeded">Đội theo thứ tự hạt giống (mạnh nhất trước); nhóm = khối liên tiếp.</param>
    /// <param name="matches">Số trận mỗi đội (= 2 × số nhóm).</param>
    public static IReadOnlyList<(T Home, T Away)> LeaguePhase<T>(IReadOnlyList<(T Id, string? Country)> seeded, int matches, Random rng, int maxPerCountry = 2)
        where T : notnull
    {
        if (matches <= 0 || matches % 2 != 0) throw new ArgumentException("Số trận league phase phải chẵn", nameof(matches));
        var pots = matches / 2;
        var n = seeded.Count;
        if (n % pots != 0 || n / pots < 3) throw new ArgumentException($"{n} đội không chia đều được {pots} nhóm (mỗi nhóm ≥ 3)", nameof(seeded));
        var size = n / pots;
        if (seeded.Select(s => s.Id).Distinct().Count() != n) throw new ArgumentException("Đội bị trùng", nameof(seeded));
        // Điều kiện cần (tránh tìm kiếm vô vọng hàng giờ): k đội một nước gặp nhau tối đa 2 trận/đội (cùng nhóm) → cần ≥ k·(matches − 2) trận
        // với đội nước khác, mỗi đội nước khác nhận tối đa maxPerCountry đối thủ nước đó.
        foreach (var g in seeded.Where(s => s.Country is not null).GroupBy(s => s.Country))
            if (g.Count() * (matches - 2) > maxPerCountry * (n - g.Count()))
                throw new ArgumentException($"{g.Count()} đội {g.Key}: quá nhiều để bốc thăm (không gặp đội cùng nước, tối đa {maxPerCountry} đối thủ cùng nước)", nameof(seeded));
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            var draw = new LeaguePhaseSearch(seeded.Select(s => s.Country).ToArray(), size, pots, maxPerCountry, rng);
            if (draw.Run(StepsPerAttempt)) return draw.Matches.Select(m => (seeded[m.Home].Id, seeded[m.Away].Id)).ToList();
        }
        throw new InvalidOperationException("Không bốc thăm được league phase với ràng buộc quốc gia hiện tại");
    }

    /// <summary>Bốc thăm league phase rồi chia thành <paramref name="matches"/> vòng (PO chọn 06/11, bước 6); chia không được thì bốc lại.</summary>
    public static IReadOnlyList<IReadOnlyList<(T Home, T Away)>> LeaguePhaseRounds<T>(IReadOnlyList<(T Id, string? Country)> seeded, int matches, Random rng,
        int maxPerCountry = 2) where T : notnull
    {
        for (var attempt = 0; attempt < Attempts; attempt++)
            if (SplitRounds(LeaguePhase(seeded, matches, rng, maxPerCountry), matches, rng) is { } rounds) return rounds;
        throw new InvalidOperationException("Không chia được league phase thành vòng");
    }

    /// <summary>Chia trận thành <paramref name="rounds"/> vòng: mỗi đội đá đúng 1 trận mỗi vòng, không quá 2 trận sân nhà (hoặc sân khách) liên tiếp.
    /// null = không chia được trong giới hạn bước thử.</summary>
    public static IReadOnlyList<IReadOnlyList<(T Home, T Away)>>? SplitRounds<T>(IReadOnlyList<(T Home, T Away)> matches, int rounds, Random rng) where T : notnull
    {
        var teams = matches.SelectMany(m => new[] { m.Home, m.Away }).Distinct().ToList();
        var index = teams.Select((t, i) => (t, i)).ToDictionary(x => x.t, x => x.i);
        var edges = matches.Select(m => (Home: index[m.Home], Away: index[m.Away])).ToArray();
        if (teams.Any(t => matches.Count(m => m.Home.Equals(t) || m.Away.Equals(t)) != rounds))
            throw new ArgumentException($"Mỗi đội phải có đúng {rounds} trận", nameof(matches));
        for (var attempt = 0; attempt < SplitAttempts; attempt++)
        {
            var search = new RoundSearch(teams.Count, edges, rounds, rng);
            if (search.Run(StepsPerAttempt))
                return [.. Enumerable.Range(0, rounds).Select(r => (IReadOnlyList<(T, T)>)[.. search.Round(r).Select(e => (teams[edges[e].Home], teams[edges[e].Away]))])];
        }
        return null;
    }

    /// <summary>Khung loại trực tiếp theo thể thức: play-off 2 lượt, các vòng sau 2 lượt, chung kết 1 trận.</summary>
    public static IReadOnlyList<(FixtureStage Stage, int? Leg)> KnockoutStages(int teams, int direct, int playoffPlaces)
    {
        CheckBracket(teams, direct, playoffPlaces);
        var stages = new List<(FixtureStage, int?)> { (FixtureStage.KnockoutPlayoff, 1), (FixtureStage.KnockoutPlayoff, 2) };
        for (var t = 2 * direct; t > 2; t /= 2) stages.AddRange([(StageFor(t), 1), (StageFor(t), 2)]);
        stages.Add((FixtureStage.Final, null));
        return stages;
    }

    /// <summary>Luỹ thừa 2 gần nhất ≥ <paramref name="teams"/> (vòng đầu có bye nếu không đủ đội).</summary>
    public static int BracketSize(int teams)
    {
        if (teams < 2) throw new ArgumentException("Cúp cần ít nhất 2 đội", nameof(teams));
        var n = 2;
        while (n < teams) n *= 2;
        return n;
    }

    /// <summary>Khung knockout 1 lượt (cúp quốc gia): từ vòng đầu (có bye nếu cần) tới chung kết.</summary>
    public static IReadOnlyList<(FixtureStage Stage, int? Leg)> SingleLegStages(int teams)
    {
        var stages = new List<(FixtureStage, int?)>();
        for (var t = BracketSize(teams); t >= 2; t /= 2) stages.Add((StageFor(t), null));
        return stages;
    }

    /// <summary>Vòng mở màn cúp 1 lượt: hạt giống mạnh (đầu danh sách) được bye; các đội còn lại bốc cặp ngẫu nhiên.
    /// Trả các cặp đá vòng đầu (chưa gồm đội bye).</summary>
    public static IReadOnlyList<KnockoutTie<T>> SingleLegOpen<T>(IReadOnlyList<T> seeded, Random rng) where T : notnull
    {
        var bracket = BracketSize(seeded.Count);
        var byes = bracket - seeded.Count;
        var playing = seeded.Skip(byes).ToList();
        if (playing.Count == 0 || playing.Count % 2 != 0)
            throw new ArgumentException($"{seeded.Count} đội không tạo được vòng mở với {byes} bye", nameof(seeded));
        return SingleLegPair(playing, StageFor(bracket), rng);
    }

    /// <summary>Bốc thăm vòng knockout 1 lượt: số đội phải là luỹ thừa của 2; ghép ngẫu nhiên, chủ nhà ngẫu nhiên.</summary>
    public static IReadOnlyList<KnockoutTie<T>> SingleLegDraw<T>(IReadOnlyList<T> clubs, Random rng) where T : notnull
    {
        if (clubs.Count < 2 || (clubs.Count & (clubs.Count - 1)) != 0)
            throw new ArgumentException("Số đội vòng knockout 1 lượt phải là luỹ thừa của 2", nameof(clubs));
        return SingleLegPair(clubs, StageFor(clubs.Count), rng);
    }

    private static IReadOnlyList<KnockoutTie<T>> SingleLegPair<T>(IReadOnlyList<T> clubs, FixtureStage stage, Random rng) where T : notnull
    {
        var order = clubs.OrderBy(_ => rng.Next()).ToList();
        return [.. Enumerable.Range(0, order.Count / 2).Select(i =>
        {
            var (a, b) = (order[2 * i], order[2 * i + 1]);
            return rng.Next(2) == 0 ? new KnockoutTie<T>(stage, a, b) : new KnockoutTie<T>(stage, b, a);
        })];
    }

    /// <summary>Cặp play-off từ BXH league phase: hạng D+k gặp hạng P+1−k (k = 1..D), cần P − D = 2D.</summary>
    public static IReadOnlyList<KnockoutTie<T>> Playoff<T>(IReadOnlyList<T> ranking, int direct, int playoffPlaces)
    {
        CheckBracket(ranking.Count, direct, playoffPlaces);
        return [.. Enumerable.Range(1, direct).Select(k => new KnockoutTie<T>(FixtureStage.KnockoutPlayoff, ranking[direct + k - 1], ranking[playoffPlaces - k]))];
    }

    /// <summary>Vòng loại trực tiếp đầu: hạng i (1..D) gặp đội thắng play-off cặp k = D+1−i. Trả theo thứ tự nhánh.</summary>
    /// <param name="playoffWinners">Đội thắng theo thứ tự cặp của <see cref="Playoff"/>.</param>
    public static IReadOnlyList<KnockoutTie<T>> FirstRound<T>(IReadOnlyList<T> ranking, int direct, IReadOnlyList<T> playoffWinners, Random rng)
    {
        if (playoffWinners.Count != direct) throw new ArgumentException($"Cần {direct} đội thắng play-off", nameof(playoffWinners));
        var pairs = BracketOrder(direct).Select(i => (ranking[i - 1], playoffWinners[direct - i])).ToList();
        return Round(pairs, ranking, rng);
    }

    /// <summary>Vòng sau: ghép đội thắng liền kề theo thứ tự nhánh của vòng trước.</summary>
    public static IReadOnlyList<KnockoutTie<T>> NextRound<T>(IReadOnlyList<T> winnersInBracketOrder, IReadOnlyList<T> ranking, Random rng)
    {
        if (winnersInBracketOrder.Count < 2 || winnersInBracketOrder.Count % 2 != 0) throw new ArgumentException("Số đội thắng phải chẵn", nameof(winnersInBracketOrder));
        var pairs = Enumerable.Range(0, winnersInBracketOrder.Count / 2).Select(i => (winnersInBracketOrder[2 * i], winnersInBracketOrder[2 * i + 1])).ToList();
        return Round(pairs, ranking, rng);
    }

    /// <summary>Đội đi tiếp của cặp 2 lượt (PO chọn 06/11): tổng tỷ số → bàn thắng sân khách → đội xếp trên ở league phase (<see cref="KnockoutTie{T}.Higher"/>).
    /// Game không biết tỷ số lượt 1 nên không dùng hiệp phụ/luân lưu trong game cho cặp 2 lượt.</summary>
    public static T TieWinner<T>(KnockoutTie<T> tie, (int Home, int Away) leg1, (int Home, int Away) leg2)
    {
        var (lower, higher) = (leg1.Home + leg2.Away, leg1.Away + leg2.Home);
        if (lower != higher) return higher > lower ? tie.Higher : tie.Lower;
        return leg2.Away > leg1.Away ? tie.Lower : tie.Higher;
    }

    /// <summary>Thứ tự nhánh chuẩn: 1 → [1]; 2n → mỗi s thành [s, 2n+1−s] (4 → 1,4,2,3).</summary>
    public static IReadOnlyList<int> BracketOrder(int seeds)
    {
        if (seeds < 1 || (seeds & (seeds - 1)) != 0) throw new ArgumentException("Số đội vào thẳng phải là luỹ thừa của 2", nameof(seeds));
        IReadOnlyList<int> order = [1];
        while (order.Count < seeds) { var m = order.Count * 2; order = [.. order.SelectMany(s => new[] { s, m + 1 - s })]; }
        return order;
    }

    private static void CheckBracket(int teams, int direct, int playoffPlaces)
    {
        BracketOrder(direct);
        if (playoffPlaces - direct != 2 * direct || playoffPlaces > teams)
            throw new ArgumentException($"Thể thức chưa hỗ trợ: {direct} đội vào thẳng, play-off tới hạng {playoffPlaces}, {teams} đội");
    }

    private static FixtureStage StageFor(int teams) => teams switch
    {
        2 => FixtureStage.Final, 4 => FixtureStage.SemiFinal, 8 => FixtureStage.QuarterFinal, 16 => FixtureStage.RoundOf16,
        32 => FixtureStage.RoundOf32, 64 => FixtureStage.RoundOf64,
        _ => throw new ArgumentException($"Vòng {teams} đội chưa hỗ trợ"),
    };

    private static IReadOnlyList<KnockoutTie<T>> Round<T>(List<(T A, T B)> pairs, IReadOnlyList<T> ranking, Random rng)
    {
        var stage = StageFor(pairs.Count * 2);
        int Rank(T id) { var i = ranking.ToList().IndexOf(id); return i < 0 ? throw new ArgumentException($"{id} không có trong BXH") : i; }
        return [.. pairs.Select(p =>
        {
            var aHigher = stage == FixtureStage.Final ? rng.Next(2) == 0 : Rank(p.A) < Rank(p.B);
            return aHigher ? new KnockoutTie<T>(stage, p.A, p.B) : new KnockoutTie<T>(stage, p.B, p.A);
        })];
    }

    /// <summary>Tìm kiếm quay lui: mỗi đội cần 1 trận nhà + 1 trận khách với mỗi nhóm; chọn nhu cầu ít ứng viên nhất trước, thử ngẫu nhiên.</summary>
    private sealed class LeaguePhaseSearch(string?[] country, int size, int pots, int maxPerCountry, Random rng)
    {
        private readonly int n = country.Length;
        // need[t, q, 0] = còn thiếu trận nhà với nhóm q; [.., 1] = trận khách.
        private readonly bool[,,] need = Fill(country.Length, pots);
        private readonly HashSet<(int, int)> met = [];
        private readonly Dictionary<(int Team, string Country), int> perCountry = [];
        private int steps;
        public List<(int Home, int Away)> Matches { get; } = [];

        private static bool[,,] Fill(int n, int pots)
        {
            var a = new bool[n, pots, 2];
            for (var t = 0; t < n; t++) for (var q = 0; q < pots; q++) a[t, q, 0] = a[t, q, 1] = true;
            return a;
        }

        private int Pot(int t) => t / size;

        private bool Allowed(int home, int away)
        {
            if (home == away || met.Contains((Math.Min(home, away), Math.Max(home, away)))) return false;
            if (!need[home, Pot(away), 0] || !need[away, Pot(home), 1]) return false;
            var (ch, ca) = (country[home], country[away]);
            if (ch is not null && ch == ca && Pot(home) != Pot(away)) return false;
            if (ca is not null && perCountry.GetValueOrDefault((home, ca)) >= maxPerCountry) return false;
            return ch is null || perCountry.GetValueOrDefault((away, ch)) < maxPerCountry;
        }

        private void Apply(int home, int away, int delta)
        {
            var on = delta < 0;
            need[home, Pot(away), 0] = on;
            need[away, Pot(home), 1] = on;
            var key = (Math.Min(home, away), Math.Max(home, away));
            if (on) { met.Remove(key); Matches.RemoveAt(Matches.Count - 1); } else { met.Add(key); Matches.Add((home, away)); }
            if (country[away] is { } ca) perCountry[(home, ca)] = perCountry.GetValueOrDefault((home, ca)) + delta;
            if (country[home] is { } ch) perCountry[(away, ch)] = perCountry.GetValueOrDefault((away, ch)) + delta;
        }

        public bool Run(int maxSteps)
        {
            if (++steps > maxSteps) return false;
            List<(int Home, int Away)>? best = null;
            for (var t = 0; t < n; t++)
                for (var q = 0; q < pots; q++)
                    for (var side = 0; side < 2; side++)
                    {
                        if (!need[t, q, side]) continue;
                        var options = new List<(int, int)>();
                        for (var o = q * size; o < (q + 1) * size; o++)
                        {
                            var m = side == 0 ? (t, o) : (o, t);
                            if (Allowed(m.Item1, m.Item2)) options.Add(m);
                        }
                        if (options.Count == 0) return false;
                        if (best is null || options.Count < best.Count) best = options;
                    }
            if (best is null) return true;
            foreach (var (home, away) in best.OrderBy(_ => rng.Next()).ToList())
            {
                Apply(home, away, 1);
                if (Run(maxSteps)) return true;
                Apply(home, away, -1);
                if (steps > maxSteps) return false;
            }
            return false;
        }
    }

    /// <summary>Xếp lần lượt từng vòng: ghép đủ cặp cho vòng (đội ít trận hợp lệ nhất trước, thử ngẫu nhiên), quay lui cả sang vòng trước.
    /// Cắt nhánh khi số trận nhà/khách còn lại không xếp được với tối đa 2 trận liên tiếp cùng sân.</summary>
    private sealed class RoundSearch
    {
        private readonly int n, rounds;
        private readonly (int Home, int Away)[] edges;
        private readonly List<int>[] adj;
        private readonly Random rng;
        private readonly int[] roundOf;            // vòng của trận; −1 = chưa xếp
        private readonly bool[] busy;              // đội đã có trận ở vòng đang xếp
        private readonly int[] homeLeft, awayLeft;
        private readonly int[] run;                // chuỗi hiện tại: > 0 = số trận nhà liên tiếp, < 0 = số trận khách liên tiếp
        private readonly Stack<(int Edge, int RunHome, int RunAway)> history = new();
        private int round, matched, steps;

        public RoundSearch(int n, (int Home, int Away)[] edges, int rounds, Random rng)
        {
            (this.n, this.edges, this.rounds, this.rng) = (n, edges, rounds, rng);
            adj = [.. Enumerable.Range(0, n).Select(_ => new List<int>())];
            (homeLeft, awayLeft, busy, run) = (new int[n], new int[n], new bool[n], new int[n]);
            for (var e = 0; e < edges.Length; e++)
            {
                adj[edges[e].Home].Add(e);
                adj[edges[e].Away].Add(e);
                homeLeft[edges[e].Home]++;
                awayLeft[edges[e].Away]++;
            }
            roundOf = [.. Enumerable.Repeat(-1, edges.Length)];
        }

        public IEnumerable<int> Round(int r) => Enumerable.Range(0, edges.Length).Where(e => roundOf[e] == r);

        private static int Next(int run, bool home) => home ? (run > 0 ? run + 1 : 1) : (run < 0 ? run - 1 : -1);

        /// <summary>Còn <paramref name="home"/> trận nhà, <paramref name="away"/> trận khách sau chuỗi <paramref name="run"/>: xếp được không quá 2 liên tiếp?
        /// Trận khách chia trận nhà thành tối đa (khách + 1) đoạn, mỗi đoạn ≤ 2, đoạn đầu nối chuỗi hiện tại; ngược lại tương tự.</summary>
        private static bool Feasible(int home, int away, int run)
        {
            var (firstHome, firstAway) = run > 0 ? (2 - run, 2) : (2, 2 + run);
            return home <= firstHome + 2 * away && away <= firstAway + 2 * home;
        }

        private bool Allowed(int e)
        {
            var (h, a) = edges[e];
            if (roundOf[e] >= 0 || busy[h] || busy[a]) return false;
            var (rh, ra) = (Next(run[h], true), Next(run[a], false));
            return rh <= 2 && ra >= -2 && Feasible(homeLeft[h] - 1, awayLeft[h], rh) && Feasible(homeLeft[a], awayLeft[a] - 1, ra);
        }

        private void Apply(int e)
        {
            var (h, a) = edges[e];
            history.Push((e, run[h], run[a]));
            roundOf[e] = round;
            busy[h] = busy[a] = true;
            homeLeft[h]--;
            awayLeft[a]--;
            (run[h], run[a]) = (Next(run[h], true), Next(run[a], false));
            matched += 2;
        }

        private void Undo()
        {
            var (e, rh, ra) = history.Pop();
            var (h, a) = edges[e];
            roundOf[e] = -1;
            busy[h] = busy[a] = false;
            homeLeft[h]++;
            awayLeft[a]++;
            (run[h], run[a]) = (rh, ra);
            matched -= 2;
        }

        public bool Run(int maxSteps)
        {
            if (++steps > maxSteps) return false;
            if (matched == n)
            {
                if (round == rounds - 1) return true;
                (round, matched) = (round + 1, 0);
                Array.Fill(busy, false);
                if (Run(maxSteps)) return true;
                (round, matched) = (round - 1, n);
                Array.Fill(busy, true);
                return false;
            }
            List<int>? best = null;
            for (var t = 0; t < n; t++)
            {
                if (busy[t]) continue;
                var options = adj[t].Where(Allowed).ToList();
                if (options.Count == 0) return false;
                if (best is null || options.Count < best.Count) best = options;
            }
            foreach (var e in best!.OrderBy(_ => rng.Next()).ToList())
            {
                Apply(e);
                if (Run(maxSteps)) return true;
                Undo();
                if (steps > maxSteps) return false;
            }
            return false;
        }
    }
}
