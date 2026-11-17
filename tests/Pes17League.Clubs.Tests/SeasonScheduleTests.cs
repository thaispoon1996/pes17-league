using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Application;

namespace Pes17League.Clubs.Tests;

/// <summary>Bước 6 (PO chọn 06/11): lịch mùa nhiều giải theo khung ngày → bắt đầu mùa → tự sinh vòng loại trực tiếp tới chung kết, với patch thật.</summary>
public class SeasonScheduleTests
{
    private static string Flatten(SeasonSchedule s) => string.Join("\n", s.Slots.Select(x =>
        $"{x.Number} {x.Slot}: {string.Join(" ", x.Matches.Select(m => $"{m.EditionId:N}/{m.Stage}/{m.Home:N}-{m.Away:N}").Order(StringComparer.Ordinal))}"));

    [DbFact]
    public async Task Federation_season_runs_from_schedule_to_cup_finals()
    {
        await using var t = await TestDb.CreateAsync();
        await using var db = t.Context();
        var seasonId = (await new FederationSetupService(db, t.Store, t.Clock).SetupAsync("PES6VN 25-26", Env.OrigOptionFile, "Mùa 1", "test")).SeasonId;
        var admin = new CompetitionAdminService(db, t.Clock);
        await admin.SeedDefaultsAsync("test");
        await new DivisionService(db, t.Clock).ApplyAsync(seasonId, "test");
        var schedule = new SeasonScheduleService(db, t.Clock);
        var svc = new CompetitionService(db, t.Store, t.Clock, rng: new Random(1));

        // Giải lẻ số CLB → báo lỗi kèm tên giải; trả CLB lại thì xếp được.
        var ger2 = (await schedule.PreviewAsync(seasonId, 1)).Editions.Single(e => e.Competition is { Country: "GER", Tier: 2 });
        var removed = ger2.Clubs[^1];
        await admin.RemoveEditionClubAsync(ger2.EditionId, removed.ClubId, "test");
        Assert.Contains(ger2.Competition.Name, (await Assert.ThrowsAsync<AdminRuleException>(() => schedule.PreviewAsync(seasonId, 1))).Message);
        await admin.SetEditionClubAsync(ger2.EditionId, removed.ClubId, removed.Seed, "test");

        // Xem trước: cùng mã → cùng lịch, khác mã → lịch khác; chưa ghi gì nên chưa bắt đầu được.
        var preview = await schedule.PreviewAsync(seasonId, 42);
        Assert.Equal(Flatten(preview), Flatten(await schedule.PreviewAsync(seasonId, 42)));
        Assert.NotEqual(Flatten(preview), Flatten(await schedule.PreviewAsync(seasonId, 43)));
        Assert.Null(await schedule.ViewAsync(seasonId));
        Assert.Equal(CompetitionError.SeasonNotScheduled, (await svc.StartScheduledSeasonAsync(seasonId, "test")).Error);

        var written = await schedule.ApplyAsync(seasonId, 42, "test");
        Assert.Equal(Flatten(preview), Flatten(written));
        // Quốc nội 686 + LP 192 + vòng mở 4 cúp quốc gia (4+4+4+2) = 892; khung thêm 5 vòng KO quốc gia.
        var openCups = written.Editions.Where(e => e.Competition.Type == CompetitionType.DomesticCup)
            .Sum(e => written.Slots.Sum(s => s.Matches.Count(m => m.EditionId == e.EditionId)));
        Assert.Equal(14, openCups);
        Assert.Equal((42, 686 + 192 + 14), (written.Seed, written.Slots.Sum(s => s.Matches.Count)));
        Assert.True(written.Slots.Count > 33);
        Assert.All(written.Slots, s => Assert.Equal(2 * s.Matches.Count, s.Matches.SelectMany(m => new[] { m.Home, m.Away }).Distinct().Count()));   // ≤ 1 trận/CLB/khung
        Assert.All(written.Slots, s => Assert.All(s.Matches, m => Assert.Equal(s.Slot.Stage ?? FixtureStage.League, m.Stage)));
        // KO châu lục + vòng sau cúp quốc gia: chờ bốc; vòng mở (RoundOf32) đã có trận.
        Assert.All(written.Slots.Where(s => s.Slot.Stage is FixtureStage.KnockoutPlayoff or FixtureStage.RoundOf16
            or FixtureStage.QuarterFinal or FixtureStage.SemiFinal or FixtureStage.Final), s => Assert.Empty(s.Matches));
        foreach (var e in written.Editions)
        {
            var n = e.Clubs.Count;
            var count = written.Slots.Sum(s => s.Matches.Count(m => m.EditionId == e.EditionId));
            Assert.Equal(e.Competition.Type switch
            {
                CompetitionType.DomesticLeague => n * (n - 1),
                CompetitionType.ContinentalCup => n * 4,
                CompetitionType.DomesticCup => n - CupDraw.BracketSize(n) / 2,   // vòng mở = số đội − số vào vòng sau
                _ => -1,
            }, count);
        }
        Assert.All(await db.Editions.Where(e => e.SeasonId == seasonId).ToListAsync(), e => Assert.Equal((EditionStatus.Scheduled, true), (e.Status, e.RulesSnapshot is not null)));
        await Assert.ThrowsAsync<AdminRuleException>(() => schedule.ApplyAsync(seasonId, 42, "test"));
        await Assert.ThrowsAsync<AdminRuleException>(() => admin.RemoveEditionClubAsync(ger2.EditionId, removed.ClubId, "test"));      // CLB đã khoá

        // Sơ đồ nhánh trước khi đá: suất theo hạng, chưa có CLB; tứ kết hạng 1, 4, 2, 3 gặp đội thắng play-off cặp 4, 1, 3, 2.
        var cupId = written.Editions.First(e => e.Competition.Type == CompetitionType.ContinentalCup).EditionId;
        var bracket = (await svc.GetBracketAsync(cupId))!;
        Assert.Equal([FixtureStage.KnockoutPlayoff, FixtureStage.QuarterFinal, FixtureStage.SemiFinal, FixtureStage.Final], bracket.Select(r => r.Stage));
        Assert.Equal([(5, 12), (6, 11), (7, 10), (8, 9)], bracket[0].Ties.Select(t => (t.A.Rank!.Value, t.B.Rank!.Value)));
        Assert.Equal([(1, 4), (4, 1), (2, 3), (3, 2)], bracket[1].Ties.Select(t => (t.A.Rank!.Value, t.B.From!.Value)));
        Assert.Equal([(1, 2), (3, 4)], bracket[2].Ties.Select(t => (t.A.From!.Value, t.B.From!.Value)));
        Assert.All(bracket.SelectMany(r => r.Ties), t => Assert.True(t.A.Club is null && t.B.Club is null && t.Legs.Count == 0));
        Assert.Null(await svc.GetBracketAsync(written.Editions[0].EditionId));     // giải quốc nội

        var other = new Season { Name = "Mùa khác", Number = 99, Status = SeasonStatus.Active, CreatedAt = t.Clock.GetUtcNow() };
        db.Seasons.Add(other);
        await db.SaveChangesAsync();
        Assert.Equal(CompetitionError.AnotherSeasonActive, (await svc.StartScheduledSeasonAsync(seasonId, "test")).Error);
        db.Seasons.Remove(other);
        await db.SaveChangesAsync();

        var start = await svc.StartScheduledSeasonAsync(seasonId, "test");
        Assert.True(start.Ok, start.Error.ToString());
        var matchdays = written.Slots.Count;
        Assert.Equal(matchdays, start.Matchdays);
        Assert.NotEmpty(start.Budgets!);
        Assert.All(await db.Editions.Where(e => e.SeasonId == seasonId).ToListAsync(), e => Assert.Equal(EditionStatus.Active, e.Status));
        Assert.Equal(await db.Clubs.CountAsync(c => c.Status == ClubStatus.Active), await db.SeasonClubs.CountAsync(x => x.SeasonId == seasonId));
        await Assert.ThrowsAsync<AdminRuleException>(() => schedule.PreviewAsync(seasonId, 42));

        // Đá hết mùa bằng kết quả admin; bỏ qua sinh OF (test khác đã kiểm). Chung kết hoà: cúp đầu chưa có penalty, cúp sau có.
        // Knockout 1 lượt không được hoà (trừ chung kết có penalty).
        var goals = new Random(5);
        var finals = 0;
        var projected = false;
        for (var number = 1; number <= matchdays; number++)
        {
            db.ChangeTracker.Clear();
            var md = await db.Matchdays.SingleAsync(m => m.SeasonId == seasonId && m.Number == number);
            Assert.Equal(MatchdayStatus.WindowOpen, md.Status);
            Assert.Equal(number <= 5 || number is 18 or 19, md.TransferWindowId is not null);   // kỳ CN chỉ ở khung 1–5, 18–19 (PO 06/11)
            if (md.TransferWindowId is not null) (await db.TransferWindows.SingleAsync(w => w.Id == md.TransferWindowId)).Status = TransferWindowStatus.Closed;
            md.Status = MatchdayStatus.Locked;
            await db.SaveChangesAsync();
            var fixtures = await db.Fixtures.Where(f => f.MatchdayId == md.Id).OrderBy(f => f.Id).ToListAsync();
            Assert.NotEmpty(fixtures);
            foreach (var f in fixtures)
            {
                RecordResult r;
                if (f.Stage == FixtureStage.Final)
                    r = await svc.RecordResultAsync(f.Id, 1, 1, [], ResultSource.Admin, penalties: finals++ == 0 ? null : (5, 4));
                else
                {
                    var (hg, ag) = (goals.Next(4), goals.Next(4));
                    if (f.Leg is null && f.Stage > FixtureStage.LeaguePhase && hg == ag) ag++;
                    r = await svc.RecordResultAsync(f.Id, hg, ag, [], ResultSource.Admin);
                }
                Assert.True(r.Ok, r.Error.ToString());
                Assert.Equal(number == matchdays && f == fixtures[^1], r.SeasonFinished);
            }
            if (md.Stage == FixtureStage.LeaguePhase && !projected)
            {
                // League phase đang đá: play-off dự kiến theo BXH hiện tại (hạng 5–8 gặp 12–9), chưa khớp trận nào.
                var now = (await svc.GetEditionStandingsAsync(cupId)).Select(r => r.Club).ToArray();
                var po = (await svc.GetBracketAsync(cupId))![0].Ties;
                Assert.Equal(now[4..8], po.Select(t => t.A.Club!.Value));
                Assert.Equal(now[8..12].Reverse(), po.Select(t => t.B.Club!.Value));
                Assert.All(po, t => Assert.True(t.A.Projected && t.B.Projected && t.Legs.Count == 0 && t.Winner is null));
                projected = true;
            }
        }
        Assert.True(projected);
        db.ChangeTracker.Clear();
        Assert.Equal(6, finals);   // 2 châu lục + 4 quốc gia
        Assert.Equal(SeasonStatus.Finished, (await db.Seasons.SingleAsync(s => s.Id == seasonId)).Status);
        var pending = Assert.Single(await db.Editions.Where(e => e.SeasonId == seasonId && e.Status != EditionStatus.Finished).ToListAsync());
        await admin.SetFinalPenaltiesAsync((await db.Fixtures.SingleAsync(f => f.CompetitionEditionId == pending.Id && f.Stage == FixtureStage.Final)).Id, 3, 4, "test");
        Assert.All(await db.Editions.AsNoTracking().Where(e => e.SeasonId == seasonId).ToListAsync(), e => Assert.Equal(EditionStatus.Finished, e.Status));

        var all = await (from f in db.Fixtures join m in db.Matchdays on f.MatchdayId equals m.Id where m.SeasonId == seasonId select new { f, m.Stage, m.Leg }).ToListAsync();
        // Quốc nội + LP + KO châu lục + cúp quốc gia (mỗi nước n−1 trận).
        var domesticCupMatches = written.Editions.Where(e => e.Competition.Type == CompetitionType.DomesticCup).Sum(e => e.Clubs.Count - 1);
        Assert.Equal(686 + 192 + 2 * 21 + domesticCupMatches, all.Count);
        Assert.All(all.GroupBy(x => x.f.MatchdayId), g => Assert.Equal(2 * g.Count(), g.SelectMany(x => new[] { x.f.HomeClubId, x.f.AwayClubId }).Distinct().Count()));
        Assert.All(all.Where(x => x.f.Stage > FixtureStage.LeaguePhase), x => Assert.Equal((x.f.Stage, x.f.Leg), (x.Stage!.Value, x.Leg)));    // đúng khung của giai đoạn/lượt
        foreach (var cup in written.Editions.Where(e => e.Competition.Type == CompetitionType.ContinentalCup))
        {
            var ko = all.Select(x => x.f).Where(f => f.CompetitionEditionId == cup.EditionId && f.Stage > FixtureStage.LeaguePhase).ToList();
            Assert.Equal([8, 8, 4, 1], new[] { FixtureStage.KnockoutPlayoff, FixtureStage.QuarterFinal, FixtureStage.SemiFinal, FixtureStage.Final }.Select(s => ko.Count(f => f.Stage == s)));
            Assert.Equal(21, ko.Count);
            // Cặp 2 lượt cùng TieId, lượt 2 đổi sân. Nhánh theo BXH league phase: hạng 1–4 vào thẳng tứ kết (lượt 2 sân nhà), hạng 5–8 gặp 9–12 ở play-off.
            var ties = ko.Where(f => f.Stage != FixtureStage.Final).GroupBy(f => f.TieId!.Value)
                .Select(g => (Leg1: g.Single(f => f.Leg == 1), Leg2: g.Single(f => f.Leg == 2))).ToList();
            Assert.All(ties, x => Assert.Equal((x.Leg1.Stage, x.Leg1.HomeClubId, x.Leg1.AwayClubId), (x.Leg2.Stage, x.Leg2.AwayClubId, x.Leg2.HomeClubId)));
            var table = (await svc.GetEditionStandingsAsync(cup.EditionId)).Select(r => r.Club).ToArray();
            Assert.Equal(table[..4].Order(), ties.Where(x => x.Leg2.Stage == FixtureStage.QuarterFinal).Select(x => x.Leg2.HomeClubId).Order());
            Assert.Equal(table[4..8].Order(), ties.Where(x => x.Leg2.Stage == FixtureStage.KnockoutPlayoff).Select(x => x.Leg2.HomeClubId).Order());
            Assert.Equal(table[8..12].Order(), ties.Where(x => x.Leg2.Stage == FixtureStage.KnockoutPlayoff).Select(x => x.Leg1.HomeClubId).Order());
            // Vòng sau = đội thắng vòng trước (tổng tỷ số → bàn sân khách → xếp trên), tứ kết thêm 4 đội vào thẳng.
            foreach (var (stage, next) in new[] { (FixtureStage.KnockoutPlayoff, FixtureStage.QuarterFinal), (FixtureStage.QuarterFinal, FixtureStage.SemiFinal), (FixtureStage.SemiFinal, FixtureStage.Final) })
            {
                var winners = ties.Where(x => x.Leg1.Stage == stage).Select(x => CupDraw.TieWinner(new KnockoutTie<Guid>(stage, x.Leg2.HomeClubId, x.Leg1.HomeClubId),
                    (x.Leg1.HomeGoals!.Value, x.Leg1.AwayGoals!.Value), (x.Leg2.HomeGoals!.Value, x.Leg2.AwayGoals!.Value))).ToList();
                Assert.Equal((stage == FixtureStage.KnockoutPlayoff ? winners.Concat(table[..4]) : winners).Order(),
                    ko.Where(f => f.Stage == next && f.Leg != 2).SelectMany(f => new[] { f.HomeClubId, f.AwayClubId }).Order());
            }
            // Sơ đồ nhánh khớp đủ mọi trận loại trực tiếp, hết dự kiến, mọi bên mang hạng league phase; tứ kết hạng 1, 4, 2, 3; vô địch = đội thắng chung kết.
            var rounds = (await svc.GetBracketAsync(cup.EditionId))!;
            Assert.Equal(ko.Select(f => f.Id).Order(), rounds.SelectMany(r => r.Ties).SelectMany(t => t.Legs).Select(f => f.Id).Order());
            Assert.All(rounds.SelectMany(r => r.Ties), t => Assert.True(t.Winner is not null && !t.A.Projected && !t.B.Projected));
            Assert.All(rounds.SelectMany(r => r.Ties).SelectMany(t => new[] { t.A, t.B }), s => Assert.Equal(Array.IndexOf(table, s.Club!.Value) + 1, s.Rank));
            Assert.Equal(CupDraw.BracketOrder(4).Select(i => table[i - 1]), rounds[1].Ties.Select(t => t.A.Club!.Value));
            Assert.Equal(ko.Single(f => f.Stage == FixtureStage.Final).WinnerClubId(), rounds[^1].Ties.Single().Winner);
        }
        foreach (var cup in written.Editions.Where(e => e.Competition.Type == CompetitionType.DomesticCup))
        {
            var ko = all.Select(x => x.f).Where(f => f.CompetitionEditionId == cup.EditionId).ToList();
            Assert.Equal(cup.Clubs.Count - 1, ko.Count);
            Assert.All(ko, f => Assert.Null(f.Leg));
            Assert.NotNull(ko.Single(f => f.Stage == FixtureStage.Final).WinnerClubId());
            var tree = (await svc.GetBracketAsync(cup.EditionId))!;
            Assert.Equal(ko.Count, tree.SelectMany(r => r.Ties).Count(t => t.Legs.Count > 0));
            Assert.Equal(ko.Single(f => f.Stage == FixtureStage.Final).WinnerClubId(), tree[^1].Ties.Single().Winner);
        }

        // BXH theo mùa giải không lẫn trận cúp; lương/hợp đồng chốt mọi khung ngày.
        foreach (var e in written.Editions.Where(e => e.Competition.Type != CompetitionType.DomesticCup))
        {
            var n = e.Clubs.Count;
            Assert.All(await svc.GetEditionStandingsAsync(e.EditionId), r => Assert.Equal(e.Competition.Type == CompetitionType.DomesticLeague ? 2 * (n - 1) : 8, r.Played));
        }
        Assert.Equal(2 * 686, (await svc.GetStandingsAsync(seasonId)).Sum(r => r.Played));
        Assert.Equal(matchdays, await db.RoundSettlements.CountAsync(x => x.SeasonId == seasonId));
        // Châu lục: 4 vòng × 2 cúp; quốc gia: 4 vòng sau mở × 4 cúp.
        Assert.Equal(8 + 16, await db.AdminAudit.CountAsync(a => a.Action == SeasonScheduleService.DrawAction && a.Actor == SeasonScheduleService.SystemActor));
        var view = (await schedule.ViewAsync(seasonId))!;
        Assert.Equal((42, 686 + 192 + 2 * 21 + domesticCupMatches), (view.Seed, view.Slots.Sum(s => s.Matches.Count)));
    }
}
