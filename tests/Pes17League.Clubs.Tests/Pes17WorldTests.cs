using Pes17League.Clubs.Application;

namespace Pes17League.Clubs.Tests;

/// <summary>Career PES17 (PO 19/11/2026): giải giữ thành viên như game, C1/C2 thể thức mới theo OVR, CLB lẻ dư, 2 kỳ chuyển nhượng.</summary>
public class Pes17WorldTests
{
    private static ClubStrength Club(string name, string? country, double ovr) => new(Guid.NewGuid(), name, country, ovr, 25);

    [Fact]
    public void Fixed_leagues_keep_game_membership_and_cups_take_strongest_tier1_clubs()
    {
        var w = (Eng1: new Pes17WorldLeague(9, "ENG", 1, "Premier League", 2, "eng1", []), Eng2: new Pes17WorldLeague(66, "ENG", 2, "Championship", 2, "eng2", []),
            Bra: new Pes17WorldLeague(21, "BRA", 1, "Brasileirão", 2, "bra", []));
        var top = new Competition { Name = "Premier League", Country = "ENG", Tier = 1, Type = CompetitionType.DomesticLeague, Rules = Pes17WorldService.LeagueRules(w.Eng1, true) };
        var second = new Competition { Name = "Championship", Country = "ENG", Tier = 2, Type = CompetitionType.DomesticLeague, Rules = Pes17WorldService.LeagueRules(w.Eng2, true) };
        (top.RelegatesToId, second.PromotesToId) = (second.Id, top.Id);
        var bra = new Competition { Name = "Brasileirão", Country = "BRA", Tier = 1, Type = CompetitionType.DomesticLeague, Rules = Pes17WorldService.LeagueRules(w.Bra, false) };
        var cup = new Competition { Name = "FA Cup", Country = "ENG", Type = CompetitionType.DomesticCup, Rules = CompetitionAdminService.DomesticCupRules };
        var c1 = new Competition { Name = "Champions League", Tier = 1, Type = CompetitionType.ContinentalCup, Rules = Pes17WorldService.NewFormatCupRules(2) with { LeaguePhaseTeams = 8, MaxClubsPerCountry = null } };
        var c2 = new Competition { Name = "Europa League", Tier = 2, Type = CompetitionType.ContinentalCup, Rules = Pes17WorldService.NewFormatCupRules(1) with { LeaguePhaseTeams = 5, MaxClubsPerCountry = null } };
        Assert.Equal((4, 2, 3, 0), (top.Rules.ContinentalPlaces, top.Rules.SecondaryContinentalPlaces, top.Rules.RelegationPlaces, top.Rules.PromotionPlaces));
        Assert.Equal((0, 3), (second.Rules.ContinentalPlaces, second.Rules.PromotionPlaces));
        Assert.Equal((0, 0, 0), (bra.Rules.ContinentalPlaces, bra.Rules.SecondaryContinentalPlaces, bra.Rules.RelegationPlaces));

        // Hạng 1 gồm E0..E9 + Yếu (OVR thấp nhất); hạng 2 gồm Mạnh (OVR cao nhất nước Anh) + E10..E14. Nam Mỹ mạnh hơn mọi CLB châu Âu.
        var eng1 = Enumerable.Range(0, 10).Select(i => Club($"E{i}", "ENG", 85 - i)).Append(Club("Yếu", "ENG", 50)).ToList();
        var eng2 = Enumerable.Range(10, 5).Select(i => Club($"E{i}", "ENG", 85 - i)).Prepend(Club("Mạnh", "ENG", 99)).ToList();
        var brazil = Enumerable.Range(0, 4).Select(i => Club($"B{i}", "BRA", 95 - i)).ToList();
        var loose = Enumerable.Range(0, 5).Select(i => Club($"L{i}", "TUR", 70 - i)).ToList();
        var clubs = eng1.Concat(eng2).Concat(brazil).Concat(loose).Append(Club("USA", null, 99)).ToList();
        var ids = (IReadOnlyCollection<Guid> x) => x;
        var fixedLeagues = new Dictionary<Guid, IReadOnlyCollection<Guid>>
        {
            [top.Id] = ids([.. eng1.Select(c => c.ClubId)]), [second.Id] = ids([.. eng2.Select(c => c.ClubId)]), [bra.Id] = ids([.. brazil.Select(c => c.ClubId)]),
        };

        var a = DivisionService.AllocateFirstSeason([top, second, bra, cup, c1, c2], clubs, fixedLeagues);
        Assert.Empty(a.Errors);
        string[] Of(Competition c) => [.. a.Editions.Single(e => e.Competition == c).Clubs.OrderBy(x => x.Seed).Select(x => x.Club.Name)];
        Assert.Equal([.. eng1.Select(c => c.Name)], Of(top));                 // giữ nguyên như game, hạt giống theo OVR
        Assert.Equal([.. eng2.Select(c => c.Name)], Of(second));
        Assert.Equal(17, Of(cup).Length);                                    // cúp quốc gia: hạng 1 + 2
        Assert.Equal(["E0", "E1", "E2", "E3", "E6", "E7", "L0", "L1"], Of(c1));   // 4 suất + 2 CLB lẻ + suất thêm OVR (chỉ CLB hạng 1); hạt giống theo OVR
        Assert.Equal(["E4", "E5", "E8", "E9", "L2"], Of(c2));
        Assert.DoesNotContain(a.Editions.Where(e => e.Competition.Type == CompetitionType.ContinentalCup).SelectMany(e => e.Clubs),
            x => x.Club.Name is "Mạnh" or "B0" or "USA" or "L3");
        Assert.Contains(a.Notes, n => n.StartsWith("CLB lẻ: 3/5"));
        Assert.Equal("USA", Assert.Single(a.Unassigned).Name);
    }

