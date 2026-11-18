using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Infrastructure;

namespace Pes17League.Clubs.Application;

public enum MailFolder { Inbox, Sent, Archived }

/// <summary>Người nhận: <see cref="ManagerId"/> (người chơi), <see cref="ClubId"/> (HLV đang dẫn CLB) hoặc <see cref="Admin"/> (Ban tổ chức).</summary>
public sealed record MailTo(Guid? ManagerId = null, Guid? ClubId = null, bool Admin = false);

public sealed record MailDraft(MailTo To, string Subject, string Body, string? Template = null, Guid? PlayerId = null, Guid? FixtureId = null,
    Guid? ReplyTo = null);

public sealed record MailRow(Guid MessageId, Guid ThreadId, string From, string To, string Subject, string Snippet, DateTimeOffset At, bool Read,
    bool System, int InThread);

public sealed record MailView(Guid Id, string From, string? FromClub, Guid? FromClubId, Guid? FromManagerId, bool FromAdmin, string To, string Subject,
    string Body, DateTimeOffset At, string? Template, Guid? PlayerId, Guid? FixtureId, bool Mine);

/// <summary>Hòm thư (PO 17/11). <c>me</c> null = Ban tổ chức (admin). Thư hệ thống: <see cref="SystemAsync"/>.</summary>
public sealed class MailService(ClubsDbContext db, TimeProvider clock)
{
    public const string AdminName = "Ban tổ chức", SystemName = "Hệ thống PES 17 League";

    public async Task<MailMessage> SendAsync(Guid? me, MailDraft d, CancellationToken ct = default)
    {
        var subject = d.Subject.Trim();
        var body = d.Body.Trim();
        if (subject.Length == 0) throw new AdminRuleException("Thư cần tiêu đề");
        if (body.Length == 0) throw new AdminRuleException("Thư cần nội dung");
        if (subject.Length > MailTemplates.SubjectMax) throw new AdminRuleException($"Tiêu đề tối đa {MailTemplates.SubjectMax} ký tự");
        if (body.Length > MailTemplates.BodyMax) throw new AdminRuleException($"Nội dung tối đa {MailTemplates.BodyMax} ký tự");
        var now = clock.GetUtcNow();
        if (me is { } id && await db.MailMessages.CountAsync(m => m.FromManagerId == id && m.At > now.AddHours(-1), ct) >= MailTemplates.PerHour)
            throw new AdminRuleException($"Gửi tối đa {MailTemplates.PerHour} thư mỗi giờ");

        var (toManager, toClub) = await ResolveAsync(d.To, ct);
        if (me is not null && toManager == me) throw new AdminRuleException("Không gửi thư cho chính mình");
        if (me is { } sender && toManager is { } rcpt && await db.MailBlocks.AnyAsync(b => b.ManagerId == rcpt && b.BlockedManagerId == sender, ct))
            throw new AdminRuleException("Người nhận đã chặn thư của bạn");

        var thread = Guid.Empty;
        if (d.ReplyTo is { } parent)
        {
            var p = await db.MailMessages.SingleOrDefaultAsync(m => m.Id == parent, ct) ?? throw new AdminRuleException("Không có thư này");
            if (!await CanSeeAsync(me, p, ct)) throw new AdminRuleException("Không có thư này");
            thread = p.ThreadId;
        }
        var msg = new MailMessage
        {
            FromManagerId = me, FromClubId = me is { } m2 ? await ClubOfAsync(m2, ct) : null, Subject = subject, Body = body,
            Template = MailTemplates.Find(d.Template)?.Key, PlayerId = d.PlayerId, FixtureId = d.FixtureId, At = now,
        };
        msg.ThreadId = thread == Guid.Empty ? msg.Id : thread;
        db.MailMessages.Add(msg);
        db.MailRecipients.Add(new MailRecipient { MessageId = msg.Id, ManagerId = toManager, ClubId = toClub });
        await db.SaveChangesAsync(ct);
        return msg;
    }

    /// <summary>Thư hệ thống (đề nghị chuyển nhượng, đơn xin CLB…) tới 1 người chơi. Không lưu nếu người nhận null.</summary>
    public async Task SystemAsync(Guid? managerId, string subject, string body, Guid? playerId = null, Guid? fixtureId = null, CancellationToken ct = default)
    {
        if (managerId is null) return;
        await SystemCoreAsync(managerId, subject, body, playerId, fixtureId, ct);
    }

    /// <summary>Thư hệ thống vào hòm Ban tổ chức (cảnh cáo vắng, máy BOT tắt…).</summary>
    public Task SystemToAdminAsync(string subject, string body, CancellationToken ct = default) =>
        SystemCoreAsync(null, subject, body, null, null, ct);

