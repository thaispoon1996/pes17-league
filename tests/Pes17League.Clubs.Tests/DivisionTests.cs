using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Application;

namespace Pes17League.Clubs.Tests;

/// <summary>Bước 4 phần A (PO duyệt 06/11): chia hạng + đội dự cúp mùa đầu theo OVR.</summary>
public class DivisionTests
{
    private static List<Competition> Defaults()
    {
        var list = CompetitionAdminService.DefaultLeagues.SelectMany(l => new[]
        {
            new Competition { Name = l.Top, Country = l.Country, Tier = 1, Type = CompetitionType.DomesticLeague, Rules = CompetitionAdminService.TopTierRules },
            new Competition { Name = l.Second, Country = l.Country, Tier = 2, Type = CompetitionType.DomesticLeague, Rules = CompetitionAdminService.SecondTierRules },
        }).ToList();
        for (var i = 0; i < list.Count; i += 2) (list[i].RelegatesToId, list[i + 1].PromotesToId) = (list[i + 1].Id, list[i].Id);
        list.AddRange(CompetitionAdminService.DefaultDomesticCups.Select(c =>
            new Competition { Name = c.Name, ShortName = c.Short, Country = c.Country, Type = CompetitionType.DomesticCup, Rules = CompetitionAdminService.DomesticCupRules }));
        list.Add(new() { Name = "Champions League", Tier = 1, Type = CompetitionType.ContinentalCup, Rules = CompetitionAdminService.CupRules(8) });
        list.Add(new() { Name = "Europa League", Tier = 2, Type = CompetitionType.ContinentalCup, Rules = CompetitionAdminService.CupRules(15) });
        return list;
    }

    /// <summary>BXH mùa trước: hạng 1 mỗi nước = 10 CLB đầu ("{nước}00".."09"), hạng 2 = còn lại; đứng thứ i có 40 − i trận thắng.
    /// GER06 hiệu số +5 → đội thứ 7 tốt nhất.</summary>
    private static Dictionary<Guid, IReadOnlyList<StandingRow<Guid>>> Tables(List<Competition> comps, List<ClubStrength> clubs)
    {
        var t = new Dictionary<Guid, IReadOnlyList<StandingRow<Guid>>>();
        foreach (var league in comps.Where(c => c.Type == CompetitionType.DomesticLeague))
        {
            var pool = clubs.Where(c => c.Country == league.Country).OrderBy(c => c.Name, StringComparer.Ordinal).ToList();
            var rows = (league.Tier == 1 ? pool.Take(10) : pool.Skip(10))
                .Select((c, i) => new StandingRow<Guid>(c.ClubId, 38, 40 - i, 0, 0, c.Name == "GER06" ? 5 : 0, 0)).ToList();
            t[league.Id] = rows;
        }
        return t;
    }

    /// <summary>CLB lẻ: L00–07 dự C1, L08–22 dự C2. L08 = c2Champion ? vô địch C2 : thua chung kết; L09 bán kết; L00 tứ kết; còn lại dừng ở league phase.</summary>
    private static List<CupRecord> Cups(List<ClubStrength> clubs, Guid? c2Champion)
    {
        var id = clubs.ToDictionary(c => c.Name, c => c.ClubId);
        var list = Enumerable.Range(0, 23).Select(i => new CupRecord(id[$"L{i:00}"], i < 8 ? 1 : 2,
            i switch { 8 => c2Champion is null ? CupReach.Champion : CupReach.Final, 9 => CupReach.SemiFinal, 0 => CupReach.QuarterFinal, _ => CupReach.LeaguePhase },
            i < 8 ? 20 - i : 30, 0, 0)).ToList();
        if (c2Champion is { } ch) list.Add(new CupRecord(ch, 2, CupReach.Champion, 10, 0, 0));
        return list;
    }

    /// <summary>CLB "{nước}{i}" có OVR 90 − i (i = 0 mạnh nhất); ENG mạnh hơn nước khác 0,5 cùng thứ hạng.</summary>
    private static List<ClubStrength> Clubs(int loose = 23)
    {
        var list = new List<ClubStrength>();
        foreach (var (country, n) in new[] { ("ENG", 20), ("ESP", 20), ("ITA", 20), ("GER", 18) })
            list.AddRange(Enumerable.Range(0, n).Select(i => new ClubStrength(Guid.NewGuid(), $"{country}{i:00}", country, 90 - i + (country == "ENG" ? 0.5 : 0), 25)));
        list.AddRange(Enumerable.Range(0, loose).Select(i => new ClubStrength(Guid.NewGuid(), $"L{i:00}", i % 2 == 0 ? "FRA" : "NED", 85 - i, 25)));
        list.Add(new(Guid.NewGuid(), "Alpha", null, 99, 25));
        return list;
    }