    [Fact]
    public void Cup_caps_clubs_per_country_for_loose_and_extra_places()
    {
        var eng = new Competition { Name = "Premier League", Country = "ENG", Tier = 1, Type = CompetitionType.DomesticLeague, Rules = new() { ContinentalPlaces = 2 } };
        var ned = new Competition { Name = "Eredivisie", Country = "NED", Tier = 1, Type = CompetitionType.DomesticLeague, Rules = new() { ContinentalPlaces = 1 } };
        var c1 = new Competition { Name = "Champions League", Tier = 1, Type = CompetitionType.ContinentalCup,
            Rules = Pes17WorldService.NewFormatCupRules(2) with { LeaguePhaseTeams = 7, MaxClubsPerCountry = 3 } };
        var engClubs = Enumerable.Range(0, 6).Select(i => Club($"E{i}", "ENG", 90 - i)).ToList();
        var nedClubs = Enumerable.Range(0, 4).Select(i => Club($"N{i}", "NED", 70 - i)).ToList();
        var loose = new[] { Club("T0", "TUR", 80), Club("T1", "TUR", 79), Club("T2", "TUR", 78), Club("T3", "TUR", 77), Club("B0", "BEL", 60) };
        var fixedLeagues = new Dictionary<Guid, IReadOnlyCollection<Guid>> { [eng.Id] = [.. engClubs.Select(c => c.ClubId)], [ned.Id] = [.. nedClubs.Select(c => c.ClubId)] };
        var a = DivisionService.AllocateFirstSeason([eng, ned, c1], [.. engClubs, .. nedClubs, .. loose], fixedLeagues);
        Assert.Empty(a.Errors);
        var cup = a.Editions.Single(e => e.Competition == c1).Clubs.Select(x => x.Club.Name).Order().ToArray();
        Assert.Equal(["E0", "E1", "E2", "N0", "N1", "T0", "T1"], cup);    // ENG 2 + E2 (trần 3), NED 1 + N1; T2 không có suất thêm vì không thuộc giải
        Assert.Contains(a.Notes, n => n == "Champions League: đủ trần 3 CLB/nước — ENG");
    }

    [Fact]
    public void League_phase_draw_rejects_hopeless_country_distribution_quickly()
    {
        var seeded = Enumerable.Range(0, 36).Select(i => (Id: i, Country: (string?)(i < 10 ? "ENG" : $"C{i % 9}"))).ToList();
        var ex = Assert.Throws<ArgumentException>(() => CupDraw.LeaguePhase(seeded, 8, new Random(1)));
        Assert.Contains("10 đội ENG", ex.Message);
        Assert.Equal(36 * 4, CupDraw.LeaguePhase([.. seeded.Select(s => (s.Id, s.Id < 5 ? "ENG" : $"C{s.Id % 9}"))], 8, new Random(1)).Count);
    }

    [Fact]
    public void Fixed_league_with_inactive_member_is_an_error()
    {
        var l = new Competition { Name = "Eredivisie", Country = "NED", Tier = 1, Type = CompetitionType.DomesticLeague, Rules = new() };
        var clubs = new[] { Club("A", "NED", 70), Club("B", "NED", 69) };
        var a = DivisionService.AllocateFirstSeason([l], clubs, new Dictionary<Guid, IReadOnlyCollection<Guid>> { [l.Id] = [clubs[0].ClubId, clubs[1].ClubId, Guid.NewGuid()] });
        Assert.Contains(a.Errors, e => e.Contains("Eredivisie: 1 CLB"));
    }

    [Theory]
    [InlineData(1, 38, true)]
    [InlineData(6, 38, true)]
    [InlineData(7, 38, false)]
    [InlineData(19, 38, false)]
    [InlineData(20, 38, true)]    // kỳ đông: 4 khung từ giữa mùa
    [InlineData(23, 38, true)]
    [InlineData(24, 38, false)]
    [InlineData(38, 38, false)]
    public void Two_transfer_windows_summer_and_winter(int slot, int total, bool open) =>
        Assert.Equal(open, Pes17TransferWindows.IsOpen(slot, total));
}
