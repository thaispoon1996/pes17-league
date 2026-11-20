using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Infrastructure;

namespace Pes17League.Clubs.Application;

/// <summary>
/// Chốt vòng cho HLV career (gọi trong <see cref="CompetitionService"/> sau BLĐ, trước khi lưu). Không có hồ sơ HLV (giải online) → không làm gì.
/// Mỗi vòng: kết quả trận → danh tiếng/kinh nghiệm/căng thẳng; lương vào ví; chi tiêu, tài sản; khoá học; đầu tư; hồi âm yêu cầu; thư quá hạn;
/// sự kiện ngẫu nhiên; cảnh cáo / sa thải; mời việc khi thất nghiệp; gia hạn; cuối mùa: tổng kết, thưởng, hết hợp đồng.
/// </summary>
public sealed class CoachRoundService(ClubsDbContext db, TimeProvider clock, IOptionFileStore? store = null)
{
    public async Task ApplyRoundAsync(Matchday md, bool seasonEnded, CancellationToken ct = default)
    {
        var coaches = await db.Coaches.Where(c => c.Status != CoachStatus.Creating).ToListAsync(ct);
        if (coaches.Count == 0) return;
        var season = await db.Seasons.SingleAsync(s => s.Id == md.SeasonId, ct);
        var tick = CoachRules.Tick(season.Number, md.Number);
        var rounds = await db.Matchdays.CountAsync(m => m.SeasonId == md.SeasonId, ct);
        foreach (var c in coaches.Where(c => c.Tick < tick))
            await ApplyCoachAsync(c, md, season, rounds, tick, seasonEnded, ct);
        await db.SaveChangesAsync(ct);
    }

