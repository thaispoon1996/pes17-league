using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Application;

namespace Pes17League.Clubs.Tests;

/// <summary>Admin giải đấu — bước 3a (docs/domain-admin-design.md mục 5, 9).</summary>
public class CompetitionAdminTests
{
    [DbFact]
    public async Task Seed_creates_fourteen_linked_competitions_with_valid_rules()
    {
        await using var t = await TestDb.CreateAsync();
        await using (var db = t.Context())
            Assert.Equal(14, await new CompetitionAdminService(db, t.Clock).SeedDefaultsAsync("test"));
        await using (var db = t.Context())
        {
            var all = await db.Competitions.ToListAsync();
            var by = all.ToDictionary(c => c.Name);
            Assert.Equal(((Guid?)by["Championship"].Id, (Guid?)by["Premier League"].Id), (by["Premier League"].RelegatesToId, by["Championship"].PromotesToId));
            Assert.Equal(("GER", (int?)2, "GER 2"), (by["2. Bundesliga"].Country, by["2. Bundesliga"].Tier, by["2. Bundesliga"].ShortName));
            Assert.Equal((CompetitionType.DomesticCup, "ENG", (int?)null, 1), (by["FA Cup"].Type, by["FA Cup"].Country, by["FA Cup"].Tier, by["FA Cup"].Rules.Legs));
            Assert.All(all, c => Assert.Empty(CompetitionAdminService.Validate(c.Type, c.Rules)));
            Assert.Empty(CompetitionAdminService.Warnings(all));

            var c1 = by["Champions League"];
            Assert.Equal((CompetitionType.ContinentalCup, (int?)1, (16, 8)), (c1.Type, c1.Tier, CompetitionAdminService.CupEntrants(c1, all)));
            Assert.Equal((8, 15), CompetitionAdminService.CupEntrants(by["Europa League"], all));
            // Thứ 1–4 vào thẳng + 4 đội thắng play-off = tứ kết; play-off 8 + tứ kết 8 + bán kết 4 + chung kết 1 = 21 trận.
            Assert.Equal(((int?)8, "Tứ kết", (int?)21), (CompetitionAdminService.KnockoutTeams(c1.Rules), CompetitionAdminService.KnockoutStageName(8),
                CompetitionAdminService.KnockoutMatches(c1.Rules)));
            Assert.Equal(14, await db.AdminAudit.CountAsync(a => a.Action == "Create"));
            await Assert.ThrowsAsync<AdminRuleException>(() => new CompetitionAdminService(db, t.Clock).SeedDefaultsAsync("test"));
            await Assert.ThrowsAsync<AdminRuleException>(() => new CompetitionAdminService(db, t.Clock).SeedDomesticCupsAsync("test"));
        }
    }

    [Fact]
    public void Cup_rules_need_even_playoff_and_power_of_two_bracket()
    {
        var ok = CompetitionAdminService.CupRules(8);
        Assert.Empty(CompetitionAdminService.Validate(CompetitionType.ContinentalCup, ok));
        var r16 = ok with { DirectKnockoutPlaces = 8, PlayoffPlaces = 24 };     // phương án (b): vòng 1/8 thật, 45 trận loại trực tiếp
        Assert.Equal(((int?)16, (int?)45), (CompetitionAdminService.KnockoutTeams(r16), CompetitionAdminService.KnockoutMatches(r16)));
        Assert.Empty(CompetitionAdminService.Validate(CompetitionType.ContinentalCup, r16));
        Assert.NotEmpty(CompetitionAdminService.Validate(CompetitionType.ContinentalCup, ok with { PlayoffPlaces = 11 }));   // 7 đội play-off
        Assert.NotEmpty(CompetitionAdminService.Validate(CompetitionType.ContinentalCup, ok with { DirectKnockoutPlaces = 6 }));   // 6 + 3 = 9
        Assert.NotEmpty(CompetitionAdminService.Validate(CompetitionType.ContinentalCup, ok with { LeaguePhaseMatches = 7 }));
        Assert.NotEmpty(CompetitionAdminService.Validate(CompetitionType.ContinentalCup, ok with { LeaguePhaseMatches = 24 }));
        Assert.NotEmpty(CompetitionAdminService.Validate(CompetitionType.ContinentalCup, ok with { LeaguePhaseTeams = null }));
        Assert.NotEmpty(CompetitionAdminService.Validate(CompetitionType.DomesticLeague, new() { PointsWin = 1, PointsDraw = 1 }));
        Assert.Empty(CompetitionAdminService.Validate(CompetitionType.DomesticLeague, CompetitionAdminService.TopTierRules));
        Assert.Empty(CompetitionAdminService.Validate(CompetitionType.DomesticCup, CompetitionAdminService.DomesticCupRules));
        Assert.NotEmpty(CompetitionAdminService.Validate(CompetitionType.DomesticCup, new() { Legs = 2 }));
    }

