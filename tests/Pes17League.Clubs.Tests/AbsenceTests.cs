using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Application;
using Pes17League.Clubs.Infrastructure;

namespace Pes17League.Clubs.Tests;

/// <summary>Trận giải người vs người (Sẵn sàng 2 bên) + đếm vắng 3 cảnh cáo / 5 về BOT (PO 17/11).</summary>
public class AbsenceTests
{
    private sealed record World(Season Season, CompetitionEdition Edition, Manager A, Manager B, Club CA, Club CB, Club Bot);

    private static async Task<World> SeedAsync(ClubsDbContext db, DateTimeOffset now)
    {
        var season = new Season { Name = "Mùa 1", Number = 2, CreatedAt = now, Status = SeasonStatus.Active };
        var comp = new Competition { Name = "Serie A", Type = CompetitionType.DomesticLeague, CreatedAt = now };
        var edition = new CompetitionEdition { CompetitionId = comp.Id, SeasonId = season.Id };
        var a = new Manager { Username = "an", DisplayName = "An", PasswordHash = "x" };
        var b = new Manager { Username = "binh", DisplayName = "Bình", PasswordHash = "x" };
        var w = new Manager { Username = "cho", DisplayName = "Chờ", PasswordHash = "x" };
        Club ca = new() { Name = "CLB A" }, cb = new() { Name = "CLB B" }, bot = new() { Name = "CLB BOT" };
        db.AddRange(season, comp, edition, a, b, w, ca, cb, bot);
        db.TeamAssignments.AddRange(
            new TeamAssignment { ClubId = ca.Id, ParticipantId = a.Id, ControllerType = ControllerType.Human, From = now },
            new TeamAssignment { ClubId = cb.Id, ParticipantId = b.Id, ControllerType = ControllerType.Human, From = now },
            new TeamAssignment { ClubId = bot.Id, ControllerType = ControllerType.Bot, From = now });
        db.LeagueApplications.Add(new LeagueApplication { ManagerId = w.Id, Status = LeagueApplicationStatus.Pending, CreatedAt = now });
        await db.SaveChangesAsync();
        return new(season, edition, a, b, ca, cb, bot);
    }

    private static async Task<(Matchday Md, Fixture F)> RoundAsync(ClubsDbContext db, World w, int n, Guid home, Guid away)
    {
        var md = new Matchday { SeasonId = w.Season.Id, Number = n, Status = MatchdayStatus.WindowOpen };
        var f = new Fixture { MatchdayId = md.Id, HomeClubId = home, AwayClubId = away, CompetitionEditionId = w.Edition.Id };
        db.AddRange(md, f);
        await db.SaveChangesAsync();
        return (md, f);
    }

    [DbFact]
    public async Task Duel_needs_both_ready_and_unready_clears()
    {
        await using var t = await TestDb.CreateAsync();
        await using var db = t.Context();
        var w = await SeedAsync(db, t.Clock.GetUtcNow());
        var (_, f) = await RoundAsync(db, w, 1, w.CA.Id, w.CB.Id);
        var (_, solo) = await RoundAsync(db, w, 2, w.CA.Id, w.Bot.Id);
        var svc = new LeagueDuelService(db, t.Clock);

        Assert.Null(await svc.SeatAsync(solo.Id, w.A.Id));                         // người vs BOT: không phải trận đôi
        Assert.Equal(MatchSide.Away, (await svc.SeatAsync(f.Id, w.B.Id))!.Value.Side);
        await Assert.ThrowsAsync<AdminRuleException>(() => svc.ReadyAsync(f.Id, Guid.NewGuid(), true));

        await svc.ReadyAsync(f.Id, w.A.Id, true);
        Assert.Empty(await svc.ReadyAsync());
        await svc.ReadyAsync(f.Id, w.B.Id, true);
        var ready = Assert.Single(await svc.ReadyAsync());
        Assert.Equal((f.Id, w.A.Id, w.B.Id), (ready.Fixture.Id, ready.Home, ready.Away));

        await svc.UnreadyAsync(f.Id);
        db.ChangeTracker.Clear();
        Assert.Empty(await svc.ReadyAsync());
    }

    [DbFact]
    public async Task Deadline_counts_absence_warns_at_3_releases_at_5_and_playing_resets()
    {
        await using var t = await TestDb.CreateAsync();
        await using var db = t.Context();
        var w = await SeedAsync(db, t.Clock.GetUtcNow());
        var svc = new AbsenceService(db, t.Clock);
        TeamAssignment Seat(Guid club) => db.TeamAssignments.Single(a => a.ClubId == club && a.To == null);

        // vòng 1: trận đôi, A Sẵn sàng, B không → B vắng, A có mặt
        var (md1, f1) = await RoundAsync(db, w, 1, w.CA.Id, w.CB.Id);
        f1.HomeReadyAt = t.Clock.GetUtcNow();
        await db.SaveChangesAsync();
        var r = await svc.ApplyDeadlineAsync(md1.Id);
        Assert.Equal(1, r.Assisted);
        Assert.Equal(["CLB B"], r.Absent);
        Assert.True(f1.AssistantPlays && f1.AwayAbsent && !f1.HomeAbsent);
        Assert.Equal((0, 1), (Seat(w.CA.Id).MissedInRow, Seat(w.CB.Id).MissedInRow));

        // vòng 2–3: B vắng trận gặp BOT → vòng 3 cảnh cáo (1 lần)
        for (var n = 2; n <= 3; n++)
        {
            var (md, _) = await RoundAsync(db, w, n, w.Bot.Id, w.CB.Id);
            r = await svc.ApplyDeadlineAsync(md.Id);
        }
        Assert.Equal(["CLB B"], r.Warned);
        Assert.NotNull(Seat(w.CB.Id).MissWarnedAt);
        Assert.Equal(1, await db.MailRecipients.CountAsync(x => x.ManagerId == w.B.Id));
        Assert.Equal(1, await db.MailRecipients.CountAsync(x => x.ManagerId == null));   // Ban tổ chức

        // A tự giao trợ lý (không vắng) → đá xong: chuỗi vắng về 0 (A vốn 0); B: giả lập có mặt → reset
        var (md4, f4) = await RoundAsync(db, w, 4, w.CB.Id, w.Bot.Id);
        f4.AssistantPlays = true;   // B tự giao trợ lý trước hạn
        await db.SaveChangesAsync();
        r = await svc.ApplyDeadlineAsync(md4.Id);
        Assert.Equal(0, r.Assisted);
        await svc.ResetAsync(f4);
        await db.SaveChangesAsync();
        Assert.Equal(0, Seat(w.CB.Id).MissedInRow);
        Assert.Null(Seat(w.CB.Id).MissWarnedAt);

        // vắng 5 vòng liên tiếp → về BOT, người chờ nhận thư
        for (var n = 5; n <= 9; n++)
        {
            var (md, _) = await RoundAsync(db, w, n, w.CB.Id, w.Bot.Id);
            r = await svc.ApplyDeadlineAsync(md.Id);
        }
        Assert.Equal(["CLB B"], r.Released);
        Assert.Equal(ControllerType.Bot, (await db.TeamAssignments.SingleAsync(a => a.ClubId == w.CB.Id && a.To == null)).ControllerType);
        var waiting = await db.LeagueApplications.Select(x => x.ManagerId).SingleAsync();
        Assert.Equal(1, await db.MailRecipients.CountAsync(x => x.ManagerId == waiting));
    }
}
