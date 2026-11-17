using Pes17League.Clubs.Application;

namespace Pes17League.Clubs.Tests;

/// <summary>Bước 5 (PO chọn 06/11): bốc thăm league phase kiểu UEFA, nhánh loại trực tiếp cố định theo hạng.</summary>
public class CupDrawTests
{
    /// <summary>Kiểm mọi ràng buộc league phase: mỗi đội 8 trận, mỗi nhóm 1 nhà 1 khách, không lặp đối thủ, không cùng nước (trừ cùng nhóm), ≤ 2 đối thủ/nước.</summary>
    private static void AssertValid<T>(IReadOnlyList<(T Id, string? Country)> seeded, IReadOnlyList<(T Home, T Away)> draw) where T : notnull
    {
        var pots = 4;
        var size = seeded.Count / pots;
        var index = seeded.Select((s, i) => (s.Id, i)).ToDictionary(x => x.Id, x => x.i);
        Assert.Equal(seeded.Count * pots, draw.Count);
        Assert.Equal(draw.Count, draw.Select(m => new HashSet<T> { m.Home, m.Away }).DistinctBy(s => string.Join(",", s.OrderBy(x => index[x]))).Count());
        foreach (var (id, country) in seeded)
        {
            var home = draw.Where(m => m.Home.Equals(id)).Select(m => m.Away).ToList();
            var away = draw.Where(m => m.Away.Equals(id)).Select(m => m.Home).ToList();
            Assert.All(Enumerable.Range(0, pots), q =>
                Assert.Equal((1, 1), (home.Count(o => index[o] / size == q), away.Count(o => index[o] / size == q))));
            var all = home.Concat(away).ToList();
            Assert.DoesNotContain(country, all.Where(o => index[o] / size != index[id] / size).Select(o => seeded[index[o]].Country));
            var opponents = all.Select(o => seeded[index[o]].Country).ToList();
            Assert.All(opponents.GroupBy(c => c), g => Assert.True(g.Count() <= 2, $"{id} gặp {g.Count()} đội {g.Key}"));
        }
    }

    /// <summary>C1 kiểu mùa đầu: 4 nước × 4 đội + 8 CLB lẻ (FRA 3, NED 2, POR 2, BEL 1), xen kẽ trong các nhóm.</summary>
    private static List<(string Id, string? Country)> C1()
    {
        string[] countries = ["ENG", "ESP", "ITA", "GER", "ENG", "ESP", "ITA", "GER", "FRA", "NED", "POR", "FRA", "ENG", "ESP", "ITA", "GER", "NED", "POR", "FRA", "BEL", "ENG", "ESP", "ITA", "GER"];
        return [.. countries.Select((c, i) => ($"{c}{i:00}", (string?)c))];
    }

    [Fact]
    public void League_phase_draw_meets_uefa_constraints_and_is_reproducible()
    {
        var seeded = C1();
        for (var seed = 0; seed < 30; seed++) AssertValid(seeded, CupDraw.LeaguePhase(seeded, 8, new Random(seed)));
        Assert.Equal(CupDraw.LeaguePhase(seeded, 8, new Random(7)), CupDraw.LeaguePhase(seeded, 8, new Random(7)));
        Assert.Throws<ArgumentException>(() => CupDraw.LeaguePhase(seeded.Take(22).ToList(), 8, new Random(0)));
        // 13 đội cùng nước: nhóm 1, 2 toàn ENG → không có đối thủ khác nhóm hợp lệ.
        var crowded = seeded.Select((s, i) => (s.Id, i < 13 ? "ENG" : s.Country)).ToList();
        Assert.Throws<InvalidOperationException>(() => CupDraw.LeaguePhase(crowded, 8, new Random(0)));
    }