    private static string[] Names(SeasonAllocation a, string competition) =>
        [.. a.Editions.Single(e => e.Competition.Name == competition).Clubs.OrderBy(x => x.Seed).Select(x => x.Club.Name)];

    [Fact]
    public void First_season_splits_by_ovr_and_fills_cups()
    {
        var a = DivisionService.AllocateFirstSeason(Defaults(), Clubs());
        Assert.Empty(a.Errors);
        Assert.Equal("Alpha", Assert.Single(a.Unassigned).Name);
        Assert.Equal(14, a.Editions.Count);
        Assert.Equal(Enumerable.Range(0, 10).Select(i => $"ENG{i:00}"), Names(a, "Premier League"));
        Assert.Equal(Enumerable.Range(10, 10).Select(i => $"ENG{i:00}"), Names(a, "Championship"));
        Assert.Equal((10, 8), (Names(a, "Bundesliga").Length, Names(a, "2. Bundesliga").Length));
        Assert.Equal(20, Names(a, "FA Cup").Length);
        Assert.Equal(18, Names(a, "DFB-Pokal").Length);
        Assert.Equal(Enumerable.Range(0, 20).Select(i => $"ENG{i:00}"), Names(a, "FA Cup").Order());
        Assert.All(a.Editions, e => Assert.Equal(Enumerable.Range(1, e.Clubs.Count), e.Clubs.Select(x => x.Seed).Order()));

        var c1 = Names(a, "Champions League");
        Assert.Equal(24, c1.Length);
        Assert.Equal(["ENG00", "ESP00", "GER00", "ITA00"], c1[..4].Order());
        Assert.Equal(Enumerable.Range(0, 8).Select(i => $"L{i:00}"), c1.Where(n => n[0] == 'L').Order());
        Assert.DoesNotContain(c1, n => n is "ENG04" or "GER04");
        var c2 = Names(a, "Europa League");
        Assert.Equal(24, c2.Length);
        Assert.Equal(Enumerable.Range(8, 15).Select(i => $"L{i:00}"), c2.Where(n => n[0] == 'L').Order());
        Assert.Contains("GER05", c2);
        Assert.Contains("ENG06", c2);                  // suất thêm: CLB quốc nội OVR cao nhất chưa có suất (ENG06 84,5 > ESP06 84)
        Assert.Equal(9, c2.Count(n => n[0] != 'L'));
        Assert.Empty(c1.Intersect(c2));
    }

    [Fact]
    public void Loose_club_count_must_match_cup_places()
    {
        var a = DivisionService.AllocateFirstSeason(Defaults(), Clubs(loose: 22));
        Assert.Contains(a.Errors, e => e.Contains("22 CLB lẻ") && e.Contains("23 suất"));
    }

    [Fact]
    public void Next_season_promotes_relegates_and_ranks_cups_from_previous_results()
    {
        var comps = Defaults();
        var clubs = Clubs();
        var id = clubs.ToDictionary(c => c.Name, c => c.ClubId);
        var tables = Tables(comps, clubs);
        clubs.Add(new(Guid.NewGuid(), "ENG99", "ENG", 70, 25));          // CLB mới, mùa trước chưa dự giải nào
        var a = DivisionService.AllocateNextSeason(comps, clubs, tables, Cups(clubs, id["ENG13"]));
        Assert.Empty(a.Errors);
        Assert.True(a.FromPreviousSeason);
        Assert.Equal(["Alpha", "ENG99"], a.Unassigned.Select(c => c.Name).Order());
        Assert.Equal([.. Enumerable.Range(0, 8).Select(i => $"ENG{i:00}"), "ENG10", "ENG11"], Names(a, "Premier League").Order());
        Assert.Equal([.. Enumerable.Range(8, 2).Concat(Enumerable.Range(12, 8)).Select(i => $"ENG{i:00}")], Names(a, "Championship").Order());
        Assert.Equal((10, 8), (Names(a, "Bundesliga").Length, Names(a, "2. Bundesliga").Length));
        Assert.Contains("GER11", Names(a, "Bundesliga"));
        Assert.Contains(a.Notes, n => n == "Xuống Championship: ENG08, ENG09");

        var c1 = Names(a, "Champions League");
        Assert.Equal(24, c1.Length);
        Assert.Equal([.. new[] { "ENG", "ESP", "GER", "ITA" }.SelectMany(c => Enumerable.Range(0, 4).Select(i => $"{c}{i:00}"))], c1.Where(n => n[0] != 'L').Order());
        Assert.Equal(["L00", "L01", "L02", "L03", "L04", "L05", "L08", "L09"], c1.Where(n => n[0] == 'L').Order());   // vòng xa nhất trước, cùng vòng C1 trên C2
        var c2 = Names(a, "Europa League");
        Assert.Equal(24, c2.Length);
        Assert.Equal(["L06", "L07", .. Enumerable.Range(10, 13).Select(i => $"L{i:00}")], c2.Where(n => n[0] == 'L').Order());
        Assert.Contains("ENG13", c2);                                     // vô địch C2 (đang ở hạng 2) nhận suất thêm
        Assert.DoesNotContain("GER06", c2);
        Assert.Contains(a.Notes, n => n.Contains("ENG13 (vô địch mùa trước)"));

        // Vô địch C2 là CLB lẻ (đã có suất) → suất thêm cho đội thứ 7 tốt nhất: GER06 (hiệu số +5).
        var b = DivisionService.AllocateNextSeason(comps, clubs, tables, Cups(clubs, null));
        Assert.Empty(b.Errors);
        Assert.Contains("GER06", Names(b, "Europa League"));
        Assert.DoesNotContain("ENG13", Names(b, "Europa League"));
        Assert.Contains(b.Notes, n => n.Contains("L08 đã có suất"));
    }