    [DbFact]
    public async Task Links_and_cup_tiers_are_validated_and_delete_falls_back_to_archive()
    {
        await using var t = await TestDb.CreateAsync();
        await using var db = t.Context();
        var svc = new CompetitionAdminService(db, t.Clock);
        await svc.SeedDefaultsAsync("test");
        var pl = await db.Competitions.SingleAsync(c => c.Name == "Premier League");
        var ch = await db.Competitions.SingleAsync(c => c.Name == "Championship");

        // Có suất xuống hạng mà không có giải hạng dưới; liên kết sang nước khác; cúp hạng 1 thứ hai.
        await Assert.ThrowsAsync<AdminRuleException>(() => svc.CreateAsync(new("Eredivisie", null, "NED", 1, CompetitionType.DomesticLeague, null,
            CompetitionAdminService.TopTierRules), "test"));
        await Assert.ThrowsAsync<AdminRuleException>(() => svc.CreateAsync(new("Eredivisie", null, "NED", 1, CompetitionType.DomesticLeague, null,
            CompetitionAdminService.TopTierRules, RelegatesToId: ch.Id), "test"));
        await Assert.ThrowsAsync<AdminRuleException>(() => svc.CreateAsync(new("Super Cup", null, null, 1, CompetitionType.ContinentalCup, null,
            CompetitionAdminService.CupRules(0)), "test"));
        await Assert.ThrowsAsync<AdminRuleException>(() => svc.UpdateAsync(pl.Id, CompetitionAdminService.Input(pl) with { Name = "Championship" }, "test"));

        var ned = await svc.CreateAsync(new(" Eredivisie ", "NED 1", "ned", 1, CompetitionType.DomesticLeague, null, new()), "test");
        Assert.Equal(("Eredivisie", "NED"), (ned.Name, ned.Country));
        await Assert.ThrowsAsync<AdminRuleException>(() => svc.DeleteOrArchiveAsync(ch.Id, "test"));    // Premier League còn trỏ xuống
        Assert.True(await svc.DeleteOrArchiveAsync(ned.Id, "test"));
        Assert.False(await db.Competitions.AnyAsync(c => c.Name == "Eredivisie"));

        // Có mùa giải → lưu trữ thay vì xoá; không đổi loại được; khôi phục lại.
        var season = await new CompetitionService(db, t.Store, t.Clock).CreateSeasonAsync("Mùa 1", 1);
        await svc.CreateEditionAsync(pl.Id, season.Id, "test");
        await Assert.ThrowsAsync<AdminRuleException>(() => svc.UpdateAsync(pl.Id, CompetitionAdminService.Input(pl) with
            { Type = CompetitionType.ContinentalCup }, "test"));
        Assert.False(await svc.DeleteOrArchiveAsync(pl.Id, "test"));
        Assert.Equal(CompetitionStatus.Archived, (await db.Competitions.SingleAsync(c => c.Id == pl.Id)).Status);
        Assert.Contains("Championship: giải hạng trên đã lưu trữ", CompetitionAdminService.Warnings(await db.Competitions.ToListAsync()));
        await Assert.ThrowsAsync<AdminRuleException>(() => svc.CreateEditionAsync(pl.Id, season.Id, "test"));
        await svc.RestoreAsync(pl.Id, "test");
        Assert.Empty(CompetitionAdminService.Warnings(await db.Competitions.ToListAsync()));

        // Đổi luật (A7): ghi nhật ký trước/sau.
        await svc.UpdateAsync(pl.Id, CompetitionAdminService.Input(pl) with { Rules = pl.Rules with { RelegationPlaces = 3 } }, "test");
        Assert.Contains("lên 2 đội", Assert.Single(CompetitionAdminService.Warnings(await db.Competitions.ToListAsync())));
        var audit = await db.AdminAudit.Where(a => a.EntityId == pl.Id && a.Action == "Update").SingleAsync();
        Assert.Contains("\"RelegationPlaces\":3", audit.After!.Replace(" ", ""));
        Assert.Contains("\"RelegationPlaces\":2", audit.Before!.Replace(" ", ""));
    }

