using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Application;
using Pes17League.Clubs.Infrastructure;
using Pes17League.OptionFile;

namespace Pes17League.Clubs.Tests;

/// <summary>Admin CLB — bước 3b: sửa thông tin, thêm CLB vào ô PES dư, xoá/lưu trữ (docs/domain-admin-design.md mục 5, PO chọn 05/11).</summary>
public class ClubAdminTests
{
    [DbFact]
    public async Task Update_normalizes_input_and_writes_audit_only_on_change()
    {
        await using var t = await TestDb.CreateAsync();
        await using var db = t.Context();
        var club = new Club { Name = "Arsenal", Country = "ENG", LogoPath = "/img/clubs/0.png" };
        db.Clubs.Add(club);
        await db.SaveChangesAsync();
        var svc = new ClubAdminService(db, t.Clock);

        await svc.UpdateAsync(club.Id, new("  Arsenal FC ", " ARS ", " eng", "  "), "test");
        await using (var check = t.Context())
        {
            var c = await check.Clubs.SingleAsync();
            Assert.Equal(("Arsenal FC", "ARS", "ENG", (string?)null), (c.Name, c.ShortName, c.Country, c.LogoPath));
            var a = await check.AdminAudit.SingleAsync();
            Assert.Equal((nameof(Club), club.Id, "Update", "test", t.Clock.GetUtcNow()), (a.EntityType, a.EntityId, a.Action, a.Actor, a.At));
            Assert.Equal(("Arsenal", "/img/clubs/0.png"), (Field(a.Before, "Name"), Field(a.Before, "LogoPath")));
            Assert.Equal(("Arsenal FC", null), (Field(a.After, "Name"), Field(a.After, "LogoPath")));
        }

        await svc.UpdateAsync(club.Id, new("Arsenal FC", "ARS", "ENG", null), "test");   // không đổi gì → không ghi nhật ký
        Assert.Equal(1, await db.AdminAudit.CountAsync());
    }

    [DbFact]
    public async Task Update_rejects_invalid_fields_and_duplicate_active_name()
    {
        await using var t = await TestDb.CreateAsync();
        await using var db = t.Context();
        var arsenal = new Club { Name = "Arsenal" };
        var chelsea = new Club { Name = "Chelsea" };
        var alpha = new Club { Name = "Liverpool", Status = ClubStatus.Inactive };   // CLB alpha đã lưu trữ, trùng tên CLB liên đoàn
        db.Clubs.AddRange(arsenal, chelsea, alpha);
        await db.SaveChangesAsync();
        var svc = new ClubAdminService(db, t.Clock);

        var e = await Assert.ThrowsAsync<AdminRuleException>(() => svc.UpdateAsync(chelsea.Id, new(" ", "CHELSEA-FC1", "ENGL", "/" + new string('x', 300)), "test"));
        Assert.Equal(4, e.Message.Split("; ").Length);
        e = await Assert.ThrowsAsync<AdminRuleException>(() => svc.UpdateAsync(chelsea.Id, new("Arsenal", null, null, null), "test"));
        Assert.Contains("Arsenal", e.Message);
        await Assert.ThrowsAsync<AdminRuleException>(() => svc.UpdateAsync(Guid.NewGuid(), new("X", null, null, null), "test"));
        Assert.Equal(0, await db.AdminAudit.CountAsync());

        await svc.UpdateAsync(chelsea.Id, new("Liverpool", null, null, null), "test");   // trùng CLB đã lưu trữ → được
        await svc.UpdateAsync(alpha.Id, new("Arsenal", null, null, null), "test");       // CLB đã lưu trữ không bị kiểm trùng
        Assert.Equal(2, await db.AdminAudit.CountAsync());
    }

