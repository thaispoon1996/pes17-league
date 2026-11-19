using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Application;
using Pes17League.Clubs.Infrastructure;

namespace Pes17League.Clubs.Tests;

/// <summary>Giao hữu người vs người (PO duyệt 16/11).</summary>
public class FriendlyTests
{
    [Fact]
    public void Elo_is_zero_sum_and_favours_underdog()
    {
        var (h, a) = FriendlyRules.EloDelta(1000, 1000, 1, 0);
        Assert.Equal(16, h); Assert.Equal(-16, a);
        var (u, _) = FriendlyRules.EloDelta(900, 1100, 2, 1);
        Assert.True(u > 16);
        Assert.Equal((0, 0), FriendlyRules.EloDelta(1000, 1000, 1, 1));
        Assert.Equal(100, FriendlyRules.Window(TimeSpan.FromSeconds(29)));
        Assert.Equal(200, FriendlyRules.Window(TimeSpan.FromSeconds(61)));
        Assert.Equal(FinanceRules.WinPrize / 4, FriendlyRules.DefaultWinPrize);
    }

    private static Manager NewManager(string u, int elo = 1000) => new() { Username = u, DisplayName = u.ToUpper(), PasswordHash = "x", Elo = elo };

    private static async Task<(Manager A, Manager B, Club CA, Club CB, Season S)> SeedAsync(ClubsDbContext db, DateTimeOffset now, int eloB = 1000)
    {
        var a = NewManager("an"); var b = NewManager("binh", eloB);
        var ca = new Club { Name = "CLB A" }; var cb = new Club { Name = "CLB B" };
        var s = new Season { Name = "Mùa 1", Number = 1, Status = SeasonStatus.Active, CreatedAt = now };
        db.AddRange(a, b, ca, cb, s);
        db.TeamAssignments.AddRange(
            new TeamAssignment { ClubId = ca.Id, ParticipantId = a.Id, ControllerType = ControllerType.Human, From = now },
            new TeamAssignment { ClubId = cb.Id, ParticipantId = b.Id, ControllerType = ControllerType.Human, From = now });
        await db.SaveChangesAsync();
        return (a, b, ca, cb, s);
    }

    [DbFact]
    public async Task Ranked_matchmaking_respects_elo_window_then_widens()
    {
        await using var t = await TestDb.CreateAsync();
        await using var db = t.Context();
        var (a, b, _, _, _) = await SeedAsync(db, t.Clock.GetUtcNow(), eloB: 1180);
        var svc = new FriendlyService(db, t.Clock);
        Assert.Null(await svc.FindAsync(a.Id, FriendlyMode.Ranked));
        Assert.Null(await svc.FindAsync(b.Id, FriendlyMode.Ranked));        // chênh 180 > 100
        t.Clock.Advance(TimeSpan.FromSeconds(61));                           // cửa sổ cả 2 vé = 200
        var m = await svc.FindAsync(b.Id, FriendlyMode.Ranked);
        Assert.NotNull(m);
        Assert.Equal(FriendlyStatus.Picking, m.Status);
        Assert.Equal(a.Id, m.HomeManagerId);                                  // chờ trước làm chủ nhà
        Assert.Equal("CLB A", m.HomeTeamName);
        Assert.NotNull(m.SeasonId);
        Assert.Null(await svc.TicketAsync(a.Id));
        Assert.Equal(m.Id, (await svc.FindAsync(a.Id, FriendlyMode.Free))!.Id);  // đang có trận mở → trả trận đó
    }

    [DbFact]
    public async Task Ranked_needs_club_free_does_not()
    {
        await using var t = await TestDb.CreateAsync();
        await using var db = t.Context();
        var c = NewManager("chi"); var d = NewManager("dung");
        db.AddRange(c, d); await db.SaveChangesAsync();
        var svc = new FriendlyService(db, t.Clock);
        await Assert.ThrowsAsync<AdminRuleException>(() => svc.FindAsync(c.Id, FriendlyMode.Ranked));
        Assert.Null(await svc.FindAsync(c.Id, FriendlyMode.Free));
        var m = await svc.FindAsync(d.Id, FriendlyMode.Free);
        Assert.NotNull(m);
        await Assert.ThrowsAsync<AdminRuleException>(() => svc.ReadyAsync(m.Id, c.Id));   // chưa chọn đội
        await svc.PickTeamAsync(m.Id, c.Id, 7, "Brazil");
        await svc.PickTeamAsync(m.Id, d.Id, 7, "Brazil");                                 // trùng đội được
        await svc.ReadyAsync(m.Id, c.Id);
        Assert.Equal(FriendlyStatus.Queued, (await svc.ReadyAsync(m.Id, d.Id)).Status);

        // PO 17/11: giao hữu cá nhân (không cần CLB) tính Elo xếp hạng, không có tiền thưởng
        await svc.StartNextAsync();
        await svc.RecordResultAsync(m.Id, 1, 0, []);
        Assert.NotEqual(1000, c.Elo);
        Assert.Equal(2000, c.Elo + d.Elo);
        Assert.False(await db.Ledger.AnyAsync());
        Assert.Single(await svc.StandingsAsync(null, mode: FriendlyMode.Free), r => r.Won == 1);
    }