    [DbFact]
    public async Task Edition_clubs_change_only_in_draft_with_one_league_and_one_cup_per_club()
    {
        await using var t = await TestDb.CreateAsync();
        await using var db = t.Context();
        var svc = new CompetitionAdminService(db, t.Clock);
        await svc.SeedDefaultsAsync("test");
        Club a = new() { Name = "A" }, b = new() { Name = "B" }, gone = new() { Name = "Cũ", Status = ClubStatus.Inactive };
        db.Clubs.AddRange(a, b, gone);
        await db.SaveChangesAsync();
        var season = await new CompetitionService(db, t.Store, t.Clock).CreateSeasonAsync("Mùa 1", 1);
        var ids = await db.Competitions.ToDictionaryAsync(c => c.Name, c => c.Id);
        var pl = await svc.CreateEditionAsync(ids["Premier League"], season.Id, "test");
        var ch = await svc.CreateEditionAsync(ids["Championship"], season.Id, "test");
        var c1 = await svc.CreateEditionAsync(ids["Champions League"], season.Id, "test");
        await Assert.ThrowsAsync<AdminRuleException>(() => svc.CreateEditionAsync(ids["Premier League"], season.Id, "test"));

        await svc.SetEditionClubAsync(pl.Id, a.Id, 1, "test");
        await Assert.ThrowsAsync<AdminRuleException>(() => svc.SetEditionClubAsync(ch.Id, a.Id, null, "test"));    // đã dự Premier League
        await svc.SetEditionClubAsync(c1.Id, a.Id, null, "test");                                                   // cúp thì được
        await Assert.ThrowsAsync<AdminRuleException>(() => svc.SetEditionClubAsync(pl.Id, b.Id, 1, "test"));      // hạt giống 1 đã có
        await svc.SetEditionClubAsync(pl.Id, b.Id, 2, "test");
        await svc.SetEditionClubAsync(pl.Id, a.Id, 3, "test");
        await Assert.ThrowsAsync<AdminRuleException>(() => svc.SetEditionClubAsync(pl.Id, gone.Id, null, "test"));
        await svc.RemoveEditionClubAsync(pl.Id, b.Id, "test");
        var left = Assert.Single(await db.EditionClubs.Where(x => x.EditionId == pl.Id).ToListAsync());
        Assert.Equal((a.Id, (int?)3), (left.ClubId, left.Seed));
        Assert.Contains("\"Seed\":1", (await db.AdminAudit.SingleAsync(x => x.Action == "SetSeed")).Before!.Replace(" ", ""));

        (await db.Editions.SingleAsync(e => e.Id == pl.Id)).Status = EditionStatus.Scheduled;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<AdminRuleException>(() => svc.SetEditionClubAsync(pl.Id, b.Id, null, "test"));
        await Assert.ThrowsAsync<AdminRuleException>(() => svc.DeleteEditionAsync(pl.Id, "test"));

        await svc.DeleteEditionAsync(c1.Id, "test");
        Assert.False(await db.Editions.AnyAsync(e => e.Id == c1.Id));
        Assert.False(await db.EditionClubs.AnyAsync(x => x.EditionId == c1.Id));
        (await db.Seasons.SingleAsync(s => s.Id == season.Id)).Status = SeasonStatus.Finished;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<AdminRuleException>(() => svc.CreateEditionAsync(ids["Europa League"], season.Id, "test"));
    }
}