    async Task ApplyCoachAsync(Coach c, Matchday md, Season season, int rounds, int tick, bool seasonEnded, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var letters = new CoachLetterService(db, clock);
        var effects = new CoachEffects(db, clock);
        var k = await db.CoachContracts.SingleOrDefaultAsync(x => x.CoachId == c.Id && x.Status == CoachContractStatus.Active, ct);
        var club = k is null ? null : await db.Clubs.AsNoTracking().Where(x => x.Id == k.ClubId).Select(x => x.Name).SingleOrDefaultAsync(ct);

        // 1. Kết quả trận vòng này.
        if (k is not null)
        {
            var f = await db.Fixtures.AsNoTracking().FirstOrDefaultAsync(x => x.MatchdayId == md.Id && (x.HomeClubId == k.ClubId || x.AwayClubId == k.ClubId)
                && x.HomeGoals != null && x.AwayGoals != null, ct);
            if (f is not null)
            {
                var diff = (f.HomeGoals!.Value - f.AwayGoals!.Value) * (f.HomeClubId == k.ClubId ? 1 : -1);
                var opp = f.HomeClubId == k.ClubId ? f.AwayClubId : f.HomeClubId;
                var values = await (from m in db.SquadMemberships join p in db.PlayerPrices on m.PlayerId equals p.PlayerId
                                    where m.LeftAt == null && p.SeasonId == md.SeasonId && (m.ClubId == k.ClubId || m.ClubId == opp)
                                    group p.Price by m.ClubId into g select new { g.Key, V = g.Sum() }).ToDictionaryAsync(x => x.Key, x => x.V, ct);
                c.Reputation = Math.Clamp(c.Reputation + CoachRules.MatchRep(diff, values.GetValueOrDefault(k.ClubId), values.GetValueOrDefault(opp)), 0, 100);
                c.Xp += CoachRules.MatchXp(diff);
                c.Stress += CoachRules.MatchStress(diff, c.Trait);
            }
            // 2. Lương vào ví (tiền tự sinh, CLB không bị trừ).
            CoachService.Money(db, c, k.Wage, CoachMoney.Wage, $"Lương {club} vòng {md.Number}", tick, now);
            k.RoundsPaid++;
        }

        // 3. Chi tiêu cá nhân, bảo trì tài sản, giảm căng thẳng.
        var assets = await db.CoachAssets.Where(a => a.CoachId == c.Id).ToListAsync(ct);
        CoachService.Money(db, c, -CoachRules.LivingCost(c.Lifestyle, c.Trait), CoachMoney.Living, $"Chi tiêu ({CoachRules.Living[c.Lifestyle].Name.ToLowerInvariant()})", tick, now);
        var relief = CoachRules.Living[c.Lifestyle].StressRelief;
        foreach (var a in assets.Where(a => a.MaturesTick is null))
            if (CoachRules.Assets.FirstOrDefault(x => x.Key == a.Key) is { } def)
            {
                CoachService.Money(db, c, -def.Upkeep, CoachMoney.Upkeep, $"Bảo trì {def.Name.ToLowerInvariant()}", tick, now);
                relief += def.StressRelief;
            }
        c.Stress -= relief;

        // 4. Đầu tư đáo hạn.
        foreach (var a in assets.Where(a => a.MaturesTick is { } m && m <= tick))
        {
            var back = a.Price + a.Price * a.ReturnPermille / 1000;
            CoachService.Money(db, c, back, CoachMoney.Investment, "Đầu tư đáo hạn", tick, now);
            CoachLetterService.Write(db, c, LetterKind.Info, LetterFrom.Agent, "Bạn cũ thời đi học", "Khoản đầu tư đã đáo hạn",
                $"Khoản góp {MoneyText.Format(a.Price)} đã tất toán. Ông nhận về {MoneyText.Format(back)} ({(a.ReturnPermille >= 0 ? "lãi" : "lỗ")} {Math.Abs(a.ReturnPermille) / 10.0:0.#}%).", tick, now);
            db.CoachAssets.Remove(a);
        }

        // 5. Khoá học.
        if (c.CourseLicense > 0 && --c.CourseRoundsLeft <= 0)
        {
            c.License = c.CourseLicense;
            c.CourseLicense = 0;
            c.CourseRoundsLeft = 0;
            c.FreePoints++;
            CoachLetterService.Write(db, c, LetterKind.Official, LetterFrom.Federation, "Liên đoàn bóng đá", $"Cấp bằng huấn luyện {CoachRules.Licenses[c.License]}",
                $"Chúc mừng ông đã hoàn thành khoá học và được cấp bằng {CoachRules.Licenses[c.License]}. Ông nhận thêm 1 điểm năng lực.", tick, now, link: "hlv");
        }

        // 6. Kinh nghiệm → điểm năng lực.
        while (c.Xp >= CoachRules.XpPerPoint)
        {
            c.Xp -= CoachRules.XpPerPoint;
            c.FreePoints++;
        }

        // 7. Hồi âm yêu cầu gửi từ vòng trước.
        foreach (var req in await db.CoachLetters.Where(l => l.CoachId == c.Id && l.Outgoing && l.AnsweredTick == null && l.Tick < tick).OrderBy(l => l.Tick).ToListAsync(ct))
            await letters.AnswerAsync(c, req, md.SeasonId, tick, ct);
        k = await db.CoachContracts.SingleOrDefaultAsync(x => x.CoachId == c.Id && x.Status == CoachContractStatus.Active, ct);   // có thể vừa từ chức

        // 8. Thư quyết định quá hạn → phương án mặc định.
        foreach (var l in await db.CoachLetters.Where(l => l.CoachId == c.Id && l.Kind == LetterKind.Decision && l.Chosen == null && l.DeadlineTick != null
                     && l.DeadlineTick <= tick).ToListAsync(ct))
        {
            var r = await letters.ResolveAsync(c, l, l.DefaultOption, byDefault: true, ct);
            l.Body += $"\n\n— {r}";
        }

        // 9. Sự kiện ngẫu nhiên.
        await RandomEventAsync(c, k, club, tick, now, ct);

        // 10. BLĐ: cảnh cáo / sa thải (chỉ career có hồ sơ HLV).
        if (k is not null && !seasonEnded && await db.BoardConfidences.AsNoTracking().SingleOrDefaultAsync(b => b.ClubId == k.ClubId && b.SeasonId == md.SeasonId, ct) is { } board)
        {
            if (board.Confidence < CoachRules.SackBelow && md.Number > CoachRules.SackAfterRound)
            {
                var pay = await new CoachJobService(db, clock, store).SackAsync(c, k, tick, ct);
                CoachLetterService.Write(db, c, LetterKind.Official, LetterFrom.Board, $"{CoachRules.Chairman(k.ClubId)}, chủ tịch {club}", "Quyết định chấm dứt hợp đồng",
                    $"Ban lãnh đạo {club} quyết định chấm dứt hợp đồng với ông do kết quả không đạt yêu cầu (độ tin cậy {board.Confidence}/100). " +
                    $"Khoản bồi thường {MoneyText.Format(pay)} ({k.SeverancePct}% lương còn lại) đã chuyển vào ví HLV. Ông có thể tìm việc ở CLB khác.", tick, now, k.ClubId, key: "sa-thai", link: "hlv/viec-lam");
                k = null;
            }
            else if (board.Confidence < CoachRules.WarnBelow && c.WarnedTick / 100 != tick / 100)
            {
                c.WarnedTick = tick;
                CoachLetterService.Write(db, c, LetterKind.Official, LetterFrom.Board, $"{CoachRules.Chairman(k.ClubId)}, chủ tịch {club}", "Thư cảnh cáo",
                    $"Ban lãnh đạo không hài lòng với kết quả hiện tại (độ tin cậy {board.Confidence}/100). Nếu tình hình không cải thiện, CLB sẽ cân nhắc thay HLV.",
                    tick, now, k.ClubId, key: "canh-cao");
            }
        }

        // 11. Gia hạn: 5 vòng trước khi hết hợp đồng.
        if (k is not null && k.EndSeasonNumber == season.Number && md.Number == rounds - 5 && !k.RenewAgreed)
        {
            var conf = (await db.BoardConfidences.AsNoTracking().SingleOrDefaultAsync(b => b.ClubId == k.ClubId && b.SeasonId == md.SeasonId, ct))?.Confidence ?? BoardRules.StartConfidence;
            if (conf >= CoachRules.RenewFrom)
            {
                var l = CoachLetterService.Write(db, c, LetterKind.Decision, LetterFrom.Board, $"{CoachRules.Chairman(k.ClubId)}, chủ tịch {club}", "Đề nghị gia hạn hợp đồng",
                    $"Hợp đồng của ông hết hạn cuối mùa này. Ban lãnh đạo muốn gia hạn thêm 1 mùa với mức lương {MoneyText.Format(k.Wage * 105 / 100 / 100 * 100)}/vòng.", tick, now, k.ClubId, key: "gia-han");
                l.Options = "Đồng ý gia hạn\nTừ chối, rời CLB cuối mùa";
                l.DefaultOption = 0;
                l.DeadlineTick = CoachRules.Tick(season.Number, rounds);
            }
            else
                CoachLetterService.Write(db, c, LetterKind.Official, LetterFrom.Board, $"{CoachRules.Chairman(k.ClubId)}, chủ tịch {club}", "Không gia hạn hợp đồng",
                    $"Ban lãnh đạo thông báo sẽ không gia hạn hợp đồng khi mùa giải kết thúc (độ tin cậy {conf}/100).", tick, now, k.ClubId, key: "khong-gia-han");
        }

        // 12. Thất nghiệp: lời mời làm việc (tối đa 2 / vòng).
        if (k is null && c.Status == CoachStatus.Unemployed && !seasonEnded) await OffersAsync(c, md.SeasonId, tick, now, ct);

        // 13. Cuối mùa.
        if (seasonEnded && k is not null) await SeasonEndAsync(c, k, season, club, tick, now, ct);

        c.Stress = Math.Clamp(c.Stress, 0, CoachRules.StressMax);
        c.Tick = tick;
        c.UpdatedAt = now;
    }

