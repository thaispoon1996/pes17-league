using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Application;

namespace Pes17League.Clubs.Tests;

/// <summary>Chung kết hoà sau 120' → loạt penalty trong game (PO duyệt 06/11): lưu riêng tỷ số penalty, đội thắng penalty là đội thắng trận,
/// không đọc được → ghi tỷ số 120', admin nhập penalty sau.</summary>
public class FinalPenaltyTests
{
    private sealed class Runner(Func<MatchRunRequest, MatchRunOutcome> play) : IMatchRunner
    {
        public List<MatchRunRequest> Calls { get; } = [];

        public Task<MatchRunOutcome> RunAsync(MatchRunRequest r, CancellationToken ct)
        {
            lock (Calls) Calls.Add(r);
            return Task.FromResult(play(r));
        }
    }

    /// <summary>Giải thử nghiệm không có chung kết thật → đánh dấu 1 trận của vòng 1 là chung kết.</summary>
    private static async Task<Guid> MarkFinalAsync(TestDb t, Guid md1)
    {
        await using var db = t.Context();
        var f = await db.Fixtures.Where(x => x.MatchdayId == md1).OrderBy(x => x.Id).FirstAsync();
        f.Stage = FixtureStage.Final;
        await db.SaveChangesAsync();
        return f.Id;
    }

    [Fact]
    public void Penalties_only_after_draw_and_decide_winner()
    {
        Assert.True(Fixture.PenaltiesValid(1, 1, 3, 2));
        Assert.False(Fixture.PenaltiesValid(2, 1, 3, 2));      // có đội thắng sau 120'
        Assert.False(Fixture.PenaltiesValid(1, 1, 3, 3));      // loạt penalty luôn có đội thắng
        Assert.False(Fixture.PenaltiesValid(0, 0, -1, 2));
        var (home, away) = (Guid.NewGuid(), Guid.NewGuid());
        Fixture F(int? hg, int? ag, int? hp = null, int? ap = null) =>
            new() { HomeClubId = home, AwayClubId = away, HomeGoals = hg, AwayGoals = ag, HomePenalties = hp, AwayPenalties = ap };
        Assert.Equal(new Guid?[] { away, home, away, null, null },
            new[] { F(0, 1), F(2, 1), F(1, 1, 2, 4), F(1, 1), F(null, null) }.Select(f => f.WinnerClubId()));
    }

    /// <summary>Ghi kết quả kèm penalty: chỉ chung kết hoà sau 120', penalty không hoà; sai → PenaltiesInvalid, trận chưa ghi.</summary>
    [DbFact]
    public async Task Record_result_accepts_penalties_only_for_drawn_final()
    {
        await using var t = await TestDb.CreateAsync();
        var (_, md1, _) = await MatchOrchestratorTests.SetupAsync(t, 4, human: false);
        var final = await MarkFinalAsync(t, md1);
        await using (var db = t.Context())
        {
            var svc = new CompetitionService(db, t.Store, t.Clock);
            var league = await db.Fixtures.Where(f => f.MatchdayId == md1 && f.Id != final).Select(f => f.Id).SingleAsync();
            async Task<CompetitionError?> Record(Guid id, int h, int a, (int, int) pens) =>
                (await svc.RecordResultAsync(id, h, a, [], ResultSource.Admin, penalties: pens)).Error;
            Assert.Equal(CompetitionError.PenaltiesInvalid, await Record(league, 1, 1, (3, 2)));   // không phải chung kết
            Assert.Equal(CompetitionError.PenaltiesInvalid, await Record(final, 2, 1, (3, 2)));    // có đội thắng sau 120'
            Assert.Equal(CompetitionError.PenaltiesInvalid, await Record(final, 1, 1, (3, 3)));    // loạt penalty không hoà
            Assert.Null(await Record(final, 1, 1, (3, 2)));
        }

        await using var check = t.Context();
        var fs = await check.Fixtures.Where(f => f.MatchdayId == md1).ToListAsync();
        Assert.Equal(FixtureStatus.Scheduled, fs.Single(f => f.Id != final).Status);
        var fin = fs.Single(f => f.Id == final);
        Assert.Equal((FixtureStatus.Played, 1, 1, 3, 2), (fin.Status, fin.HomeGoals, fin.AwayGoals, fin.HomePenalties, fin.AwayPenalties));
    }