    [DbFact]
    public async Task Create_in_spare_slot_takes_slot_players_skips_owned_and_prices_new_contracts()
    {
        await using var t = await TestDb.CreateAsync();
        var (season, patch) = await SetupAsync(t);
        var image = OptionFileCodec.Decode(Env.OrigOptionFile);
        var slot98 = image.GetSquad(98).Players.Select(s => s.PlayerId).Where(id => image.GetPosition(id) != Position.GK).ToList();
        Guid ownedId, freeId;
        await using (var db = t.Context())
        {
            // 1 người của ô 98 đang ở CLB khác (bỏ qua), 1 người là cầu thủ tự do trong DB (nhận, giữ CLB gốc).
            var holder = new Club { Name = "Holder" };
            var owned = new FootballPlayer { Name = image.GetPlayerName(slot98[0]), Position = image.GetPosition(slot98[0]) };
            var free = new FootballPlayer { Name = image.GetPlayerName(slot98[1]), Position = image.GetPosition(slot98[1]) };
            (ownedId, freeId) = (owned.Id, free.Id);
            db.AddRange(holder, owned, free,
                new PlayerPesMapping { PlayerId = owned.Id, PatchVersionId = patch, PesPlayerId = slot98[0] },
                new PlayerPesMapping { PlayerId = free.Id, PatchVersionId = patch, PesPlayerId = slot98[1] },
                new PlayerAttributes { PlayerId = free.Id, SeasonId = season, CapturedAt = t.Clock.GetUtcNow(),
                    Values = PlayerField.Verified.ToDictionary(f => f.Name, f => image.Get(slot98[1], f)) },
                new SquadMembership { ClubId = holder.Id, PlayerId = owned.Id, ShirtNumber = 1, JoinedAt = t.Clock.GetUtcNow() });
            await db.SaveChangesAsync();
            var economy = new EconomyService(db, t.Clock);
            await economy.SetParametersAsync(season, new(DefaultContractMatches: 36));
            await economy.RecalculatePricesAsync(season);   // mùa đã có giá → CLB mới được tính giá + lương ngay
        }

        CreateClubResult r;
        await using (var db = t.Context())
        {
            var svc = new ClubAdminService(db, t.Clock, t.Store);
            Assert.Contains(await svc.SpareSlotsAsync(), s => s is { Slot: 98, Players: 25, Goalkeepers: 4 });
            r = await svc.CreateAsync(98, new(" AC Milan 2006 ", " acm ", "ita", null), "test");
        }
        Assert.Equal((24, 0), (r.Players, r.AddedGoalkeepers.Count));
        Assert.Equal([image.GetPlayerName(slot98[0])], r.Skipped);

        await using (var db = t.Context())
        {
            var club = await db.Clubs.SingleAsync(c => c.Id == r.ClubId);
            Assert.Equal(("AC Milan 2006", "acm", "ITA", ClubStatus.Active), (club.Name, club.ShortName, club.Country, club.Status));
            Assert.Equal(98, (await db.ClubPesMappings.SingleAsync(m => m.ClubId == club.Id)).PesClubIndex);
            Assert.Equal(ControllerType.Bot, (await db.TeamAssignments.SingleAsync(a => a.ClubId == club.Id && a.To == null)).ControllerType);
            var members = await db.SquadMemberships.Where(m => m.ClubId == club.Id && m.LeftAt == null).OrderBy(m => m.SlotOrder).ToListAsync();
            Assert.Equal(24, members.Select(m => m.ShirtNumber).Distinct().Count());
            Assert.Contains(members, m => m.PlayerId == freeId);
            Assert.DoesNotContain(members, m => m.PlayerId == ownedId);
            Assert.Equal(23, await db.Players.CountAsync(p => p.OriginClubId == club.Id));
            Assert.Null((await db.Players.SingleAsync(p => p.Id == freeId)).OriginClubId);
            Assert.Equal(23, await db.PlayerAttributes.CountAsync(a => a.SeasonId == season && db.Players.Any(p => p.Id == a.PlayerId && p.OriginClubId == club.Id)));
            var contracts = await db.Contracts.Where(c => c.ClubId == club.Id).ToListAsync();
            Assert.Equal(24, contracts.Count);
            Assert.All(contracts, c => Assert.Equal((36, ContractStatus.Active, true), (c.MatchesTotal, c.Status, c.Wage is not null)));
            var a = await db.AdminAudit.SingleAsync(x => x.EntityId == club.Id);
            Assert.Equal(("Create", "98", "24"), (a.Action, Number(a.After, "Slot"), Number(a.After, "Players")));

            var svc = new ClubAdminService(db, t.Clock, t.Store);
            var spare = (await svc.SpareSlotsAsync()).Select(s => s.Slot).ToList();
            Assert.DoesNotContain(98, spare);
            Assert.DoesNotContain(125, spare);   // Slovan không có trong menu game
            Assert.Equal(140 - 3 - 1, spare.Count);
            Assert.Contains("Milan", (await Assert.ThrowsAsync<AdminRuleException>(() => svc.CreateAsync(98, new("Milan B", null, null, null), "test"))).Message);
            Assert.Contains("Đã có CLB", (await Assert.ThrowsAsync<AdminRuleException>(() => svc.CreateAsync(99, new("AC Milan 2006", null, null, null), "test"))).Message);
            foreach (var bad in new[] { 0, 125, 140, -1 })
                await Assert.ThrowsAsync<AdminRuleException>(() => svc.CreateAsync(bad, new($"Club {bad}", null, null, null), "test"));
            Assert.Equal(1, await db.AdminAudit.CountAsync());
        }
    }