    async Task RandomEventAsync(Coach c, CoachContract? k, string? club, int tick, DateTimeOffset now, CancellationToken ct)
    {
        var pending = await db.CoachLetters.Where(l => l.CoachId == c.Id && l.Kind == LetterKind.Decision && l.Chosen == null).Select(l => l.Key).ToListAsync(ct);
        if (pending.Count >= CoachEvents.MaxPending) return;
        var forced = c.Stress >= CoachRules.BurnoutAt && !pending.Contains("kiet-suc");
        if (!forced && CoachRules.Roll(c.Id, tick) >= CoachEvents.ChancePermille) return;

        var seasonId = await CoachEffects.ActiveSeasonAsync(db, ct);
        var conf = k is null ? (int?)null : (await db.BoardConfidences.AsNoTracking().SingleOrDefaultAsync(b => b.ClubId == k.ClubId && b.SeasonId == seasonId, ct))?.Confidence;
        (Guid Id, string Name, int Morale)? unhappy = null;
        if (k is not null)
        {
            var low = await (from m in db.SquadMemberships join p in db.Players on m.PlayerId equals p.Id
                             join cd in db.PlayerConditions on m.PlayerId equals cd.PlayerId
                             where m.ClubId == k.ClubId && m.LeftAt == null && cd.Morale < 40
                             orderby cd.Morale select new { p.Id, p.Name, cd.Morale }).FirstOrDefaultAsync(ct);
            if (low is not null) unhappy = (low.Id, low.Name, low.Morale);
        }
        var recentWin = k is not null && await (from f in db.Fixtures join m in db.Matchdays on f.MatchdayId equals m.Id
                                                where m.SeasonId == seasonId && (f.HomeClubId == k.ClubId || f.AwayClubId == k.ClubId) && f.HomeGoals != null
                                                orderby m.Number descending
                                                select f.HomeClubId == k.ClubId ? f.HomeGoals > f.AwayGoals : f.AwayGoals > f.HomeGoals).FirstOrDefaultAsync(ct);
        var seen = (await db.CoachLetters.Where(l => l.CoachId == c.Id && l.Kind == LetterKind.Decision && l.Tick / 100 == tick / 100)
            .Select(l => l.Key).ToListAsync(ct)).GroupBy(x => x).ToDictionary(g => g.Key ?? "", g => g.Count());

        CoachEventDef? pick;
        if (forced) pick = CoachEvents.Find("kiet-suc");
        else
        {
            var bag = new List<(CoachEventDef E, int W)>();
            foreach (var e in CoachEvents.All)
            {
                if (pending.Contains(e.Key) || seen.GetValueOrDefault(e.Key) >= 2) continue;
                var w = e.Weight;
                switch (e.Key)
                {
                    case "ghe-nong": if (conf is not { } cf || cf >= 45) continue; w *= 3; break;
                    case "clb-lon-dom-ngo": if (k is null || c.Reputation < 35) continue; break;
                    case "cau-thu-bat-man": if (unhappy is null) continue; w *= 3; if (c.Trait == CoachTrait.IronDiscipline) w /= 2; break;
                    case "qua-cdv": if (k is null) continue; if (recentWin) w *= 3; break;
                    case "binh-luan-tv": if (c.Reputation < 15) continue; break;
                    case "quang-cao": if (c.Reputation < 20) continue; if (c.Trait == CoachTrait.Charismatic) w *= 2; break;
                    case "kiet-suc": if (c.Stress < 50) continue; w *= 3; break;
                    case "scandal": if (c.Trait == CoachTrait.PressDarling) w /= 2; if (c.Trait == CoachTrait.HotHead) w *= 2; break;
                    case "hoi-thao": if (k is null && c.Reputation > 60) w /= 2; break;
                }
                if (e.Key is "ghe-nong" or "cau-thu-bat-man" or "qua-cdv" or "scandal" && k is null) continue;
                if (w > 0) bag.Add((e, w));
            }
            if (bag.Count == 0) return;
            var roll = CoachRules.Roll(c.Id, tick * 31 + 17) * bag.Sum(b => b.W) / 1000;
            pick = bag[^1].E;
            foreach (var (e, w) in bag)
            {
                if (roll < w) { pick = e; break; }
                roll -= w;
            }
        }
        if (pick is null) return;
        var player = pick.Key == "cau-thu-bat-man" ? unhappy : null;
        CoachLetterService.WriteEvent(db, c, pick, tick, now, k?.ClubId, club, player?.Id, player?.Name);
    }

