using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Pes17League.Clubs.Application;

namespace Pes17League.Clubs.Tests;

/// <summary>Liên đoàn nhiều giải — bước 1 (docs/domain-admin-design.md, PO duyệt 05/11): bảng mới + chuyển dữ liệu mùa cũ.</summary>
public class FederationTests
{
    [DbFact]
    public async Task Competition_rules_round_trip_and_one_edition_per_season()
    {
        await using var t = await TestDb.CreateAsync();
        var season = new Season { Name = "2026/27", Number = 2, CreatedAt = t.Clock.GetUtcNow() };
        var top = new Competition { Name = "Premier League", Country = "ENG", Tier = 1, Type = CompetitionType.DomesticLeague,
            Rules = new() { PromotionPlaces = 0, RelegationPlaces = 2, ContinentalPlaces = 4, SecondaryContinentalPlaces = 2 }, CreatedAt = t.Clock.GetUtcNow() };
        var cup = new Competition { Name = "C1", Type = CompetitionType.ContinentalCup,
            Rules = new() { LeaguePhaseMatches = 8, DirectKnockoutPlaces = 4, PlayoffPlaces = 12 }, CreatedAt = t.Clock.GetUtcNow() };
        await using (var db = t.Context())
        {
            db.AddRange(season, top, cup, new CompetitionEdition { CompetitionId = cup.Id, SeasonId = season.Id, RulesSnapshot = cup.Rules });
            await db.SaveChangesAsync();
        }
        await using (var db = t.Context())
        {
            var c = await db.Competitions.SingleAsync(x => x.Id == cup.Id);
            Assert.Equal((8, 4, 12, 3), (c.Rules.LeaguePhaseMatches, c.Rules.DirectKnockoutPlaces, c.Rules.PlayoffPlaces, c.Rules.PointsWin));
            Assert.Equal(2, (await db.Competitions.SingleAsync(x => x.Id == top.Id)).Rules.RelegationPlaces);
            Assert.Equal(cup.Rules, (await db.Editions.SingleAsync()).RulesSnapshot);
            db.Editions.Add(new CompetitionEdition { CompetitionId = cup.Id, SeasonId = season.Id });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }

    [DbFact]
    public async Task Migration_moves_existing_season_into_test_competition()
    {
        await using var t = await TestDb.CreateAsync();
        Guid s = Guid.NewGuid(), m = Guid.NewGuid(), a = Guid.NewGuid(), b = Guid.NewGuid(), f = Guid.NewGuid();
        await using (var db = t.Context())
        {
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20261105101700_TacticsPhase2");    // Down của Federation phải chạy được
            await db.Database.ExecuteSqlRawAsync($"""
                INSERT INTO clubs."Clubs" ("Id","Name","Status","SquadVersion") VALUES ('{a}','A','Active',0), ('{b}','B','Active',0);
                INSERT INTO clubs."Seasons" ("Id","Name","Number","Status","CreatedAt") VALUES ('{s}','Mùa 1',1,'Active',now());
                INSERT INTO clubs."SeasonClubs" ("SeasonId","ClubId") VALUES ('{s}','{a}'), ('{s}','{b}');
                INSERT INTO clubs."Matchdays" ("Id","SeasonId","Number","Status") VALUES ('{m}','{s}',1,'Settled');
                INSERT INTO clubs."Fixtures" ("Id","MatchdayId","HomeClubId","AwayClubId","Status") VALUES ('{f}','{m}','{a}','{b}','Played');
                """);
            await migrator.MigrateAsync();
        }
        await using (var db = t.Context())
        {
            var comp = await db.Competitions.SingleAsync();
            Assert.Equal(("Giải thử nghiệm", CompetitionStatus.Archived), (comp.Name, comp.Status));
            var ed = await db.Editions.SingleAsync();
            Assert.Equal((comp.Id, s, EditionStatus.Active, comp.Rules), (ed.CompetitionId, ed.SeasonId, ed.Status, ed.RulesSnapshot));
            Assert.Equal(new[] { a, b }.Order(), (await db.EditionClubs.Where(x => x.EditionId == ed.Id).Select(x => x.ClubId).ToListAsync()).Order());
            var fx = await db.Fixtures.SingleAsync();
            Assert.Equal((ed.Id, FixtureStage.League, (int?)null), (fx.CompetitionEditionId, fx.Stage, fx.Leg));
        }
    }

    /// <summary>Bước 2: mùa alpha đang chạy (PO điều khiển Arsenal) → lưu trữ; nhập lại 101 CLB từ patch, tất cả BOT, có nước + logo.</summary>
    [DbFact]
    public async Task Setup_archives_alpha_and_imports_all_federation_clubs_as_bots()
    {
        await using var t = await TestDb.CreateAsync();
        Guid alpha, alphaPatch;
        await using (var db = t.Context())
        {
            var svc = new CompetitionService(db, t.Store, t.Clock);
            alpha = (await svc.CreateSeasonAsync("Alpha 1", 1)).Id;
            alphaPatch = (await new ImportPatchService(db, t.Store, t.Clock).ImportAsync("PES6VN 25-26 alpha 1", Env.OrigOptionFile, alpha,
                [new LeagueClub(0), new LeagueClub(1)])).PatchVersionId;
            await new EconomyService(db, t.Clock).SetParametersAsync(alpha, new(InitialBudgetFund: 100_000_000));
            var human = await db.ClubPesMappings.Where(m => m.PesClubIndex == 0).Select(m => m.ClubId).SingleAsync();
            Assert.True((await svc.StartSeasonAsync(alpha, alphaPatch, human)).Ok);
        }

        FederationSetupResult r;
        await using (var db = t.Context())
            r = await new FederationSetupService(db, t.Store, t.Clock).SetupAsync("PES6VN 25-26", Env.OrigOptionFile, "Mùa 1", "test");

        await using (var db = t.Context())
        {
            Assert.Equal((alpha, 2, 101), (r.ArchivedSeasons.Single(), r.ArchivedClubs, r.Import.Clubs));
            Assert.Equal(SeasonStatus.Finished, (await db.Seasons.FindAsync(alpha))!.Status);
            Assert.All(await db.Editions.Where(e => e.SeasonId == alpha).ToListAsync(), e => Assert.Equal(EditionStatus.Finished, e.Status));
            Assert.False(await db.TransferWindows.AnyAsync(w => w.Status != TransferWindowStatus.Closed));
            var season = await db.Seasons.SingleAsync(s => s.Id == r.SeasonId);
            Assert.Equal(("Mùa 1", 2, SeasonStatus.Planned, r.Import.PatchVersionId), (season.Name, season.Number, season.Status, season.PatchVersionId));

            var clubs = await (from m in db.ClubPesMappings.Where(m => m.PatchVersionId == r.Import.PatchVersionId)
                               join c in db.Clubs on m.ClubId equals c.Id
                               select new { c.Id, c.Name, c.Status, c.Country, c.LogoPath, m.PesClubIndex }).ToListAsync();
            Assert.Equal(FederationSetupService.Clubs.Select(c => c.PesClubIndex).Order(), clubs.Select(c => c.PesClubIndex).Order());
            Assert.All(clubs, c => Assert.Equal((ClubStatus.Active, $"/img/clubs/{c.PesClubIndex}.png"), (c.Status, c.LogoPath)));
            Assert.Equal([("AUT", 2), ("BEL", 1), ("ENG", 20), ("ESP", 20), ("FRA", 6), ("GER", 18), ("ITA", 20), ("NED", 3), ("POR", 3), ("RUS", 1), ("SCO", 1), ("SUI", 1), ("TUR", 3), ("UKR", 2)],
                clubs.GroupBy(c => c.Country!).Select(g => (g.Key, g.Count())).OrderBy(x => x.Key, StringComparer.Ordinal).ToList());
            var loose = clubs.Where(c => c.PesClubIndex >= 78).OrderBy(c => c.PesClubIndex).Select(c => $"{c.PesClubIndex} {c.Name} {c.Country}");
            Assert.Equal("""
                78 Ajax Amsterdam NED|79 Brest FRA|80 Benfica POR|81 Besiktas TUR|82 Celtic FC SCO|83 Club Brugge BEL|84 Dynamo Kyiv UKR|85 Feyenoord NED|86 Galatasaray TUR|87 Lille OSC FRA|88 Olympique Lyon FRA|89 Olympique Marseille FRA|90 AS Monaco FRA|91 FC Porto POR|92 Paris Saint-Germain FRA|93 PSV Eindhoven NED|94 Sturm Graz AUT|95 Shakhtar Donetsk UKR|96 Sporting Lisbon POR|97 Z. Saint Petersburg RUS|118 BSC Young Boys SUI|119 RB Salzburg AUT|121 Fenerbahce SK TUR
                """, string.Join('|', loose));

            // Mọi CLB đang điều khiển đều là CLB mới + BOT; CLB/cầu thủ alpha còn nguyên nhưng Inactive.
            var active = await db.TeamAssignments.Where(a => a.To == null).ToListAsync();
            Assert.Equal(clubs.Select(c => c.Id).Order(), active.Select(a => a.ClubId).Order());
            Assert.All(active, a => Assert.Equal(ControllerType.Bot, a.ControllerType));
            var old = await db.ClubPesMappings.Where(m => m.PatchVersionId == alphaPatch).Select(m => m.ClubId).ToListAsync();
            Assert.All(await db.Clubs.Where(c => old.Contains(c.Id)).ToListAsync(), c => Assert.Equal(ClubStatus.Inactive, c.Status));
            Assert.True(await db.SquadMemberships.AnyAsync(m => old.Contains(m.ClubId) && m.LeftAt == null));

            Assert.Equal(r.Import.SquadPlayers, await db.SquadMemberships.CountAsync(m => m.LeftAt == null && !old.Contains(m.ClubId)));
            Assert.Equal(r.Import.SquadPlayers + r.Import.FreeAgents, await db.PlayerAttributes.CountAsync(a => a.SeasonId == r.SeasonId));
            Assert.Equal("FederationSetup", (await db.AdminAudit.SingleAsync()).Action);

            // PO chọn 05/11: hợp đồng nhập + gia hạn mặc định 36 trận (hết mùa đầu).
            var ids = clubs.Select(c => c.Id).ToList();
            Assert.All(await db.Contracts.Where(c => ids.Contains(c.ClubId)).ToListAsync(), c => Assert.Equal((36, ContractStatus.Active), (c.MatchesTotal, c.Status)));
            Assert.Equal(36, (await db.SeasonEconomies.FindAsync(r.SeasonId))!.DefaultContractMatches);
        }
        // Báo cáo cho PO (không assert): số cầu thủ, GK bổ sung/thiếu.
        Console.WriteLine($"FEDERATION squad={r.Import.SquadPlayers} free={r.Import.FreeAgents} gkAdded={r.Import.AddedGoalkeepers.Count} " +
            $"gkShort={string.Join(',', r.Import.GoalkeeperShortages.Select(s => s.ClubName))} shirtFixes={r.Import.ShirtChanges.Count}");
    }

    /// <summary>Mùa 1 trên dev đã nhập với 10 trận trước khi PO chọn 36 → đổi hợp đồng nhập chưa đá khi mùa còn Planned.</summary>
    [DbFact]
    public async Task Imported_contract_length_changes_only_while_season_planned()
    {
        await using var t = await TestDb.CreateAsync();
        Guid season, paid;
        await using (var db = t.Context())
        {
            season = (await new CompetitionService(db, t.Store, t.Clock).CreateSeasonAsync("Mùa 1", 1)).Id;
            (await db.Seasons.FindAsync(season))!.PatchVersionId = (await new ImportPatchService(db, t.Store, t.Clock)
                .ImportAsync("PES6VN 25-26", Env.OrigOptionFile, season, [new LeagueClub(0), new LeagueClub(1)])).PatchVersionId;
            var c = await db.Contracts.OrderBy(x => x.Id).FirstAsync();
            (paid, c.Wage) = (c.Id, 1);     // đã có lương → không phải hợp đồng nhập chưa khởi động, giữ nguyên
            await db.SaveChangesAsync();
        }
        await using (var db = t.Context())
        {
            var n = await new FederationSetupService(db, t.Store, t.Clock).SetImportedContractLengthAsync(season, 36, "test");
            Assert.Equal(await db.Contracts.CountAsync() - 1, n);
        }
        await using (var db = t.Context())
        {
            Assert.All(await db.Contracts.Where(c => c.Id != paid).ToListAsync(), c => Assert.Equal(36, c.MatchesTotal));
            Assert.Equal(10, (await db.Contracts.FindAsync(paid))!.MatchesTotal);
            Assert.Equal(36, (await db.SeasonEconomies.FindAsync(season))!.DefaultContractMatches);
            Assert.Equal("SetImportedContractLength", (await db.AdminAudit.SingleAsync()).Action);
            var svc = new FederationSetupService(db, t.Store, t.Clock);
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => svc.SetImportedContractLengthAsync(season, 61, "test"));
            (await db.Seasons.FindAsync(season))!.Status = SeasonStatus.Active;
            await db.SaveChangesAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() => svc.SetImportedContractLengthAsync(season, 20, "test"));
        }
    }
}