    [DbFact]
    public async Task Create_tops_up_goalkeepers_and_delete_without_history_removes_slot_players()
    {
        await using var t = await TestDb.CreateAsync();
        var (_, patch) = await SetupAsync(t);
        var players = await CountAsync(t, db => db.Players);
        CreateClubResult r;
        await using (var db = t.Context())
            r = await new ClubAdminService(db, t.Clock, t.Store).CreateAsync(138, new("Đông Á Thanh Hoá", "THA", "VIE", null), "test");   // ô 138 không có GK
        Assert.Equal((18, 2), (r.Players, r.AddedGoalkeepers.Count));

        List<Guid> gks;
        await using (var db = t.Context())
        {
            gks = await (from m in db.SquadMemberships join p in db.Players on m.PlayerId equals p.Id
                         where m.ClubId == r.ClubId && p.Position == Position.GK select p.Id).ToListAsync();
            Assert.Equal(2, gks.Count);
            Assert.Equal(0, await db.Players.CountAsync(p => gks.Contains(p.Id) && p.OriginClubId != null));   // GK tự do bổ sung
            var values = await new EconomyService(db, t.Clock).CurrentAttributesAsync((await db.Seasons.SingleAsync()).Id, gks);
            Assert.All(gks, id => Assert.True(values[id][PlayerField.GoalKeeping.Name] >= 70));
            Assert.Equal(players + 16, await db.Players.CountAsync());

            var svc = new ClubAdminService(db, t.Clock, t.Store);
            Assert.False(await svc.HasHistoryAsync(r.ClubId));
            Assert.True(await svc.DeleteOrArchiveAsync(r.ClubId, "test"));
        }
        await using (var db = t.Context())
        {
            Assert.False(await db.Clubs.AnyAsync(c => c.Id == r.ClubId));
            Assert.Equal(players, await db.Players.CountAsync());
            Assert.False(await db.SquadMemberships.AnyAsync(m => gks.Contains(m.PlayerId)));
            Assert.False(await db.Contracts.AnyAsync(c => gks.Contains(c.PlayerId)));
            Assert.Equal(2, await db.Players.CountAsync(p => gks.Contains(p.Id)));   // GK về lại tự do
            Assert.False(await db.ClubPesMappings.AnyAsync(m => m.PatchVersionId == patch && m.PesClubIndex == 138));
            Assert.Contains(await new ClubAdminService(db, t.Clock, t.Store).SpareSlotsAsync(), s => s.Slot == 138);
            Assert.Equal(["Create", "Delete"], await db.AdminAudit.OrderBy(a => a.At).ThenBy(a => a.Action).Select(a => a.Action).ToListAsync());
        }
    }