    /// <summary>4 CLB, 1 trận là chung kết: chỉ trận đó bật hiệp phụ; hoà 1–1 (bàn hiệp phụ 2 phút 115) + penalty 3–2 → lưu đủ, đội nhà thắng.
    /// Trận thường hoà mà runner báo penalty → bỏ qua penalty, vẫn ghi kết quả.</summary>
    [DbFact]
    public async Task Final_runs_extra_time_and_stores_penalties()
    {
        await using var t = await TestDb.CreateAsync();
        var (_, md1, _) = await MatchOrchestratorTests.SetupAsync(t, 4, human: false);
        var final = await MarkFinalAsync(t, md1);
        var runner = new Runner(r => new MatchRunOutcome(true, 1, 1,
            [new(MatchSide.Home, 1, 10, 300), r.ExtraTime ? new(MatchSide.Away, 4, 115, 3000) : new(MatchSide.Away, 2, 70, 1500)], Penalties: (3, 2)));
        var report = await new MatchOrchestrator(t.Context, t.Store, t.Clock, runner).PlayBotFixturesAsync(md1);
        Assert.All(report.Fixtures, f => Assert.True(f.Played, f.LastError));
        Assert.Equal([final], runner.Calls.Where(r => r.ExtraTime).Select(r => r.FixtureId));

        await using var db = t.Context();
        var fs = await db.Fixtures.Where(f => f.MatchdayId == md1).ToListAsync();
        var fin = fs.Single(f => f.Id == final);
        Assert.Equal((1, 1, 3, 2, fin.HomeClubId), (fin.HomeGoals, fin.AwayGoals, fin.HomePenalties, fin.AwayPenalties, fin.WinnerClubId()));
        Assert.Equal(115, (await db.Goals.SingleAsync(g => g.FixtureId == final && g.Half == 4)).Minute);
        var league = fs.Single(f => f.Id != final);
        Assert.Equal((FixtureStatus.Played, 1, 1), (league.Status, league.HomeGoals, league.AwayGoals));
        Assert.Null(league.HomePenalties);
    }

    /// <summary>Chung kết hoà, loạt penalty đọc sai (hoà 3–3) → vẫn ghi tỷ số 120', chờ admin; admin nhập 4–5 (có nhật ký),
    /// không nhập lần 2, không nhập cho trận không phải chung kết.</summary>
    [DbFact]
    public async Task Unreadable_penalties_wait_for_admin()
    {
        await using var t = await TestDb.CreateAsync();
        var (_, md1, _) = await MatchOrchestratorTests.SetupAsync(t, 4, human: false);
        var final = await MarkFinalAsync(t, md1);
        var runner = new Runner(r => new MatchRunOutcome(true, 0, 0, [], Penalties: r.ExtraTime ? (3, 3) : null));
        Assert.All((await new MatchOrchestrator(t.Context, t.Store, t.Clock, runner).PlayBotFixturesAsync(md1)).Fixtures, f => Assert.True(f.Played, f.LastError));

        await using (var db = t.Context())
        {
            var f = await db.Fixtures.SingleAsync(x => x.Id == final);
            Assert.Equal(FixtureStatus.Played, f.Status);
            Assert.Null(f.HomePenalties);
            Assert.Null(f.WinnerClubId());
            var league = await db.Fixtures.Where(x => x.MatchdayId == md1 && x.Id != final).Select(x => x.Id).SingleAsync();
            var admin = new CompetitionAdminService(db, t.Clock);
            await Assert.ThrowsAsync<AdminRuleException>(() => admin.SetFinalPenaltiesAsync(league, 4, 5, "test"));
            await Assert.ThrowsAsync<AdminRuleException>(() => admin.SetFinalPenaltiesAsync(final, 2, 2, "test"));
            await admin.SetFinalPenaltiesAsync(final, 4, 5, "test");
            await Assert.ThrowsAsync<AdminRuleException>(() => admin.SetFinalPenaltiesAsync(final, 5, 4, "test"));   // đã có → không sửa
        }

        await using var check = t.Context();
        var fin = await check.Fixtures.SingleAsync(x => x.Id == final);
        Assert.Equal((4, 5, fin.AwayClubId), (fin.HomePenalties, fin.AwayPenalties, fin.WinnerClubId()));
        var audit = await check.AdminAudit.SingleAsync(a => a.Action == "SetPenalties");
        Assert.Equal((nameof(Fixture), final, "test"), (audit.EntityType, audit.EntityId, audit.Actor));
    }
}