    /// <summary>Dữ liệu thật: patch PES6VN 25-26 qua FederationSetupService → 14 giải, mỗi CLB 1 giải quốc gia + cúp quốc gia, 2 cúp châu lục đủ 24 đội.</summary>
    [DbFact]
    public async Task Apply_writes_draft_editions_and_can_redo_until_scheduled()
    {
        await using var t = await TestDb.CreateAsync();
        Guid seasonId;
        await using (var db = t.Context())
        {
            seasonId = (await new FederationSetupService(db, t.Store, t.Clock).SetupAsync("PES6VN 25-26", Env.OrigOptionFile, "Mùa 1", "test")).SeasonId;
            await new CompetitionAdminService(db, t.Clock).SeedDefaultsAsync("test");
            var svc = new DivisionService(db, t.Clock);
            var plan = await svc.PreviewAsync(seasonId);
            Assert.Empty(plan.Errors);
            Assert.All(plan.Editions.SelectMany(e => e.Clubs), x => Assert.True(x.Club.Players >= 11 && x.Club.Ovr > 40, x.Club.Name));
            Assert.Equal(14, await svc.ApplyAsync(seasonId, "test"));
            Assert.Equal(14, await svc.ApplyAsync(seasonId, "test"));     // còn Draft → chia lại, ghi đè
        }
        await using (var db = t.Context())
        {
            var rows = await (from ec in db.EditionClubs join e in db.Editions on ec.EditionId equals e.Id
                              join c in db.Competitions on e.CompetitionId equals c.Id
                              where e.SeasonId == seasonId select new { c.Name, c.Type, ec.ClubId, ec.Seed }).ToListAsync();
            var count = rows.GroupBy(r => r.Name).ToDictionary(g => g.Key, g => g.Count());
            Assert.Equal((10, 10, 10, 8, 24, 24, 20, 18), (count["Premier League"], count["Championship"], count["Bundesliga"], count["2. Bundesliga"],
                count["Champions League"], count["Europa League"], count["FA Cup"], count["DFB-Pokal"]));
            Assert.Equal(78, rows.Count(r => r.Type == CompetitionType.DomesticLeague));
            Assert.Equal(78, rows.Where(r => r.Type == CompetitionType.DomesticLeague).Select(r => r.ClubId).Distinct().Count());
            Assert.Equal(48, rows.Where(r => r.Type == CompetitionType.ContinentalCup).Select(r => r.ClubId).Distinct().Count());
            Assert.Equal(78, rows.Where(r => r.Type == CompetitionType.DomesticCup).Select(r => r.ClubId).Distinct().Count());
            Assert.Equal(28, await db.AdminAudit.CountAsync(x => x.Action == "AllocateByOvr"));

            var pl = await db.Editions.SingleAsync(e => e.SeasonId == seasonId && db.Competitions.Any(c => c.Id == e.CompetitionId && c.Name == "Premier League"));
            pl.Status = EditionStatus.Scheduled;
            await db.SaveChangesAsync();
            await Assert.ThrowsAsync<AdminRuleException>(() => new DivisionService(db, t.Clock).ApplyAsync(seasonId, "test"));
        }
    }