    [DbFact]
    public async Task Challenge_expires_and_picking_deadline_auto_readies()
    {
        await using var t = await TestDb.CreateAsync();
        await using var db = t.Context();
        var (a, b, _, _, _) = await SeedAsync(db, t.Clock.GetUtcNow());
        var svc = new FriendlyService(db, t.Clock);
        var inv = await svc.ChallengeAsync(a.Id, b.Id, FriendlyMode.Ranked);
        await Assert.ThrowsAsync<AdminRuleException>(() => svc.ChallengeAsync(b.Id, a.Id, FriendlyMode.Free));   // a đang bận
        t.Clock.Advance(TimeSpan.FromMinutes(3));
        await svc.SweepAsync();
        Assert.Equal(FriendlyStatus.Expired, (await svc.GetAsync(inv.Id)).Status);

        var inv2 = await svc.ChallengeAsync(a.Id, b.Id, FriendlyMode.Ranked);
        await Assert.ThrowsAsync<AdminRuleException>(() => svc.RespondAsync(inv2.Id, a.Id, true));   // chỉ người được mời
        await svc.RespondAsync(inv2.Id, b.Id, true);
        await svc.ReadyAsync(inv2.Id, a.Id);
        t.Clock.Advance(TimeSpan.FromSeconds(91));
        Assert.Equal(1, await svc.SweepAsync());
        Assert.Equal(FriendlyStatus.Queued, (await svc.GetAsync(inv2.Id)).Status);
    }

    [DbFact]
    public async Task Ranked_result_moves_elo_pays_prize_and_fills_table()
    {
        await using var t = await TestDb.CreateAsync();
        await using var db = t.Context();
        var (a, b, ca, cb, s) = await SeedAsync(db, t.Clock.GetUtcNow());
        var svc = new FriendlyService(db, t.Clock);
        var m = await svc.ChallengeAsync(a.Id, b.Id, FriendlyMode.Ranked);
        await svc.RespondAsync(m.Id, b.Id, true);
        await svc.ReadyAsync(m.Id, a.Id); await svc.ReadyAsync(m.Id, b.Id);
        Assert.Equal(m.Id, (await svc.StartNextAsync())!.Id);
        await svc.RecordResultAsync(m.Id, 2, 1, [new FriendlyGoal { Home = true, Half = 1, Minute = 10, ScorerName = "X" }]);

        await using var db2 = t.Context();
        Assert.Equal(1016, (await db2.Managers.SingleAsync(x => x.Id == a.Id)).Elo);
        Assert.Equal(984, (await db2.Managers.SingleAsync(x => x.Id == b.Id)).Elo);
        var pay = await db2.Ledger.Where(l => l.Reason == LedgerReason.FriendlyPrize).ToListAsync();
        Assert.Equal(ca.Id, Assert.Single(pay).ClubId);
        Assert.Equal(FriendlyRules.DefaultWinPrize, pay[0].Amount);
        Assert.Single(await db2.FriendlyGoals.Where(g => g.MatchId == m.Id).ToListAsync());

        // đá lại: người rời sau hiệp 1 → thua 0-3
        var r = await svc.RematchAsync(m.Id, b.Id);
        Assert.Equal(r.Id, (await svc.RematchAsync(m.Id, a.Id)).Id);   // bên kia bấm sau → cùng phòng
        await svc.ReadyAsync(r.Id, a.Id); await svc.ReadyAsync(r.Id, b.Id);
        await svc.StartNextAsync();
        await svc.AbandonAsync(r.Id, a.Id, afterFirstHalf: true);
        var table = await svc.StandingsAsync(s.Id);
        Assert.Equal(2, table.Count);
        Assert.All(table, row => Assert.Equal(3, row.Points));
        Assert.Equal(table, await svc.StandingsAsync(null));
        var st = await svc.StatsAsync(a.Id, FriendlyMode.Ranked);
        Assert.Equal((2, 1, 0, 1, 2, 4), (st.Played, st.Won, st.Drawn, st.Lost, st.GoalsFor, st.GoalsAgainst));
        Assert.Equal(2, await db2.Ledger.CountAsync(l => l.Reason == LedgerReason.FriendlyPrize));
        _ = cb;
    }

    [DbFact]
    public async Task Leaving_before_half_time_voids_and_chat_is_room_scoped()
    {
        await using var t = await TestDb.CreateAsync();
        await using var db = t.Context();
        var (a, b, _, _, _) = await SeedAsync(db, t.Clock.GetUtcNow());
        var outsider = NewManager("ngoai"); db.Add(outsider); await db.SaveChangesAsync();
        var svc = new FriendlyService(db, t.Clock);
        await svc.FindAsync(a.Id, FriendlyMode.Free);
        var m = (await svc.FindAsync(b.Id, FriendlyMode.Free))!;
        await svc.ChatAsync(m.Id, a.Id, "  chào  ");
        await svc.ChatAsync(null, outsider.Id, new string('x', 400));
        await Assert.ThrowsAsync<AdminRuleException>(() => svc.ChatAsync(m.Id, outsider.Id, "vào ké"));
        Assert.Equal("chào", Assert.Single(await svc.ChatLogAsync(m.Id)).Text);
        Assert.Equal(300, Assert.Single(await svc.ChatLogAsync(null)).Text.Length);
        await svc.PickTeamAsync(m.Id, a.Id, 1, "A"); await svc.PickTeamAsync(m.Id, b.Id, 2, "B");
        await svc.ReadyAsync(m.Id, a.Id); await svc.ReadyAsync(m.Id, b.Id);
        await svc.StartNextAsync();
        await svc.AbandonAsync(m.Id, b.Id, afterFirstHalf: false);
        Assert.Equal(FriendlyStatus.Voided, (await svc.GetAsync(m.Id)).Status);
        Assert.Equal(0, (await svc.StatsAsync(a.Id, FriendlyMode.Free)).Played);
    }
}