    async Task OffersAsync(Coach c, Guid seasonId, int tick, DateTimeOffset now, CancellationToken ct)
    {
        var roll = CoachRules.Roll(c.Id, tick * 13 + 5);
        var count = roll < 100 ? 2 : roll < 350 ? 1 : 0;
        if (count == 0) return;
        var offered = (await db.CoachLetters.Where(l => l.CoachId == c.Id && l.Key == "moi-viec" && l.Tick / 100 == tick / 100).Select(l => l.ClubId).ToListAsync(ct)).ToHashSet();
        var applied = (await db.CoachApplications.Where(a => a.CoachId == c.Id && a.SeasonId == seasonId).Select(a => a.ClubId).ToListAsync(ct)).ToHashSet();
        var rows = (await new CoachJobService(db, clock, store).ClubsAsync(c, seasonId, null, ct))
            .Where(r => r.Vacant && r.Lock is null && !r.Refused && !offered.Contains(r.ClubId) && !applied.Contains(r.ClubId))
            .OrderByDescending(r => r.Tier).ThenBy(r => CoachRules.Roll(r.ClubId, tick)).Take(count).ToList();
        foreach (var r in rows)
        {
            var l = CoachLetterService.Write(db, c, LetterKind.Decision, LetterFrom.Club, $"{CoachRules.Chairman(r.ClubId)}, chủ tịch {r.Name}", $"Lời mời dẫn dắt {r.Name}",
                $"{r.Name} ({r.League}) đang tìm HLV trưởng. Mục tiêu: {r.Objective.ToLowerInvariant()}. Lương khởi điểm khoảng {MoneyText.Format(r.Wage)}/vòng. " +
                "Nếu quan tâm, mời ông tới thương lượng.", tick, now, r.ClubId, key: "moi-viec", data: $"club={r.ClubId}");
            l.Options = "Mở thương lượng\nTừ chối";
            l.DefaultOption = 1;
            l.DeadlineTick = tick + CoachEvents.DeadlineRounds;
        }
    }