    /// <summary>Phần B trên DB: mùa trước đã chia theo OVR, có trận giải + chung kết 2 cúp → mùa sau lên/xuống hạng theo BXH.</summary>
    [DbFact]
    public async Task Next_season_reads_previous_standings_and_cup_finals()
    {
        await using var t = await TestDb.CreateAsync();
        await using var db = t.Context();
        var s1 = (await new FederationSetupService(db, t.Store, t.Clock).SetupAsync("PES6VN 25-26", Env.OrigOptionFile, "Mùa 1", "test")).SeasonId;
        await new CompetitionAdminService(db, t.Clock).SeedDefaultsAsync("test");
        var svc = new DivisionService(db, t.Clock);
        await svc.ApplyAsync(s1, "test");
        var season1 = await db.Seasons.SingleAsync(s => s.Id == s1);
        var s2 = await new CompetitionService(db, t.Store, t.Clock).CreateSeasonAsync("Mùa 2", season1.Number + 1);
        s2.PatchVersionId = season1.PatchVersionId;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<AdminRuleException>(() => svc.PreviewAsync(s2.Id));       // mùa trước chưa kết thúc

        var ed = await (from e in db.Editions join c in db.Competitions on e.CompetitionId equals c.Id where e.SeasonId == s1 select new { e, c.Name }).ToListAsync();
        var members = await (from ec in db.EditionClubs join c in db.Clubs on ec.ClubId equals c.Id select new { ec.EditionId, ec.ClubId, c.Name }).ToListAsync();
        List<(Guid Id, string Name)> Of(string comp) => [.. members.Where(m => m.EditionId == ed.Single(x => x.Name == comp).e.Id).Select(m => (m.ClubId, m.Name)).OrderBy(m => m.Name, StringComparer.Ordinal)];
        var md = new Matchday { SeasonId = s1, Number = 1 };
        db.Add(md);
        Fixture Played(string comp, Guid home, Guid away, int hg, int ag, FixtureStage stage, (int Home, int Away)? pens = null) => new()
        {
            MatchdayId = md.Id, HomeClubId = home, AwayClubId = away, HomeGoals = hg, AwayGoals = ag, Status = FixtureStatus.Played,
            CompetitionEditionId = ed.Single(x => x.Name == comp).e.Id, Stage = stage, HomePenalties = pens?.Home, AwayPenalties = pens?.Away,
        };
        // Không có trận → bằng điểm, xếp theo tên. Đội cuối tên thắng đội đầu tên 3–0: đội đầu tên xuống cuối bảng.
        var pl = Of("Premier League");
        var (c1, c2) = (Of("Champions League"), Of("Europa League"));
        var champion = Of("Championship")[^1];
        var c2Champion = c2.First(c => !pl.Contains(c) && members.Any(m => m.ClubId == c.Id && ed.Any(x => x.e.Id == m.EditionId && x.Name == "Bundesliga")));
        db.Fixtures.AddRange(Played("Premier League", pl[^1].Id, pl[0].Id, 3, 0, FixtureStage.League),
            Played("Championship", champion.Id, Of("Championship")[0].Id, 1, 0, FixtureStage.League),
            Played("Champions League", c1[0].Id, c1[1].Id, 2, 1, FixtureStage.Final),
            Played("Europa League", c2[0].Id == c2Champion.Id ? c2[1].Id : c2[0].Id, c2Champion.Id, 1, 1, FixtureStage.Final, pens: (2, 4)));   // hoà → thắng penalty
        foreach (var x in ed) x.e.Status = EditionStatus.Finished;
        await db.SaveChangesAsync();

        var plan = await svc.PreviewAsync(s2.Id);
        Assert.Empty(plan.Errors);
        Assert.True(plan.FromPreviousSeason);
        var next = plan.Editions.ToDictionary(e => e.Competition.Name, e => e.Clubs.Select(x => x.Club.ClubId).ToHashSet());
        Assert.Equal((10, 10, 10, 8, 24, 24), (next["Premier League"].Count, next["Championship"].Count, next["Bundesliga"].Count, next["2. Bundesliga"].Count,
            next["Champions League"].Count, next["Europa League"].Count));
        Assert.DoesNotContain(pl[0].Id, next["Premier League"]);          // thua 0–3 → bét bảng → xuống
        Assert.DoesNotContain(pl[^2].Id, next["Premier League"]);         // cuối tên trong nhóm 0 điểm → xuống
        Assert.Contains(pl[^1].Id, next["Premier League"]);
        Assert.Contains(champion.Id, next["Premier League"]);             // đầu Championship → lên
        Assert.Contains(pl[^1].Id, next["Champions League"]);
        // Vô địch C2 có suất cúp đúng 1 lần: suất thêm C2, hoặc đã có suất theo thứ hạng (2 nhánh đã kiểm ở test dữ liệu giả).
        Assert.Equal(1, new[] { "Champions League", "Europa League" }.Count(c => next[c].Contains(c2Champion.Id)));
        Assert.Contains(plan.Notes, n => n.Contains($"{c2Champion.Name} (vô địch mùa trước)") || n.Contains($"vô địch mùa trước {c2Champion.Name} đã có suất"));

        Assert.Equal(14, await svc.ApplyAsync(s2.Id, "test"));
        Assert.Equal(14, await db.AdminAudit.CountAsync(x => x.Action == "AllocateFromPrevious"));
    }
}