    [DbFact]
    public async Task Archive_blocks_while_in_edition_then_releases_players_and_round_option_file_skips_slot()
    {
        await using var t = await TestDb.CreateAsync();
        var (season, patch) = await SetupAsync(t);
        CreateClubResult r;
        await using (var db = t.Context())
            r = await new ClubAdminService(db, t.Clock, t.Store).CreateAsync(98, new("AC Milan 2006", null, null, null), "test");
        await using (var db = t.Context())
        {
            var build = await new RoundOptionFileService(db, t.Store, t.Clock).BuildAsync(patch, null);
            Assert.True(build.Ok);
            Assert.Contains("\"98\"", (await db.OptionFileBuilds.SingleAsync(b => b.Id == build.BuildId)).SquadSnapshot);

            var comp = new Competition { Name = "Serie A", Type = CompetitionType.DomesticLeague, CreatedAt = t.Clock.GetUtcNow() };
            var edition = new CompetitionEdition { CompetitionId = comp.Id, SeasonId = season };
            db.AddRange(comp, edition, new EditionClub { EditionId = edition.Id, ClubId = r.ClubId });
            await db.SaveChangesAsync();
            var svc = new ClubAdminService(db, t.Clock, t.Store);
            Assert.Contains("Serie A", (await Assert.ThrowsAsync<AdminRuleException>(() => svc.DeleteOrArchiveAsync(r.ClubId, "test"))).Message);
            await new CompetitionAdminService(db, t.Clock).RemoveEditionClubAsync(edition.Id, r.ClubId, "test");
            db.Ledger.Add(new ClubLedgerEntry { ClubId = r.ClubId, Amount = 100, Reason = LedgerReason.Adjustment, At = t.Clock.GetUtcNow() });
            await db.SaveChangesAsync();
            Assert.True(await svc.HasHistoryAsync(r.ClubId));
            Assert.False(await svc.DeleteOrArchiveAsync(r.ClubId, "test"));
            Assert.False(await svc.DeleteOrArchiveAsync(r.ClubId, "test"));   // đã lưu trữ → không làm gì
        }
        await using (var db = t.Context())
        {
            Assert.Equal(ClubStatus.Inactive, (await db.Clubs.SingleAsync(c => c.Id == r.ClubId)).Status);
            Assert.False(await db.SquadMemberships.AnyAsync(m => m.ClubId == r.ClubId && m.LeftAt == null));
            Assert.All(await db.Contracts.Where(c => c.ClubId == r.ClubId).ToListAsync(),
                c => Assert.Equal((ContractStatus.Ended, ContractEndReason.Released), (c.Status, c.EndReason)));
            Assert.False(await db.TeamAssignments.AnyAsync(a => a.ClubId == r.ClubId && a.To == null));
            var a = await db.AdminAudit.SingleAsync(x => x.EntityId == r.ClubId && x.Action == "Archive");
            Assert.Equal("25", Number(a.After, "Released"));

            var build = await new RoundOptionFileService(db, t.Store, t.Clock).BuildAsync(patch, null);
            Assert.True(build.Ok);
            var snapshot = (await db.OptionFileBuilds.SingleAsync(b => b.Id == build.BuildId)).SquadSnapshot;
            Assert.DoesNotContain("\"98\"", snapshot);
            Assert.Contains("\"0\"", snapshot);
            var svc = new ClubAdminService(db, t.Clock, t.Store);
            Assert.DoesNotContain(await svc.SpareSlotsAsync(), s => s.Slot == 98);   // ô giữ tới hết patch
            Assert.Contains("AC Milan 2006", (await Assert.ThrowsAsync<AdminRuleException>(() => svc.CreateAsync(98, new("Milan", null, null, null), "test"))).Message);
        }
    }