    /// <summary>Kiểm 8 vòng league phase: mỗi vòng mỗi đội đúng 1 trận, không quá 2 trận nhà/khách liên tiếp, gộp lại = kết quả bốc thăm hợp lệ.</summary>
    internal static void AssertRounds<T>(IReadOnlyList<(T Id, string? Country)> seeded, IReadOnlyList<IReadOnlyList<(T Home, T Away)>> rounds) where T : notnull
    {
        Assert.Equal(8, rounds.Count);
        AssertValid(seeded, [.. rounds.SelectMany(r => r)]);
        foreach (var r in rounds) Assert.Equal(seeded.Select(s => s.Id).Order(), r.SelectMany(m => new[] { m.Home, m.Away }).Order());
        foreach (var (id, _) in seeded)
        {
            var sides = string.Concat(rounds.Select(r => r.Any(m => m.Home.Equals(id)) ? 'H' : 'A'));
            Assert.DoesNotContain("HHH", sides);
            Assert.DoesNotContain("AAA", sides);
        }
    }

    /// <summary>Bước 6: bốc thăm + chia 8 vòng hợp lệ với 30 mã; cùng mã → cùng lịch.</summary>
    [Fact]
    public void League_phase_splits_into_rounds_with_short_home_away_runs()
    {
        var seeded = C1();
        for (var seed = 0; seed < 30; seed++) AssertRounds(seeded, CupDraw.LeaguePhaseRounds(seeded, 8, new Random(seed)));
        Assert.Equal(CupDraw.LeaguePhaseRounds(seeded, 8, new Random(3)).SelectMany(r => r), CupDraw.LeaguePhaseRounds(seeded, 8, new Random(3)).SelectMany(r => r));
        Assert.Throws<ArgumentException>(() => CupDraw.SplitRounds([("A", "B"), ("B", "C")], 2, new Random(0)));     // A, C chỉ có 1 trận
    }

    [Fact]
    public void Knockout_bracket_follows_fixed_ranking_paths()
    {
        var ranking = Enumerable.Range(1, 24).Select(i => $"R{i}").ToList();
        var playoff = CupDraw.Playoff(ranking, 4, 12);
        Assert.Equal([("R5", "R12"), ("R6", "R11"), ("R7", "R10"), ("R8", "R9")], playoff.Select(t => (t.Higher, t.Lower)));
        Assert.All(playoff, t => Assert.Equal(FixtureStage.KnockoutPlayoff, t.Stage));
        // Đội thứ hạng thấp thắng ở cặp 6–11 và 8–9.
        var winners = new[] { "R5", "R11", "R7", "R9" };
        var qf = CupDraw.FirstRound(ranking, 4, winners, new Random(0));
        Assert.Equal([("R1", "R9"), ("R4", "R5"), ("R2", "R7"), ("R3", "R11")], qf.Select(t => (t.Higher, t.Lower)));
        Assert.All(qf, t => Assert.Equal(FixtureStage.QuarterFinal, t.Stage));
        // Bán kết: thắng (1 vs 9) gặp thắng (4 vs 5); đội xếp trên league phase đá lượt 2 sân nhà.
        var sf = CupDraw.NextRound(["R9", "R4", "R2", "R3"], ranking, new Random(0));
        Assert.Equal([("R4", "R9"), ("R2", "R3")], sf.Select(t => (t.Higher, t.Lower)));
        Assert.All(sf, t => Assert.Equal(FixtureStage.SemiFinal, t.Stage));
        var finals = Enumerable.Range(0, 20).Select(s => CupDraw.NextRound(["R9", "R2"], ranking, new Random(s)).Single()).ToList();
        Assert.All(finals, f => Assert.Equal(FixtureStage.Final, f.Stage));
        Assert.Equal(2, finals.Select(f => f.Higher).Distinct().Count());       // chủ nhà chung kết theo bốc thăm
        Assert.Throws<ArgumentException>(() => CupDraw.Playoff(ranking, 4, 16));
        Assert.Equal([1, 8, 4, 5, 2, 7, 3, 6], CupDraw.BracketOrder(8));
    }