    async Task SeasonEndAsync(Coach c, CoachContract k, Season season, string? club, int tick, DateTimeOffset now, CancellationToken ct)
    {
        var status = await new BoardService(db, clock).StatusAsync(season.Id, k.ClubId, ct);
        var cups = await db.Fixtures.CountAsync(f => f.Stage == FixtureStage.Final && f.Status == FixtureStatus.Played
            && db.Matchdays.Any(m => m.Id == f.MatchdayId && m.SeasonId == season.Id)
            && ((f.HomeClubId == k.ClubId && (f.HomeGoals > f.AwayGoals || (f.HomeGoals == f.AwayGoals && f.HomePenalties > f.AwayPenalties)))
                || (f.AwayClubId == k.ClubId && (f.AwayGoals > f.HomeGoals || (f.HomeGoals == f.AwayGoals && f.AwayPenalties > f.HomePenalties)))), ct);
        var lines = new List<string>();
        if (status?.Position is { } pos)
        {
            var expected = k.ExpectedPosition > 0 ? k.ExpectedPosition : status.ExpectedPosition;
            var rep = CoachRules.SeasonRep(pos == 1, pos, expected, cups);
            c.Reputation = Math.Clamp(c.Reputation + rep, 0, 100);
            lines.Add($"Kết thúc ở hạng {pos}/{status.Teams} {status.League} (mục tiêu hạng {expected}).");
            if (cups > 0) lines.Add($"Giành {cups} cúp.");
            if (pos <= expected)
            {
                CoachService.Money(db, c, k.Bonus, CoachMoney.Bonus, "Thưởng đạt mục tiêu mùa", tick, now);
                c.FreePoints++;
                lines.Add($"Đạt mục tiêu: thưởng {MoneyText.Format(k.Bonus)} vào ví, thêm 1 điểm năng lực.");
            }
            else lines.Add("Không đạt mục tiêu: không có thưởng.");
            lines.Add($"Danh tiếng {(rep >= 0 ? "tăng" : "giảm")} {Math.Abs(rep)}, hiện {c.Reputation} ({CoachRules.RepTier(c.Reputation)}).");
        }
        var chairman = $"{CoachRules.Chairman(k.ClubId)}, chủ tịch {club}";
        CoachLetterService.Write(db, c, LetterKind.Official, LetterFrom.Board, chairman, $"Tổng kết {season.Name}", string.Join("\n", lines), tick, now, k.ClubId, key: "tong-ket");

        if (k.EndSeasonNumber <= season.Number)
        {
            if (k.RenewAgreed)
            {
                var rounds = await db.Matchdays.CountAsync(m => m.SeasonId == season.Id, ct);
                k.Seasons++;
                k.EndSeasonNumber++;
                k.TotalRounds += rounds;
                k.Wage = k.Wage * 105 / 100 / 100 * 100;
                k.Bonus = CoachRules.OfferBonus(k.Wage);
                k.RenewAgreed = false;
                CoachLetterService.Write(db, c, LetterKind.Official, LetterFrom.Board, chairman, "Phụ lục gia hạn hợp đồng",
                    $"Hợp đồng được gia hạn tới hết mùa {k.EndSeasonNumber}, lương mới {MoneyText.Format(k.Wage)}/vòng.", tick, now, k.ClubId, key: "gia-han");
            }
            else
            {
                await new CoachJobService(db, clock, store).LeaveAsync(c, k, CoachContractStatus.Expired, 0, 0, tick, ct);
                CoachLetterService.Write(db, c, LetterKind.Official, LetterFrom.Board, chairman, "Hợp đồng hết hạn",
                    $"Hợp đồng của ông với {club} đã kết thúc. Cảm ơn ông vì những đóng góp. Ông có thể tìm việc ở CLB khác khi mùa mới bắt đầu.",
                    tick, now, k.ClubId, key: "het-han", link: "hlv/viec-lam");
            }
        }
        k.RenewAgreed = false;
    }
}
