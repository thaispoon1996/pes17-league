using Pes17League.Clubs.Application;
using Pes17League.Clubs.Infrastructure;

namespace Pes17League.Clubs.Tests;

/// <summary>Hòm thư (PO duyệt 17/11).</summary>
public class MailTests
{
    [Fact]
    public void Template_fill_marks_missing_values()
    {
        var t = MailTemplates.Find("hoi-mua")!;
        var s = MailTemplates.Fill(t.Subject, new Dictionary<string, string?> { ["player"] = "Messi" });
        Assert.Equal("Đề nghị mua Messi", s);
        Assert.Contains("…", MailTemplates.Fill("{fee}", new Dictionary<string, string?> { ["fee"] = null }));
        Assert.True(MailTemplates.Find("khieu-nai")!.ToAdmin);
    }

    private static async Task<(Manager A, Manager B, Club CB, Club Bot)> SeedAsync(ClubsDbContext db, DateTimeOffset now)
    {
        var a = new Manager { Username = "an", DisplayName = "An", PasswordHash = "x" };
        var b = new Manager { Username = "binh", DisplayName = "Bình", PasswordHash = "x" };
        var cb = new Club { Name = "CLB B" }; var bot = new Club { Name = "CLB BOT" };
        db.AddRange(a, b, cb, bot);
        db.TeamAssignments.Add(new TeamAssignment { ClubId = cb.Id, ParticipantId = b.Id, ControllerType = ControllerType.Human, From = now });
        await db.SaveChangesAsync();
        return (a, b, cb, bot);
    }

    [DbFact]
    public async Task Send_to_club_reply_thread_archive_and_unread()
    {
        await using var t = await TestDb.CreateAsync();
        await using var db = t.Context();
        var (a, b, cb, bot) = await SeedAsync(db, t.Clock.GetUtcNow());
        var svc = new MailService(db, t.Clock);

        var m1 = await svc.SendAsync(a.Id, new MailDraft(new MailTo(ClubId: cb.Id), "Hỏi mua", "Nội dung", "hoi-mua"));
        Assert.Equal(1, await svc.UnreadAsync(b.Id));
        var inbox = await svc.ListAsync(b.Id, MailFolder.Inbox);
        Assert.Single(inbox);
        Assert.False(inbox[0].Read);
        Assert.Equal("An", inbox[0].From);
        Assert.Single(await svc.ListAsync(a.Id, MailFolder.Sent));

        var th = await svc.ThreadAsync(b.Id, m1.ThreadId);           // đọc → hết chưa đọc
        Assert.Single(th);
        Assert.Equal(0, await svc.UnreadAsync(b.Id));

        t.Clock.Advance(TimeSpan.FromMinutes(1));
        var m2 = await svc.SendAsync(b.Id, new MailDraft(new MailTo(ManagerId: a.Id), "Re: Hỏi mua", "Đồng ý", ReplyTo: m1.Id));
        Assert.Equal(m1.ThreadId, m2.ThreadId);
        var ta = await svc.ThreadAsync(a.Id, m1.ThreadId);
        Assert.Equal(2, ta.Count);
        Assert.True(ta[0].Mine); Assert.False(ta[1].Mine);
        Assert.Equal("Bình · CLB B", ta[1].From);
        Assert.Equal("Re: Hỏi mua", Assert.Single(await svc.ListAsync(a.Id, MailFolder.Inbox)).Subject);

        await svc.MarkUnreadAsync(a.Id, m1.ThreadId);
        Assert.Equal(1, await svc.UnreadAsync(a.Id));
        await svc.ArchiveAsync(a.Id, m1.ThreadId, true);
        Assert.Empty(await svc.ListAsync(a.Id, MailFolder.Inbox));
        Assert.Single(await svc.ListAsync(a.Id, MailFolder.Archived));
        Assert.Equal(0, await svc.UnreadAsync(a.Id));                  // lưu trữ không tính chưa đọc

        var ex = await Assert.ThrowsAsync<AdminRuleException>(() => svc.SendAsync(a.Id, new MailDraft(new MailTo(ClubId: bot.Id), "x", "y")));
        Assert.Contains("BOT", ex.Message);
        await Assert.ThrowsAsync<AdminRuleException>(() => svc.SendAsync(a.Id, new MailDraft(new MailTo(ManagerId: a.Id), "x", "y")));
        await Assert.ThrowsAsync<AdminRuleException>(() => svc.SendAsync(a.Id, new MailDraft(new MailTo(ManagerId: b.Id), " ", "y")));
    }

    [DbFact]
    public async Task Block_admin_box_system_and_rate_limit()
    {
        await using var t = await TestDb.CreateAsync();
        await using var db = t.Context();
        var (a, b, _, _) = await SeedAsync(db, t.Clock.GetUtcNow());
        var svc = new MailService(db, t.Clock);

        await svc.BlockAsync(b.Id, a.Id, true);
        Assert.True(await svc.BlockedAsync(b.Id, a.Id));
        await Assert.ThrowsAsync<AdminRuleException>(() => svc.SendAsync(a.Id, new MailDraft(new MailTo(ManagerId: b.Id), "x", "y")));
        await svc.BlockAsync(b.Id, a.Id, false);
        await svc.SendAsync(a.Id, new MailDraft(new MailTo(ManagerId: b.Id), "x", "y"));

        // khiếu nại tới Ban tổ chức (hòm admin = null), admin trả lời
        var k = await svc.SendAsync(a.Id, new MailDraft(new MailTo(Admin: true), "Khiếu nại", "…", "khieu-nai"));
        Assert.Equal(1, await svc.UnreadAsync(null));
        var adminView = await svc.ThreadAsync(null, k.ThreadId);
        Assert.Single(adminView);
        Assert.False(adminView[0].Mine);
        var r = await svc.SendAsync(null, new MailDraft(new MailTo(ManagerId: a.Id), "Re: Khiếu nại", "Đã xem", ReplyTo: k.Id));
        Assert.Equal(k.ThreadId, r.ThreadId);
        Assert.Equal(MailService.AdminName, (await svc.ThreadAsync(a.Id, k.ThreadId))[1].From);
        Assert.Empty(await svc.ThreadAsync(b.Id, k.ThreadId));            // người ngoài không xem được

        await svc.SystemAsync(b.Id, "Có đề nghị mới", "…");
        var sys = (await svc.ListAsync(b.Id, MailFolder.Inbox)).First(x => x.System);
        Assert.Equal(MailService.SystemName, sys.From);
        Assert.Empty(await svc.ListAsync(null, MailFolder.Sent).ContinueWith(x => x.Result.Where(r => r.System).ToList()));

        for (var i = 0; i < MailTemplates.PerHour - 2; i++) await svc.SendAsync(a.Id, new MailDraft(new MailTo(ManagerId: b.Id), "x", "y"));
        await Assert.ThrowsAsync<AdminRuleException>(() => svc.SendAsync(a.Id, new MailDraft(new MailTo(ManagerId: b.Id), "x", "y")));
        t.Clock.Advance(TimeSpan.FromHours(1.1));
        await svc.SendAsync(a.Id, new MailDraft(new MailTo(ManagerId: b.Id), "x", "y"));
    }
}
