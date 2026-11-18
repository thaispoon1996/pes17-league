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
    public void Needs_human_when_any_human_side_and_not_delegated()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), bot = Guid.NewGuid();
        var humans = new HashSet<Guid> { a, b };
        Assert.True(MatchOrchestrator.NeedsHuman(new Fixture { HomeClubId = a, AwayClubId = bot }, humans));
        Assert.False(MatchOrchestrator.NeedsHuman(new Fixture { HomeClubId = a, AwayClubId = bot, AssistantPlays = true }, humans));
        Assert.True(MatchOrchestrator.NeedsHuman(new Fixture { HomeClubId = a, AwayClubId = b }, humans));   // PO 17/11: người vs người = 2 người đá
        Assert.True(MatchOrchestrator.BothHuman(new Fixture { HomeClubId = a, AwayClubId = b }, humans));
        Assert.False(MatchOrchestrator.NeedsHuman(new Fixture { HomeClubId = bot, AwayClubId = Guid.NewGuid() }, humans));
    }

    [DbFact]
    public async Task Players_self_register_with_single_use_invite()
    {
        await using var t = await TestDb.CreateAsync();
        await using var db = t.Context();
        var svc = new ManagerService(db, t.Clock);
        var codes = await svc.CreateInvitesAsync(2, "bạn A", "test");
        Assert.All(codes, c => Assert.Matches("^PES-[A-Z0-9]{6}$", c));
        await Assert.ThrowsAsync<AdminRuleException>(() => svc.RegisterAsync("PES-XXXXXX", "binh", "Bình", "matkhau123"));
        await Assert.ThrowsAsync<AdminRuleException>(() => svc.RegisterAsync("", "binh", "Bình", "123", requireInvite: true));
        var free = await svc.RegisterAsync("", "khach1", "Khách 1", "123");   // PO 16/11: không cần mã khi tắt yêu cầu
        Assert.NotNull(await svc.VerifyAsync("khach1", "123"));
        _ = free;
        await Assert.ThrowsAsync<AdminRuleException>(() => svc.RegisterAsync(codes[0], "binh", "Bình", "12"));   // mật khẩu < 3
        var m = await svc.RegisterAsync(" " + codes[0].ToLower() + " ", " Binh ", "Bình", "matkhau123");
        Assert.True(m.SelfRegistered);
        Assert.NotNull(await svc.VerifyAsync("binh", "matkhau123"));
        await Assert.ThrowsAsync<AdminRuleException>(() => svc.RegisterAsync(codes[0], "chi", "Chi", "matkhau123"));   // mã đã dùng
        await Assert.ThrowsAsync<AdminRuleException>(() => svc.RegisterAsync(codes[1], "binh", "Bình 2", "matkhau123")); // trùng tên
        var unused = await db.InviteCodes.SingleAsync(i => i.Code == codes[1]);
        await svc.RevokeInviteAsync(unused.Id);
        await Assert.ThrowsAsync<AdminRuleException>(() => svc.RegisterAsync(codes[1], "chi", "Chi", "matkhau123"));    // mã đã huỷ
        Assert.False(await db.TeamAssignments.AnyAsync(a => a.ParticipantId == m.Id));   // chưa có CLB
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

        var lan = await CreateId(svc, db, "lan");
        Assert.Contains("Admin không", (await Assert.ThrowsAsync<AdminRuleException>(() => clubs.SetHumanClubAsync(milan.Id, "test"))).Message);   // PO 17/11
        await clubs.SetHumanClubAsync(milan.Id, "test", lan);     // Lan cầm Milan
        await clubs.SetHumanClubAsync(inter.Id, "test", an);      // An cầm Inter, Milan vẫn của Lan
        Assert.Contains("HLV khác", (await Assert.ThrowsAsync<AdminRuleException>(() => clubs.SetHumanClubAsync(milan.Id, "test", binh))).Message);
        await clubs.SetHumanClubAsync(roma.Id, "test", binh);
        var rows = await svc.ListAsync();
        Assert.Equal((inter.Id, roma.Id), (rows.Single(r => r.Id == an).ClubId, rows.Single(r => r.Id == binh).ClubId));
        Assert.Equal(3, await db.TeamAssignments.CountAsync(a => a.To == null && a.ControllerType == ControllerType.Human));

        await svc.SetDisabledAsync(an, true, "test");             // khoá: Inter về BOT, không đăng nhập được
        Assert.Null(await svc.VerifyAsync("an.nguyen", pw));
        Assert.False(await db.TeamAssignments.AnyAsync(a => a.ClubId == inter.Id && a.To == null && a.ControllerType == ControllerType.Human));
        Assert.True(await db.TeamAssignments.AnyAsync(a => a.ClubId == milan.Id && a.To == null && a.ControllerType == ControllerType.Human && a.ParticipantId == lan));
        Assert.Equal(ManagerService.DefaultPassword, pw);                    // PO 16/11: mật khẩu mặc định 123

        // Đăng ký đá giải (PO 16/11): người chưa có CLB nộp đơn → admin duyệt giao CLB
        var apps = new LeagueApplicationService(db, t.Clock);
        var open = await apps.OpenClubsAsync();
        Assert.Contains(open, c => c.Id == inter.Id);
        Assert.DoesNotContain(open, c => c.Id == roma.Id || c.Id == milan.Id);
        await Assert.ThrowsAsync<AdminRuleException>(() => apps.ApplyAsync(binh, null, null));                // đã có CLB
        await Assert.ThrowsAsync<AdminRuleException>(() => apps.ApplyAsync(an, roma.Id, null));              // CLB không trống
        var chi = await CreateId(svc, db, "chi");
        var app = await apps.ApplyAsync(chi, inter.Id, "  muốn cầm Inter  ");
        Assert.Equal("muốn cầm Inter", app.Note);
        await Assert.ThrowsAsync<AdminRuleException>(() => apps.ApplyAsync(chi, null, null));                // 1 đơn chờ
        Assert.Single(await apps.PendingListAsync());
        await apps.ApproveAsync(app.Id, inter.Id, "test");
        Assert.True(await db.TeamAssignments.AnyAsync(a => a.ClubId == inter.Id && a.To == null && a.ParticipantId == chi && a.ControllerType == ControllerType.Human));
        Assert.Equal(LeagueApplicationStatus.Approved, (await apps.LatestAsync(chi))!.Status);
        var dung = await CreateId(svc, db, "dung");
        var app2 = await apps.ApplyAsync(dung, null, null);
        await apps.RejectAsync(app2.Id, "hết chỗ", "test");
        Assert.Equal("hết chỗ", (await apps.LatestAsync(dung))!.Reason);
    }

    private static async Task<Guid> CreateId(ManagerService svc, Infrastructure.ClubsDbContext db, string name)
    {
        await svc.CreateAsync(name, name, "test");
        return (await db.Managers.SingleAsync(m => m.Username == name)).Id;
    }
}