    private async Task SystemCoreAsync(Guid? managerId, string subject, string body, Guid? playerId, Guid? fixtureId, CancellationToken ct)
    {
        var msg = new MailMessage
        {
            FromSystem = true, Subject = subject.Length > MailTemplates.SubjectMax ? subject[..MailTemplates.SubjectMax] : subject,
            Body = body.Length > MailTemplates.BodyMax ? body[..MailTemplates.BodyMax] : body, PlayerId = playerId, FixtureId = fixtureId, At = clock.GetUtcNow(),
        };
        msg.ThreadId = msg.Id;
        db.MailMessages.Add(msg);
        db.MailRecipients.Add(new MailRecipient { MessageId = msg.Id, ManagerId = managerId });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>HLV người đang dẫn CLB (null = CLB BOT).</summary>
    public Task<Guid?> ManagerOfClubAsync(Guid clubId, CancellationToken ct = default) =>
        db.TeamAssignments.Where(a => a.ClubId == clubId && a.To == null && a.ControllerType == ControllerType.Human && a.ParticipantId != null)
            .Select(a => a.ParticipantId).FirstOrDefaultAsync(ct);

    public Task<Guid?> ClubOfAsync(Guid managerId, CancellationToken ct = default) =>
        db.TeamAssignments.Where(a => a.ParticipantId == managerId && a.To == null && a.ControllerType == ControllerType.Human)
            .Select(a => (Guid?)a.ClubId).FirstOrDefaultAsync(ct);

    private async Task<(Guid? Manager, Guid? Club)> ResolveAsync(MailTo to, CancellationToken ct)
    {
        if (to.Admin) return (null, null);
        if (to.ClubId is { } club)
        {
            var name = await db.Clubs.Where(c => c.Id == club).Select(c => c.Name).SingleOrDefaultAsync(ct) ?? throw new AdminRuleException("Không có CLB này");
            return (await ManagerOfClubAsync(club, ct) ?? throw new AdminRuleException($"{name} đang do BOT dẫn, không có người nhận thư"), club);
        }
        if (to.ManagerId is { } m && await db.Managers.AnyAsync(x => x.Id == m && !x.Disabled, ct)) return (m, null);
        throw new AdminRuleException("Chưa chọn người nhận");
    }

    private async Task<bool> CanSeeAsync(Guid? me, MailMessage m, CancellationToken ct) =>
        (me is not null ? m.FromManagerId == me : m.FromManagerId == null && !m.FromSystem)
        || await db.MailRecipients.AnyAsync(r => r.MessageId == m.Id && r.ManagerId == me, ct);

    public Task<int> UnreadAsync(Guid? me, CancellationToken ct = default) =>
        db.MailRecipients.CountAsync(r => r.ManagerId == me && r.ReadAt == null && !r.Archived, ct);

    /// <summary>Danh sách thư theo chuỗi (thư mới nhất của mỗi chuỗi), mới trước.</summary>
    public async Task<List<MailRow>> ListAsync(Guid? me, MailFolder folder, int take = 100, CancellationToken ct = default)
    {
        List<(MailMessage M, MailRecipient R)> rows;
        if (folder == MailFolder.Sent)
        {
            var q = me is not null ? db.MailMessages.Where(m => m.FromManagerId == me) : db.MailMessages.Where(m => m.FromManagerId == null && !m.FromSystem);
            rows = (await (from m in q.Where(m => !m.SenderArchived) join r in db.MailRecipients on m.Id equals r.MessageId
                           orderby m.At descending select new { m, r }).Take(take * 3).ToListAsync(ct)).Select(x => (x.m, x.r)).ToList();
        }
        else
        {
            var archived = folder == MailFolder.Archived;
            rows = (await (from r in db.MailRecipients.Where(r => r.ManagerId == me && r.Archived == archived) join m in db.MailMessages on r.MessageId equals m.Id
                           orderby m.At descending select new { m, r }).Take(take * 3).ToListAsync(ct)).Select(x => (x.m, x.r)).ToList();
        }
        var latest = rows.GroupBy(x => x.M.ThreadId).Select(g => (Last: g.First(), Count: g.Count(), Unread: g.Any(x => x.R.ReadAt == null))).Take(take).ToList();
        var names = await NamesAsync(latest.SelectMany(x => new[] { x.Last.M.FromManagerId, x.Last.R.ManagerId }), latest.Select(x => x.Last.R.ClubId), ct);
        return latest.Select(x => new MailRow(x.Last.M.Id, x.Last.M.ThreadId, FromName(x.Last.M, names), ToName(x.Last.R, names), x.Last.M.Subject,
            Snippet(x.Last.M.Body), x.Last.M.At, folder == MailFolder.Sent || !x.Unread, x.Last.M.FromSystem, x.Count)).ToList();
    }

    /// <summary>Cả chuỗi thư mà <paramref name="me"/> được xem (gửi hoặc nhận), cũ trước; đánh dấu đã đọc.</summary>
    public async Task<List<MailView>> ThreadAsync(Guid? me, Guid threadId, CancellationToken ct = default)
    {
        var msgs = await db.MailMessages.Where(m => m.ThreadId == threadId).OrderBy(m => m.At).ToListAsync(ct);
        var ids = msgs.Select(m => m.Id).ToList();
        var rcpts = await db.MailRecipients.Where(r => ids.Contains(r.MessageId)).ToListAsync(ct);
        bool Sent(MailMessage m) => me is not null ? m.FromManagerId == me : m.FromManagerId == null && !m.FromSystem;
        var visible = msgs.Where(m => Sent(m) || rcpts.Any(r => r.MessageId == m.Id && r.ManagerId == me)).ToList();
        var now = clock.GetUtcNow();
        var changed = false;
        foreach (var r in rcpts.Where(r => r.ManagerId == me && r.ReadAt == null)) { r.ReadAt = now; changed = true; }
        if (changed) await db.SaveChangesAsync(ct);
        var names = await NamesAsync(visible.Select(m => m.FromManagerId).Concat(rcpts.Select(r => r.ManagerId)),
            visible.Select(m => m.FromClubId).Concat(rcpts.Select(r => r.ClubId)), ct);
        return visible.Select(m =>
        {
            var to = rcpts.FirstOrDefault(r => r.MessageId == m.Id);
            return new MailView(m.Id, FromName(m, names), m.FromClubId is { } c ? names.GetValueOrDefault(c) : null, m.FromClubId, m.FromManagerId,
                m.FromManagerId is null && !m.FromSystem, to is null ? "" : ToName(to, names), m.Subject, m.Body, m.At, m.Template, m.PlayerId, m.FixtureId, Sent(m));
        }).ToList();
    }

    /// <summary>Cất / đưa lại chuỗi thư vào hộp đến (phía nhận) và ẩn khỏi Đã gửi (phía gửi).</summary>
    public async Task ArchiveAsync(Guid? me, Guid threadId, bool archived, CancellationToken ct = default)
    {
        var ids = await db.MailMessages.Where(m => m.ThreadId == threadId).Select(m => m.Id).ToListAsync(ct);
        foreach (var r in await db.MailRecipients.Where(r => ids.Contains(r.MessageId) && r.ManagerId == me).ToListAsync(ct)) r.Archived = archived;
        var sent = me is not null ? db.MailMessages.Where(m => ids.Contains(m.Id) && m.FromManagerId == me)
            : db.MailMessages.Where(m => ids.Contains(m.Id) && m.FromManagerId == null && !m.FromSystem);
        foreach (var m in await sent.ToListAsync(ct)) m.SenderArchived = archived;
        await db.SaveChangesAsync(ct);
    }

    public async Task MarkUnreadAsync(Guid? me, Guid threadId, CancellationToken ct = default)
    {
        var last = await (from r in db.MailRecipients join m in db.MailMessages on r.MessageId equals m.Id
                          where m.ThreadId == threadId && r.ManagerId == me orderby m.At descending select r).FirstOrDefaultAsync(ct);
        if (last is null) return;
        last.ReadAt = null;
        await db.SaveChangesAsync(ct);
    }

    public async Task BlockAsync(Guid me, Guid other, bool block, CancellationToken ct = default)
    {
        var b = await db.MailBlocks.FindAsync([me, other], ct);
        if (block && b is null && me != other) db.MailBlocks.Add(new MailBlock { ManagerId = me, BlockedManagerId = other, At = clock.GetUtcNow() });
        else if (!block && b is not null) db.MailBlocks.Remove(b);
        await db.SaveChangesAsync(ct);
    }

    public Task<bool> BlockedAsync(Guid me, Guid other, CancellationToken ct = default) =>
        db.MailBlocks.AnyAsync(b => b.ManagerId == me && b.BlockedManagerId == other, ct);

    private async Task<Dictionary<Guid, string>> NamesAsync(IEnumerable<Guid?> managers, IEnumerable<Guid?> clubs, CancellationToken ct)
    {
        var m = managers.OfType<Guid>().Distinct().ToList();
        var c = clubs.OfType<Guid>().Distinct().ToList();
        var d = await db.Managers.Where(x => m.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);
        foreach (var x in await db.Clubs.Where(x => c.Contains(x.Id)).Select(x => new { x.Id, x.Name }).ToListAsync(ct)) d[x.Id] = x.Name;
        return d;
    }

    private static string FromName(MailMessage m, Dictionary<Guid, string> n) =>
        m.FromSystem ? SystemName : m.FromManagerId is { } id ? n.GetValueOrDefault(id, "?") + (m.FromClubId is { } c && n.TryGetValue(c, out var cn) ? $" · {cn}" : "") : AdminName;

    private static string ToName(MailRecipient r, Dictionary<Guid, string> n) =>
        r.ManagerId is { } id ? n.GetValueOrDefault(id, "?") + (r.ClubId is { } c && n.TryGetValue(c, out var cn) ? $" · {cn}" : "") : AdminName;

    private static string Snippet(string body)
    {
        var s = string.Join(' ', body.Split((char[])['\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return s.Length > 120 ? s[..120] + "…" : s;
    }
}