    [Fact]
    public void Single_leg_open_gives_byes_to_top_seeds()
    {
        var seeded = Enumerable.Range(1, 20).Select(i => $"T{i}").ToList();
        Assert.Equal([FixtureStage.RoundOf32, FixtureStage.RoundOf16, FixtureStage.QuarterFinal, FixtureStage.SemiFinal, FixtureStage.Final],
            CupDraw.SingleLegStages(20).Select(s => s.Stage));
        var open = CupDraw.SingleLegOpen(seeded, new Random(1));
        Assert.Equal(4, open.Count);
        Assert.All(open, t => Assert.Equal(FixtureStage.RoundOf32, t.Stage));
        var playing = open.SelectMany(t => new[] { t.Higher, t.Lower }).ToHashSet();
        Assert.Equal(8, playing.Count);
        Assert.Equal(seeded.Take(12), seeded.Where(t => !playing.Contains(t)));   // 12 hạt giống mạnh bye
        Assert.Equal(CupDraw.SingleLegOpen(seeded, new Random(7)), CupDraw.SingleLegOpen(seeded, new Random(7)));
        var next = CupDraw.SingleLegDraw(Enumerable.Range(1, 16).Select(i => $"W{i}").ToList(), new Random(0));
        Assert.Equal(8, next.Count);
        Assert.All(next, t => Assert.Equal(FixtureStage.RoundOf16, t.Stage));
        Assert.Throws<ArgumentException>(() => CupDraw.SingleLegDraw(Enumerable.Range(1, 6).Select(i => $"X{i}").ToList(), new Random(0)));
    }

    [Fact]
    public void Tie_winner_uses_aggregate_then_away_goals_then_league_phase_rank()
    {
        var tie = new KnockoutTie<string>(FixtureStage.QuarterFinal, "A", "B");    // lượt 1 B chủ nhà, lượt 2 A chủ nhà
        Assert.Equal("B", CupDraw.TieWinner(tie, (2, 0), (1, 0)));                 // tổng 2–1
        Assert.Equal("A", CupDraw.TieWinner(tie, (1, 1), (2, 0)));                 // tổng 3–1
        Assert.Equal("A", CupDraw.TieWinner(tie, (2, 1), (1, 0)));                 // 2–2, A 1 bàn sân khách, B 0
        Assert.Equal("B", CupDraw.TieWinner(tie, (1, 0), (2, 1)));                 // 2–2, B 1 bàn sân khách, A 0
        Assert.Equal("A", CupDraw.TieWinner(tie, (1, 1), (1, 1)));                 // bằng hết → xếp trên league phase
    }

    /// <summary>Đội dự cúp thật mùa đầu (patch PES6VN 25-26, chia theo OVR): C1, C2 đều bốc thăm được với nhiều hạt ngẫu nhiên.
    /// C1 nhóm 2 có 4 đội Ý → cần ngoại lệ cùng nhóm.</summary>
    [DbFact]
    public async Task Real_first_season_cups_can_be_drawn()
    {
        await using var t = await TestDb.CreateAsync();
        await using var db = t.Context();
        var s1 = (await new FederationSetupService(db, t.Store, t.Clock).SetupAsync("PES6VN 25-26", Env.OrigOptionFile, "Mùa 1", "test")).SeasonId;
        await new CompetitionAdminService(db, t.Clock).SeedDefaultsAsync("test");
        var plan = await new DivisionService(db, t.Clock).PreviewAsync(s1);
        var cups = plan.Editions.Where(e => e.Competition.Type == CompetitionType.ContinentalCup).ToList();
        Assert.Equal(2, cups.Count);
        foreach (var cup in cups)
        {
            var seeded = cup.Clubs.OrderBy(c => c.Seed).Select(c => (c.Club.ClubId, c.Club.Country)).ToList();
            Assert.Equal(24, seeded.Count);
            for (var seed = 0; seed < 20; seed++) AssertValid(seeded, CupDraw.LeaguePhase(seeded, cup.Competition.Rules.LeaguePhaseMatches!.Value, new Random(seed)));
        }
    }
}