    [DbFact]
    public async Task Set_human_club_moves_human_assignment_and_old_club_back_to_bot()
    {
        await using var t = await TestDb.CreateAsync();
        await using var db = t.Context();
        var season = new Season { Name = "Mùa 1", Number = 2, CreatedAt = t.Clock.GetUtcNow() };
        var comp = new Competition { Name = "Serie A", Type = CompetitionType.DomesticLeague, CreatedAt = t.Clock.GetUtcNow() };
        var edition = new CompetitionEdition { CompetitionId = comp.Id, SeasonId = season.Id };
        Club milan = new() { Name = "Milan" }, inter = new() { Name = "Inter" }, roma = new() { Name = "Roma" }, lazio = new() { Name = "Lazio", Status = ClubStatus.Inactive };
        db.AddRange(season, comp, edition, milan, inter, roma, lazio,
            new EditionClub { EditionId = edition.Id, ClubId = milan.Id }, new EditionClub { EditionId = edition.Id, ClubId = inter.Id },
            new EditionClub { EditionId = edition.Id, ClubId = lazio.Id });
        db.TeamAssignments.AddRange(new[] { milan, inter, roma }.Select(c => new TeamAssignment { ClubId = c.Id, ControllerType = ControllerType.Bot, From = t.Clock.GetUtcNow() }));
        await db.SaveChangesAsync();
        var svc = new ClubAdminService(db, t.Clock);
        async Task<string> Open(Club c) => string.Join(",", await db.TeamAssignments.Where(a => a.ClubId == c.Id && a.To == null).Select(a => a.ControllerType).ToListAsync());

        t.Clock.Advance(TimeSpan.FromHours(1));
        await svc.SetHumanClubAsync(milan.Id, "test");
        Assert.Equal(("Human", "Bot"), (await Open(milan), await Open(inter)));
        Assert.Equal(t.Clock.GetUtcNow(), (await db.TeamAssignments.SingleAsync(a => a.ClubId == milan.Id && a.ControllerType == ControllerType.Bot)).To);

        t.Clock.Advance(TimeSpan.FromHours(1));
        await svc.SetHumanClubAsync(inter.Id, "test");   // đổi CLB: CLB cũ về BOT
        Assert.Equal(("Bot", "Human"), (await Open(milan), await Open(inter)));
        await svc.SetHumanClubAsync(inter.Id, "test");   // không đổi → không ghi
        Assert.Equal(3, await db.AdminAudit.CountAsync());

        Assert.Contains("không dự", (await Assert.ThrowsAsync<AdminRuleException>(() => svc.SetHumanClubAsync(roma.Id, "test"))).Message);
        Assert.Contains("lưu trữ", (await Assert.ThrowsAsync<AdminRuleException>(() => svc.SetHumanClubAsync(lazio.Id, "test"))).Message);
        await Assert.ThrowsAsync<AdminRuleException>(() => svc.SetHumanClubAsync(Guid.NewGuid(), "test"));

        var md = new Matchday { SeasonId = season.Id, Number = 1, Status = MatchdayStatus.Playing };
        var f = new Fixture { MatchdayId = md.Id, HomeClubId = roma.Id, AwayClubId = inter.Id, Status = FixtureStatus.Playing, CompetitionEditionId = edition.Id };
        db.AddRange(md, f);
        await db.SaveChangesAsync();
        Assert.Contains("đang đá", (await Assert.ThrowsAsync<AdminRuleException>(() => svc.SetHumanClubAsync(null, "test"))).Message);
        Assert.Contains("đang đá", (await Assert.ThrowsAsync<AdminRuleException>(() => svc.SetHumanClubAsync(milan.Id, "test"))).Message);

        f.Status = FixtureStatus.Played;
        await db.SaveChangesAsync();
        t.Clock.Advance(TimeSpan.FromHours(1));
        await svc.SetHumanClubAsync(null, "test");   // không cầm CLB nào
        Assert.Equal(("Bot", "Bot"), (await Open(milan), await Open(inter)));
        Assert.False(await db.TeamAssignments.AnyAsync(a => a.To == null && a.ControllerType == ControllerType.Human));
        var audit = await db.AdminAudit.OrderBy(a => a.At).ThenBy(a => a.Id).ToListAsync();
        Assert.All(audit, a => Assert.Equal((nameof(Club), ClubAdminService.SetControllerAction), (a.EntityType, a.Action)));
        Assert.Equal((inter.Id, "Human", "Bot"), (audit[^1].EntityId, Field(audit[^1].Before, "ControllerType"), Field(audit[^1].After, "ControllerType")));
    }

    /// <summary>Mùa 1 (Planned) + patch nhập từ OF gốc với 2 CLB tham gia (ô 0, 1) và cầu thủ tự do.</summary>
    private static async Task<(Guid Season, Guid Patch)> SetupAsync(TestDb t)
    {
        await using var db = t.Context();
        var season = new Season { Name = "Mùa 1", Number = 2, CreatedAt = t.Clock.GetUtcNow() };
        db.Seasons.Add(season);
        await db.SaveChangesAsync();
        season.PatchVersionId = (await new ImportPatchService(db, t.Store, t.Clock)
            .ImportAsync("PES6VN 25-26", Env.OrigOptionFile, season.Id, [new LeagueClub(0), new LeagueClub(1)])).PatchVersionId;
        await db.SaveChangesAsync();
        return (season.Id, season.PatchVersionId.Value);
    }

    private static async Task<int> CountAsync<T>(TestDb t, Func<ClubsDbContext, IQueryable<T>> set)
    {
        await using var db = t.Context();
        return await set(db).CountAsync();
    }

    private static string? Field(string? json, string name) => JsonDocument.Parse(json!).RootElement.GetProperty(name).GetString();

    private static string Number(string? json, string name) => JsonDocument.Parse(json!).RootElement.GetProperty(name).GetRawText();
}
