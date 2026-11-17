using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Application;

namespace Pes17League.Clubs.Tests;

/// <summary>Lộ trình FM đợt 2 (PO ok 12/11): tài khoản HLV, mỗi HLV 1 CLB, trận chờ người / máy đá.</summary>
public class ManagerTests
{
    [Fact]
    public void Password_hash_roundtrip()
    {
        var h = ManagerService.Hash("abc123");
        Assert.True(ManagerService.Check(h, "abc123"));
        Assert.False(ManagerService.Check(h, "abc124"));
        Assert.False(ManagerService.Check("rác", "abc123"));
    }

    [Fact]
    public void Needs_human_only_when_exactly_one_human_side_and_not_delegated()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), bot = Guid.NewGuid();
        var humans = new HashSet<Guid> { a, b };
        Assert.True(MatchOrchestrator.NeedsHuman(new Fixture { HomeClubId = a, AwayClubId = bot }, humans));
        Assert.False(MatchOrchestrator.NeedsHuman(new Fixture { HomeClubId = a, AwayClubId = bot, AssistantPlays = true }, humans));
        Assert.False(MatchOrchestrator.NeedsHuman(new Fixture { HomeClubId = a, AwayClubId = b }, humans));   // người vs người: máy đá
        Assert.False(MatchOrchestrator.NeedsHuman(new Fixture { HomeClubId = bot, AwayClubId = Guid.NewGuid() }, humans));
    }

    [DbFact]
    public async Task Managers_login_and_hold_one_club_each()
    {
        await using var t = await TestDb.CreateAsync();
        await using var db = t.Context();
        var season = new Season { Name = "Mùa 1", Number = 2, CreatedAt = t.Clock.GetUtcNow() };
        var comp = new Competition { Name = "Serie A", Type = CompetitionType.DomesticLeague, CreatedAt = t.Clock.GetUtcNow() };
        var edition = new CompetitionEdition { CompetitionId = comp.Id, SeasonId = season.Id };
        Club milan = new() { Name = "Milan" }, inter = new() { Name = "Inter" }, roma = new() { Name = "Roma" };
        db.AddRange(season, comp, edition, milan, inter, roma);
        db.AddRange(new[] { milan, inter, roma }.Select(c => new EditionClub { EditionId = edition.Id, ClubId = c.Id }));
        await db.SaveChangesAsync();
        var svc = new ManagerService(db, t.Clock);
        var clubs = new ClubAdminService(db, t.Clock);

        var pw = await svc.CreateAsync(" An.Nguyen ", "An", "test");
        Assert.Equal("an.nguyen", (await db.Managers.SingleAsync()).Username);
        await Assert.ThrowsAsync<AdminRuleException>(() => svc.CreateAsync("an.nguyen", "An 2", "test"));
        await Assert.ThrowsAsync<AdminRuleException>(() => svc.CreateAsync("admin", "x", "test"));
        Assert.NotNull(await svc.VerifyAsync("AN.NGUYEN", pw));
        Assert.Null(await svc.VerifyAsync("an.nguyen", pw + "x"));
        var an = (await db.Managers.SingleAsync()).Id;
        var binh = await CreateId(svc, db, "binh");

        await clubs.SetHumanClubAsync(milan.Id, "test");          // admin (PO) cầm Milan
        await clubs.SetHumanClubAsync(inter.Id, "test", an);      // An cầm Inter, Milan vẫn của admin
        Assert.Contains("HLV khác", (await Assert.ThrowsAsync<AdminRuleException>(() => clubs.SetHumanClubAsync(milan.Id, "test", binh))).Message);
        await clubs.SetHumanClubAsync(roma.Id, "test", binh);
        var rows = await svc.ListAsync();
        Assert.Equal((inter.Id, roma.Id), (rows.Single(r => r.Id == an).ClubId, rows.Single(r => r.Id == binh).ClubId));
        Assert.Equal(3, await db.TeamAssignments.CountAsync(a => a.To == null && a.ControllerType == ControllerType.Human));

        await svc.SetDisabledAsync(an, true, "test");             // khoá: Inter về BOT, không đăng nhập được
        Assert.Null(await svc.VerifyAsync("an.nguyen", pw));
        Assert.False(await db.TeamAssignments.AnyAsync(a => a.ClubId == inter.Id && a.To == null && a.ControllerType == ControllerType.Human));
        Assert.True(await db.TeamAssignments.AnyAsync(a => a.ClubId == milan.Id && a.To == null && a.ControllerType == ControllerType.Human && a.ParticipantId == null));
    }

    private static async Task<Guid> CreateId(ManagerService svc, Infrastructure.ClubsDbContext db, string name)
    {
        await svc.CreateAsync(name, name, "test");
        return (await db.Managers.SingleAsync(m => m.Username == name)).Id;
    }
}
