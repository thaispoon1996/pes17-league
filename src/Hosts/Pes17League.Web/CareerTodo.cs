using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Application;
using Pes17League.Clubs.Domain;
using Pes17League.Clubs.Infrastructure;
using Pes17League.Web.Components.Layout;

namespace Pes17League.Web;

/// <summary>Một việc cần xử lý trên chuông thông báo. Tone: gold = việc, red = gấp/cảnh báo, cyan = tin.</summary>
public sealed record TodoItem(string Icon, string Tone, string Title, string? Text, string Href, string? Chip);

/// <summary>Chuông thông báo đầu trang (PO 22/11, thay khối "Cần xử lý" ở trang chủ): mục mới mở khoá, thư HLV cần quyết định (theo hạn),
/// thư chưa đọc, việc và cảnh báo từ bàn làm việc (MediaService). Mục trỏ tới tính năng chưa mở thì đưa về trang gần nhất.</summary>
public static class CareerTodo
{
    public static async Task<List<TodoItem>> LoadAsync(ClubsDbContext db, TimeProvider clock, Guid managerId, CareerProgress? p, CancellationToken ct = default)
    {
        var todo = new List<TodoItem>();
        if (p?.Unlocked.Where(p.IsNew).OrderBy(f => CareerUnlocks.Introduced.ToList().IndexOf(f)).LastOrDefault() is { } nf
            && CareerLayout.Intros.TryGetValue(nf, out var nfo))
            todo.Add(new("lock_open", "gold", "Mới mở khoá: " + nfo.Title, nfo.Text, nfo.Href, "Mới"));

        var coach = await db.Coaches.AsNoTracking().Where(c => c.ManagerId == managerId).Select(c => new { c.Id, c.Status }).FirstOrDefaultAsync(ct);
        if (coach is not null && coach.Status != CoachStatus.Creating)
        {
            var now = await CoachService.NowTickAsync(db, ct);
            var waiting = await db.CoachLetters.AsNoTracking().Where(l => l.CoachId == coach.Id && l.Kind == LetterKind.Decision && l.Chosen == null && !l.Archived)
                .OrderBy(l => l.DeadlineTick).Select(l => new { l.Id, l.Subject, l.Sender, l.DeadlineTick }).ToListAsync(ct);
            foreach (var l in waiting)
            {
                int? left = l.DeadlineTick is { } d && d / 100 == now / 100 && d > now ? d - now : null;
                todo.Add(new("gavel", left is <= 1 ? "red" : "gold", l.Subject, "Thư từ " + l.Sender, $"thu-hlv?thu={l.Id}", left is { } n ? $"Còn {n} vòng" : "Cần quyết định"));
            }
            var unread = await db.CoachLetters.CountAsync(l => l.CoachId == coach.Id && !l.Outgoing && !l.Read && !l.Archived && !(l.Kind == LetterKind.Decision && l.Chosen == null), ct);
            if (unread > 0) todo.Add(new("mail", "cyan", $"{unread} thư chưa đọc", "Hòm thư HLV", "thu-hlv", null));
        }
        if (p is null) return todo;

        string? Gate(string? link) => link switch
        {
            null => null,
            "tap-luyen" when !p.Has(CareerUnlocks.Training) => null,
            "tai-chinh" when !p.Has(CareerUnlocks.Finance) => null,
            "ban-lanh-dao" when !p.Has(CareerUnlocks.Board) => null,
            "chuyen-nhuong" when !p.Has(CareerUnlocks.Transfer) => "clb",
            _ => link,
        };
        var desk = await new MediaService(db, clock).DeskAsync(p.SeasonId, p.ClubId, ct);
        // Việc trước, cảnh báo sau; "Chuẩn bị vòng" đã có ở thẻ trận tiếp theo; tin (báo cáo, bài báo, BLĐ) nằm ở Trang nhất / ô chỉ số.
        foreach (var i in desk.Inbox.Where(i => i.Kind == InboxKind.Task && !(i.Link ?? "").StartsWith("chuan-bi")))
            todo.Add(new(Icon(i), "gold", i.Title, i.Body, Gate(i.Link) ?? "hop-thu", null));
        foreach (var i in desk.Inbox.Where(i => i.Kind == InboxKind.Warning))
            todo.Add(new(Icon(i), "red", i.Title, i.Body, Gate(i.Link) ?? "phong-thay-do", null));
        foreach (var i in desk.Inbox.Where(i => i.Kind == InboxKind.Info && i.Link is null))
            todo.Add(new(Icon(i), "cyan", i.Title, i.Body, "clb", null));
        return todo;
    }

    static string Icon(InboxItem i) => i.Link switch
    {
        "de-nghi" => "handshake",
        "tap-luyen" when i.Kind == InboxKind.Task => "fitness_center",
        "tap-luyen" => "healing",
        "chuyen-nhuong" => "contract_edit",
        "tai-chinh" => "account_balance",
        "ban-lanh-dao" => "domain",
        _ when i.Title.Contains("Họp báo") => "mic",
        _ when i.Title.Contains("mệt") => "battery_alert",
        _ when i.Title.Contains("không vui") => "sentiment_dissatisfied",
        _ => i.Kind == InboxKind.Info ? "pending_actions" : "assignment",
    };
}
