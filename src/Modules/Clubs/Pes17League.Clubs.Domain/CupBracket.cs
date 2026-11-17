namespace Pes17League.Clubs.Domain;

/// <summary>Một bên của cặp trong sơ đồ nhánh. <see cref="Rank"/> = suất theo hạng league phase: CLB lấy theo BXH hiện tại,
/// <see cref="Projected"/> khi league phase chưa đá xong (chưa đá trận nào thì chưa có CLB). <see cref="From"/> = đội thắng cặp số đó của vòng trước
/// (<see cref="Club"/> và hạng của đội đó có khi cặp đó đã phân định).</summary>
public sealed record BracketSide(Guid? Club, int? Rank = null, int? From = null, bool Projected = false);

/// <summary>Cặp số <see cref="Number"/> (thứ tự nhánh, từ 1). <see cref="Legs"/> = trận đã bốc (lượt 1, lượt 2 hoặc trận chung kết);
/// <see cref="Winner"/> khi đá xong và phân định được (cặp 2 lượt: <see cref="CupDraw.TieWinner"/>; chung kết: tỷ số rồi penalty).</summary>
public sealed record BracketTie(int Number, BracketSide A, BracketSide B, IReadOnlyList<Fixture> Legs, Guid? Winner);

public sealed record BracketRound(FixtureStage Stage, IReadOnlyList<BracketTie> Ties);

/// <summary>
/// Sơ đồ nhánh cúp (PO chọn 06/11, bước 7): dựng lại từ BXH league phase và kết quả theo đúng cách <c>SeasonScheduleService.AdvanceAsync</c> bốc —
/// play-off hạng D+k gặp P+1−k, vòng đầu hạng i gặp đội thắng play-off cặp D+1−i theo <see cref="CupDraw.BracketOrder"/>, vòng sau ghép đội thắng liền kề.
/// Trận đã bốc khớp vào cặp theo 2 CLB.
/// </summary>
public static class CupBracket
{
    /// <param name="clubs">CLB của mùa giải; <paramref name="name"/> để xếp hạng như <see cref="Standings.Compute"/>.</param>
    /// <param name="fixtures">Mọi trận của mùa giải (league phase + loại trực tiếp).</param>
    public static IReadOnlyList<BracketRound> Build(IReadOnlyCollection<Guid> clubs, Func<Guid, string> name, IReadOnlyCollection<Fixture> fixtures, int direct, int playoffPlaces)
    {
        var stages = CupDraw.KnockoutStages(clubs.Count, direct, playoffPlaces).Select(s => s.Stage).Distinct().ToList();
        var lp = fixtures.Where(f => f.Stage == FixtureStage.LeaguePhase).ToList();
        var played = lp.Where(f => f.Status == FixtureStatus.Played)
            .Select(f => new PlayedFixture<Guid>(f.HomeClubId, f.AwayClubId, f.HomeGoals!.Value, f.AwayGoals!.Value)).ToList();
        var ranking = Standings.Compute(clubs, played, name).Select(r => r.Club).ToList();
        var (started, done) = (played.Count > 0, played.Count == lp.Count && lp.Count > 0);

        var ties = Enumerable.Range(1, direct).Select(k => Tie(stages[0], k, Seed(direct + k), Seed(playoffPlaces + 1 - k))).ToList();
        var rounds = new List<BracketRound> { new(stages[0], ties) };
        foreach (var stage in stages.Skip(1))
        {
            var prev = ties;
            ties = rounds.Count == 1
                ? [.. CupDraw.BracketOrder(direct).Select((seed, i) => Tie(stage, i + 1, Seed(seed), WinnerOf(prev[direct - seed])))]
                : [.. Enumerable.Range(0, prev.Count / 2).Select(i => Tie(stage, i + 1, WinnerOf(prev[2 * i]), WinnerOf(prev[2 * i + 1])))];
            rounds.Add(new(stage, ties));
        }
        return rounds;

        BracketSide Seed(int rank) => new(started ? ranking[rank - 1] : null, rank, Projected: started && !done);
        static BracketSide WinnerOf(BracketTie t) => new(t.Winner, t.Winner is null ? null : t.Winner == t.A.Club ? t.A.Rank : t.B.Rank, From: t.Number);

        BracketTie Tie(FixtureStage stage, int number, BracketSide a, BracketSide b)
        {
            List<Fixture> legs = a is { Club: { } x, Projected: false } && b is { Club: { } y, Projected: false }
                ? [.. fixtures.Where(f => f.Stage == stage && (f.HomeClubId == x && f.AwayClubId == y || f.HomeClubId == y && f.AwayClubId == x)).OrderBy(f => f.Leg)]
                : [];
            return new(number, a, b, legs, Winner(stage, legs));
        }
    }

    private static Guid? Winner(FixtureStage stage, List<Fixture> legs)
    {
        if (legs.Count == 0 || legs.Any(f => f.Status != FixtureStatus.Played)) return null;
        if (legs.Count == 1 && legs[0].Leg is null) return legs[0].WinnerClubId();   // chung kết / cúp quốc gia 1 lượt
        if (legs is not [{ Leg: 1 } l1, { Leg: 2 } l2]) return null;
        // Lượt 1 đội xếp dưới đá sân nhà.
        return CupDraw.TieWinner(new KnockoutTie<Guid>(stage, l1.AwayClubId, l1.HomeClubId), (l1.HomeGoals!.Value, l1.AwayGoals!.Value), (l2.HomeGoals!.Value, l2.AwayGoals!.Value));
    }

    /// <summary>Sơ đồ nhánh cúp quốc gia (knockout 1 lượt, bốc lại mỗi vòng): cột = giai đoạn; cặp = trận đã bốc; vòng chưa bốc để trống số cặp dự kiến.</summary>
    public static IReadOnlyList<BracketRound> BuildSingleLeg(int teams, IReadOnlyCollection<Fixture> fixtures)
    {
        var stages = CupDraw.SingleLegStages(teams).Select(s => s.Stage).ToList();
        var bracket = CupDraw.BracketSize(teams);
        return [.. stages.Select((stage, i) =>
        {
            var legs = fixtures.Where(f => f.Stage == stage).OrderBy(f => f.Id).ToList();
            if (legs.Count > 0)
                return new BracketRound(stage, [.. legs.Select((f, n) => new BracketTie(n + 1, new BracketSide(f.HomeClubId), new BracketSide(f.AwayClubId), [f],
                    f.Status == FixtureStatus.Played ? f.WinnerClubId() : null))]);
            var inRound = bracket >> i;
            return new BracketRound(stage, [.. Enumerable.Range(1, inRound / 2).Select(n => new BracketTie(n, new BracketSide(null), new BracketSide(null), [], null))]);
        })];
    }
}
