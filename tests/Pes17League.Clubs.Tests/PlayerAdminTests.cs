using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Application;
using Pes17League.Clubs.Infrastructure;
using Pes17League.OptionFile;

namespace Pes17League.Clubs.Tests;

/// <summary>
/// Admin cầu thủ — bước 3c phần 1: sửa thông tin/chỉ số, giải nghệ, ghi OF vòng sau; phần 2: tạo cầu thủ ô 32768+, xoá hẳn
/// (docs/domain-admin-design.md mục 5, PO chọn 06/11).
/// </summary>
public class PlayerAdminTests
{
    [DbFact]
    public async Task UpdateInfo_normalizes_validates_and_writes_audit_only_on_change()
    {
        await using var t = await TestDb.CreateAsync();
        await SetupAsync(t);
        await using var db = t.Context();
        var p = await db.Players.AsNoTracking().FirstAsync(x => x.Position != Position.GK && !db.SquadMemberships.Any(m => m.PlayerId == x.Id));
        var svc = new PlayerAdminService(db, t.Clock);

        Assert.Equal(new PlayerChangeResult(false), await svc.UpdateInfoAsync(p.Id, new("  Nguyễn Văn A ", p.Position, 1999, " vn "), "test"));
        await using (var check = t.Context())
        {
            var x = await check.Players.SingleAsync(x => x.Id == p.Id);
            Assert.Equal(("Nguyễn Văn A", 1999, "VN"), (x.Name, x.BirthYear, x.Nationality));
            var a = await check.AdminAudit.SingleAsync();
            Assert.Equal(("Player", p.Id, "Update", "test", t.Clock.GetUtcNow()), (a.EntityType, a.EntityId, a.Action, a.Actor, a.At));
            Assert.Equal((p.Name, "Nguyễn Văn A"), (Field(a.Before, "Name"), Field(a.After, "Name")));
            Assert.Equal((p.Position.ToString(), "1999"), (Field(a.After, "Position"), Number(a.After, "BirthYear")));
        }

        await svc.UpdateInfoAsync(p.Id, new("Nguyễn Văn A", p.Position, 1999, "VN"), "test");   // không đổi gì → không ghi nhật ký
        var e = await Assert.ThrowsAsync<AdminRuleException>(() => svc.UpdateInfoAsync(p.Id, new("Nguyễn Văn Abcdefgh", (Position)1, 1800, "VNMX"), "test"));
        Assert.Equal(4, e.Message.Split("; ").Length);
        e = await Assert.ThrowsAsync<AdminRuleException>(() => svc.UpdateInfoAsync(p.Id, new("李小龍", p.Position, null, null), "test"));
        Assert.Contains("không hiện được", e.Message);
        await Assert.ThrowsAsync<AdminRuleException>(() => svc.UpdateInfoAsync(Guid.NewGuid(), new("X", Position.CF, null, null), "test"));
        Assert.Equal(1, await db.AdminAudit.CountAsync());
    }

    [DbFact]
    public async Task Goalkeeper_position_change_promotes_next_keeper_and_blocks_below_two()
    {
        await using var t = await TestDb.CreateAsync();
        await SetupAsync(t);
        Guid club;
        await using (var db = t.Context()) club = (await ClubAsync(db, 0)).Id;
        await SetGoalkeepersAsync(t, club, 3);
        await using (var db = t.Context())
        {
            var squad = await SquadAsync(db, club);
            var gks = squad.Where(x => x.P.Position == Position.GK).ToList();
            Assert.Equal(0, gks[0].M.SlotOrder);
            var (keeper, next, third, nextSlot) = (gks[0].P.Id, gks[1].P, gks[2].P.Id, gks[1].M.SlotOrder);
            var svc = new PlayerAdminService(db, t.Clock);

            Assert.Equal(new PlayerChangeResult(true, next.Name), await svc.UpdateInfoAsync(keeper, new(gks[0].P.Name, Position.CB, null, null), "test"));
            await using (var check = t.Context())
            {
                var slots = await check.SquadMemberships.Where(m => m.ClubId == club && m.LeftAt == null).ToDictionaryAsync(m => m.PlayerId, m => m.SlotOrder);
                Assert.Equal((0, nextSlot), (slots[next.Id], slots[keeper]));
                Assert.Equal(Position.CB, (await check.Players.SingleAsync(x => x.Id == keeper)).Position);
            }

            var e = await Assert.ThrowsAsync<AdminRuleException>(() => svc.UpdateInfoAsync(third, new(gks[2].P.Name, Position.CF, null, null), "test"));
            Assert.Contains("1 thủ môn", e.Message);
            // Về lại GK được; đứng dự bị → không cần xếp lại đội hình.
            Assert.Equal(new PlayerChangeResult(nextSlot < 11), await svc.UpdateInfoAsync(keeper, new(gks[0].P.Name, Position.GK, null, null), "test"));
            Assert.Equal(2, await db.AdminAudit.CountAsync(a => a.Action == "Update"));
        }
    }

    [DbFact]
    public async Task SetAttributes_writes_admin_changes_validates_and_recalculates_price_once_season_priced()
    {
        await using var t = await TestDb.CreateAsync();
        var (season, _) = await SetupAsync(t);
        await using var db = t.Context();
        var p = (await SquadAsync(db, (await ClubAsync(db, 0)).Id))[5].P;
        var field = AttributeEvolution.RoleFields(p.Position).First();
        var start = (await db.PlayerAttributes.AsNoTracking().SingleAsync(a => a.SeasonId == season && a.PlayerId == p.Id)).Values;
        var svc = new PlayerAdminService(db, t.Clock);

        var e = await Assert.ThrowsAsync<AdminRuleException>(() => svc.SetAttributesAsync(p.Id,
            new Dictionary<string, int> { [field] = 100, ["XYZ"] = 5, [PlayerField.Height.Name] = 147, [PlayerField.Weight.Name] = 0 }, "test"));
        Assert.Equal(4, e.Message.Split("; ").Length);
        var target = start[field] > 50 ? start[field] - 4 : start[field] + 4;
        var age = start[PlayerField.Age.Name] == 30 ? 31 : 30;
        Assert.Equal(2, await svc.SetAttributesAsync(p.Id, new Dictionary<string, int>
            { [field] = target, [PlayerField.Age.Name] = age, [PlayerField.Height.Name] = start[PlayerField.Height.Name] }, "test"));
        Assert.Equal(0, await svc.SetAttributesAsync(p.Id, new Dictionary<string, int> { [field] = target }, "test"));   // như hiện tại → không ghi
        await using (var check = t.Context())
        {
            var rows = await check.AttributeChanges.Where(c => c.PlayerId == p.Id).ToListAsync();
            Assert.Equal(2, rows.Count);
            Assert.All(rows, c => Assert.Equal((AttributeChangeSource.Admin, (Guid?)null, t.Clock.GetUtcNow()), (c.Source, c.FixtureId, c.At)));
            Assert.Equal(target - start[field], rows.Single(c => c.Field == field).Delta);
            Assert.False(await check.PlayerPrices.AnyAsync());   // mùa chưa khởi động → chưa có giá
            var a = await check.AdminAudit.SingleAsync();
            Assert.Equal(("SetAttributes", start[field].ToString(), target.ToString()), (a.Action, Number(a.Before, field), Number(a.After, field)));
        }

        await new EconomyService(db, t.Clock).RecalculatePricesAsync(season);
        t.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(1, await svc.SetAttributesAsync(p.Id, new Dictionary<string, int> { [field] = target + 1 }, "test"));
        await using (var check = t.Context())
        {
            var current = (await new EconomyService(check, t.Clock).CurrentAttributesAsync(season, [p.Id]))[p.Id];
            Assert.Equal((target + 1, age), (current[field], current[PlayerField.Age.Name]));
            var price = await check.PlayerPrices.SingleAsync(x => x.SeasonId == season && x.PlayerId == p.Id);
            var model = new PriceModel();
            Assert.Equal((model.Overall(p.Position, current), t.Clock.GetUtcNow()), (price.Overall, price.UpdatedAt));
            Assert.Equal(model.BasePrice(price.Overall, age), price.BasePrice);
        }
    }

    [DbFact]
    public async Task Match_evolution_cap_is_measured_from_admin_edited_value()
    {
        await using var t = await TestDb.CreateAsync();
        var (season, _) = await SetupAsync(t);
        await using var db = t.Context();
        var home = await ClubAsync(db, 0);
        var away = await ClubAsync(db, 1);
        var p = (await SquadAsync(db, home.Id))[5].P;
        var fields = AttributeEvolution.RoleFields(p.Position).ToList();
        var start = (await db.PlayerAttributes.AsNoTracking().SingleAsync(a => a.SeasonId == season && a.PlayerId == p.Id)).Values;
        var target = start[fields[0]] > 5 ? start[fields[0]] - 4 : start[fields[0]] + 4;
        await new PlayerAdminService(db, t.Clock).SetAttributesAsync(p.Id, new Dictionary<string, int> { [fields[0]] = target }, "test");

        var md = new Matchday { SeasonId = season, Number = 1 };
        var fixture = new Fixture { MatchdayId = md.Id, HomeClubId = home.Id, AwayClubId = away.Id };
        db.AddRange(md, fixture);
        await db.SaveChangesAsync();
        var eco = new EconomyService(db, t.Clock);
        for (var i = 0; i < 4; i++)
            await eco.EvolveAttributesAsync(season, [new MatchRating { FixtureId = fixture.Id, ClubId = home.Id, PlayerId = p.Id, Slot = 5, Rating = 8.0m }]);

        var current = (await eco.CurrentAttributesAsync(season, [p.Id]))[p.Id];
        Assert.Equal(target + 3, current[fields[0]]);   // trần +3 tính từ giá trị admin sửa, không phải snapshot
        foreach (var f in fields.Skip(1).Where(f => start[f] <= 96))
            Assert.Equal(start[f] + 3, current[f]);
        Assert.Equal(3, await db.AttributeChanges.Where(c => c.PlayerId == p.Id && c.Field == fields[0] && c.Source == AttributeChangeSource.Match).SumAsync(c => c.Delta));
    }

    [DbFact]
    public async Task Finance_and_condition_apply_once_per_round()
    {
        await using var t = await TestDb.CreateAsync();
        var (season, _) = await SetupAsync(t);
        await using var db = t.Context();
        var home = await ClubAsync(db, 0);
        var away = await ClubAsync(db, 1);
        var p = (await SquadAsync(db, home.Id))[5].P;
        var md = new Matchday { SeasonId = season, Number = 1 };
        var fixture = new Fixture { MatchdayId = md.Id, HomeClubId = home.Id, AwayClubId = away.Id, HomeGoals = 2, AwayGoals = 0 };
        db.AddRange(md, fixture, new MatchRating { FixtureId = fixture.Id, ClubId = home.Id, PlayerId = p.Id, Slot = 5, Rating = 8.0m });
        await db.SaveChangesAsync();
        await new EconomyService(db, t.Clock).SettleRoundAsync(season, md.Id);
        var fin = new FinanceService(db, t.Clock);
        var before = await db.Ledger.Where(l => l.ClubId == home.Id).SumAsync(l => l.Amount);
        await fin.ApplyRoundAsync(season, md.Id);
        await fin.ApplyRoundAsync(season, md.Id);   // lần 2 không ghi
        var f = await db.ClubFinances.SingleAsync(x => x.ClubId == home.Id);
        var expected = FinanceRules.TvPerRound + f.SponsorPerRound - f.Capacity * FinanceRules.UpkeepPerSeat + FinanceRules.WinPrize
            + await db.Ledger.Where(l => l.ClubId == home.Id && l.Reason == LedgerReason.Ticket).SumAsync(l => l.Amount);
        Assert.True(await db.Ledger.AnyAsync(l => l.ClubId == home.Id && l.Reason == LedgerReason.Ticket && l.Amount > 0));
        Assert.Equal(before + expected, await db.Ledger.Where(l => l.ClubId == home.Id).SumAsync(l => l.Amount));
        Assert.Equal("Giá vé 5–40.", await fin.SetTicketPriceAsync(season, home.Id, 99));
        db.Ledger.Add(new ClubLedgerEntry { ClubId = home.Id, Amount = FinanceRules.ExpandCost, Reason = LedgerReason.Adjustment, At = t.Clock.GetUtcNow() });
        await db.SaveChangesAsync();
        var cap = f.Capacity;
        Assert.Null(await fin.ExpandStadiumAsync(season, home.Id));
        Assert.Equal(cap + FinanceRules.ExpandSeats, f.Capacity);
        var report = await fin.ReportAsync(season, home.Id);
        Assert.Single(report.Rounds);

        var cond = new ConditionService(db, t.Clock);
        await cond.ApplyRoundAsync(md.Id);
        await cond.ApplyRoundAsync(md.Id);
        var c = await db.PlayerConditions.SingleAsync(x => x.PlayerId == p.Id);
        Assert.True(c.Fatigue >= ConditionRules.MatchFatigue);
        Assert.Equal(ConditionRules.DefaultMorale + 8, c.Morale);
        var rested = await db.PlayerConditions.FirstAsync(x => x.PlayerId != p.Id && db.SquadMemberships.Any(m => m.PlayerId == x.PlayerId && m.ClubId == home.Id && m.LeftAt == null));
        Assert.Equal((0, ConditionRules.DefaultMorale - 2), (rested.Fatigue, rested.Morale));
    }

    [DbFact]
    public async Task Media_press_talk_and_inbox()
    {
        await using var t = await TestDb.CreateAsync();
        var (season, _) = await SetupAsync(t);
        await using var db = t.Context();
        var home = await ClubAsync(db, 0);
        var away = await ClubAsync(db, 1);
        var p = (await SquadAsync(db, home.Id))[5].P;
        var md = new Matchday { SeasonId = season, Number = 1 };
        var md2 = new Matchday { SeasonId = season, Number = 2 };
        var fixture = new Fixture { MatchdayId = md.Id, HomeClubId = home.Id, AwayClubId = away.Id, HomeGoals = 0, AwayGoals = 1, Status = FixtureStatus.Played };
        db.AddRange(md, md2, fixture, new Fixture { MatchdayId = md2.Id, HomeClubId = away.Id, AwayClubId = home.Id },
            new MatchRating { FixtureId = fixture.Id, ClubId = home.Id, PlayerId = p.Id, Slot = 5, Rating = 4.5m });
        await db.SaveChangesAsync();
        var svc = new MediaService(db, t.Clock);
        var desk = await svc.DeskAsync(season, home.Id);
        Assert.Equal(fixture.Id, desk.Press!.FixtureId);
        Assert.Contains(desk.Inbox, i => i.Title == "Chuẩn bị vòng 2");
        Assert.Equal("Giận dữ", desk.FanMood);
        Assert.Null(await svc.PressAsync(season, home.Id, fixture.Id, PressTone.Demanding));
        Assert.NotNull(await svc.PressAsync(season, home.Id, fixture.Id, PressTone.Praise));
        Assert.Equal(ConditionRules.DefaultMorale - 3, (await db.PlayerConditions.SingleAsync(c => c.PlayerId == p.Id)).Morale);
        Assert.Equal((null, 4), await svc.TalkAsync(season, home.Id, p.Id, TalkKind.Criticise));   // đá kém + còn tự tin
        Assert.NotNull((await svc.TalkAsync(season, home.Id, p.Id, TalkKind.Praise)).Error);
        desk = await svc.DeskAsync(season, home.Id);
        Assert.Null(desk.Press);
        Assert.Contains(p.Id, desk.TalkedThisRound);
    }

    [DbFact]
    public async Task Staff_hire_costs_fee_and_wage()
    {
        await using var t = await TestDb.CreateAsync();
        var (season, _) = await SetupAsync(t);
        await using var db = t.Context();
        var home = await ClubAsync(db, 0);
        var away = await ClubAsync(db, 1);
        long Balance() => db.Ledger.Where(l => l.ClubId == home.Id).Sum(l => l.Amount);
        db.Ledger.Add(new ClubLedgerEntry { ClubId = home.Id, Amount = 10_000_000, Reason = LedgerReason.Adjustment, At = t.Clock.GetUtcNow() });
        await db.SaveChangesAsync();
        var board = new BoardService(db, t.Clock);
        var before = Balance();
        Assert.Null(await board.SetLevelAsync(home.Id, StaffRole.Physio, 2));
        Assert.Equal(before - StaffRules.SigningFee(2), Balance());
        Assert.Equal(2, (await board.StaffAsync(home.Id)).Single(s => s.Role == StaffRole.Physio).Level);

        var md = new Matchday { SeasonId = season, Number = 1 };
        db.AddRange(md, new Fixture { MatchdayId = md.Id, HomeClubId = home.Id, AwayClubId = away.Id, HomeGoals = 1, AwayGoals = 1, Status = FixtureStatus.Played });
        await db.SaveChangesAsync();
        await new EconomyService(db, t.Clock).SettleRoundAsync(season, md.Id);
        await new FinanceService(db, t.Clock).ApplyRoundAsync(season, md.Id);
        Assert.Equal(-StaffRules.Wage(2), await db.Ledger.Where(l => l.ClubId == home.Id && l.Reason == LedgerReason.Staff && l.RefType == nameof(RoundSettlement)).SumAsync(l => l.Amount));
        await board.ApplyRoundAsync(season, md.Id);   // không có giải quốc nội trong dữ liệu test → không lỗi
        await new ConditionService(db, t.Clock).ApplyRoundAsync(md.Id);
    }

    [DbFact]
    public async Task Youth_promote_and_scout_flow()
    {
        await using var t = await TestDb.CreateAsync();
        var (season, _) = await SetupAsync(t);
        await using var db = t.Context();
        var home = await ClubAsync(db, 0);
        var away = await ClubAsync(db, 1);
        long Balance() => db.Ledger.Where(l => l.ClubId == home.Id).Sum(l => l.Amount);
        db.Ledger.Add(new ClubLedgerEntry { ClubId = home.Id, Amount = 1_000_000, Reason = LedgerReason.Adjustment, At = t.Clock.GetUtcNow() });
        await db.SaveChangesAsync();
        var youth = new YouthService(db, t.Clock);

        var size = await db.SquadMemberships.CountAsync(m => m.ClubId == home.Id && m.LeftAt == null);
        var err = await youth.PromoteAsync(season, home.Id, Position.CF);
        if (size >= 32) Assert.NotNull(err);
        else
        {
            Assert.Null(err);
            var st = await youth.StatusAsync(season, home.Id);
            Assert.Equal(1, st.UsedThisSeason);
            var pid = st.Players.Single().PlayerId;
            Assert.True(await db.SquadMemberships.AnyAsync(m => m.PlayerId == pid && m.ClubId == home.Id && m.LeftAt == null));
            Assert.Equal(YouthRules.ContractMatches, (await db.Contracts.SingleAsync(c => c.PlayerId == pid)).MatchesTotal);
        }

        var target = (await SquadAsync(db, away.Id))[3].P.Id;
        Assert.False(await youth.KnowsAsync(home.Id, target));
        Assert.Contains(target, await youth.HiddenAsync(home.Id));
        var before = Balance();
        Assert.Null(await youth.ScoutAsync(home.Id, target));
        Assert.Equal(before - ScoutRules.Fee, Balance());
        Assert.NotNull(await youth.ScoutAsync(home.Id, target));                         // giao trùng
        var others = await SquadAsync(db, away.Id);
        Assert.Null(await youth.ScoutAsync(home.Id, others[4].P.Id));
        Assert.Contains("bận", await youth.ScoutAsync(home.Id, others[5].P.Id));     // cấp 1: tối đa 2 việc
        var md1 = new Matchday { SeasonId = season, Number = 1 };
        var md2 = new Matchday { SeasonId = season, Number = 2 };
        db.AddRange(md1, md2);
        await db.SaveChangesAsync();
        await youth.ApplyRoundAsync(md1.Id);
        await youth.ApplyRoundAsync(md1.Id);                                            // idempotent
        Assert.Equal(1, (await youth.ReportAsync(home.Id, target))!.RoundsLeft);
        Assert.False(await youth.KnowsAsync(home.Id, target));
        await youth.ApplyRoundAsync(md2.Id);
        Assert.True(await youth.KnowsAsync(home.Id, target));
        Assert.DoesNotContain(target, await youth.HiddenAsync(home.Id));
        Assert.Empty(await youth.HiddenAsync(Guid.Empty));
    }

    [DbFact]
    public async Task Tactic_preset_applies_and_matches()
    {
        await using var t = await TestDb.CreateAsync();
        await SetupAsync(t);
        await using var db = t.Context();
        var club = (await ClubAsync(db, 0)).Id;
        var svc = new TacticsService(db, t.Clock);
        Assert.Null(TacticPresets.Match(await svc.GetAsync(club)));
        foreach (var p in TacticPresets.All)
        {
            Assert.Null(await TacticPresets.ApplyAsync(svc, club, p.Key));
            Assert.Equal(p.Key, TacticPresets.Match(await svc.GetAsync(club))?.Key);
        }
        Assert.Null(await svc.SetStrategyAsync(club, 0, 3));
        Assert.Null(TacticPresets.Match(await svc.GetAsync(club)));          // chỉnh tay → không còn khớp phong cách
    }

    [Fact]
    public void Youth_and_scout_rules()
    {
        Assert.Equal("70–79", ScoutRules.Mask(74));
        Assert.Equal("90+", ScoutRules.Mask(93));
        Assert.Equal((2, 1, 0), (ScoutRules.Rounds(1), ScoutRules.Rounds(3), ScoutRules.Rounds(5)));
        Assert.InRange(YouthRules.Attribute(80, 12345), 63, 72);
        Assert.InRange(YouthRules.Age(7), 16, 17);
        Assert.False(string.IsNullOrWhiteSpace(YouthRules.Name(42)));
    }

    [Fact]
    public void Board_and_media_rules()
    {
        Assert.Equal("Vô địch", BoardRules.Objective(1, 20));
        Assert.Equal("Top 4", BoardRules.Objective(3, 20));
        Assert.Equal("Trụ hạng", BoardRules.Objective(15, 20));
        Assert.Equal(3, BoardRules.Delta(2, 4, 1));
        Assert.Equal(-3, BoardRules.Delta(9, 4, -2));
        Assert.Equal((0, 375_000), (StaffRules.Wage(1), StaffRules.Wage(4) - StaffRules.Wage(1) + 150_000));
        Assert.Equal((-3, 5), MediaRules.Press(PressTone.Demanding, -2));
        Assert.Equal(6, MediaRules.Talk(TalkKind.Praise, 7.5m, 60));
        Assert.Equal("Phấn khích", MediaRules.FanMood(13, 5));
    }

    [Fact]
    public void Condition_and_finance_rules()
    {
        Assert.True(ConditionRules.Injured(80, 50));
        Assert.False(ConditionRules.Injured(10, 50));
        Assert.InRange(ConditionRules.InjuryRounds(7), 1, 4);
        Assert.Equal(-8, ConditionRules.MoraleDelta(true, 4.5m, -1));
        Assert.Equal(-2, ConditionRules.MoraleDelta(false, null, 3));
        Assert.Equal(30, ConditionRules.EffectiveOverall(70, new PlayerCondition { InjuredRounds = 2 }));
        Assert.Equal((15_000, 54_000), (FinanceRules.DefaultCapacity(0.6), FinanceRules.DefaultCapacity(1.4)));
        Assert.True(FinanceRules.Attendance(30000, 50000, 5) <= 30000);
        Assert.True(FinanceRules.Attendance(80000, 50000, 40) < FinanceRules.Attendance(80000, 50000, 10));
    }

    [DbFact]
    public async Task Retire_ends_contract_cancels_renewal_hides_player_and_respects_squad_rules()
    {
        await using var t = await TestDb.CreateAsync();
        var (season, _) = await SetupAsync(t);
        Guid club, other;
        await using (var db = t.Context()) (club, other) = ((await ClubAsync(db, 0)).Id, (await ClubAsync(db, 1)).Id);
        await using (var db = t.Context())
        {
            var p = (await SquadAsync(db, club))[5].P;
            var contract = await db.Contracts.SingleAsync(k => k.PlayerId == p.Id && k.Status == ContractStatus.Active);
            db.ContractRenewals.Add(new ContractRenewal { SeasonId = season, ContractId = contract.Id, PlayerId = p.Id, ClubId = club, Matches = 10, Fee = 1, Wage = 1,
                Status = RenewalStatus.Pending, RequestedAt = t.Clock.GetUtcNow() });
            await db.SaveChangesAsync();
            var svc = new PlayerAdminService(db, t.Clock);
            await svc.RetireAsync(p.Id, "test");
            await using (var check = t.Context())
            {
                Assert.False(await check.SquadMemberships.AnyAsync(m => m.PlayerId == p.Id && m.LeftAt == null));
                var k = await check.Contracts.SingleAsync(x => x.Id == contract.Id);
                Assert.Equal((ContractStatus.Ended, ContractEndReason.Retired, t.Clock.GetUtcNow()), (k.Status, k.EndReason, k.EndedAt));
                var r = await check.ContractRenewals.SingleAsync();
                Assert.Equal((RenewalStatus.Cancelled, "PlayerRetired"), (r.Status, r.RejectReason));
                Assert.False((await check.Players.SingleAsync(x => x.Id == p.Id)).MarketVisibility);
                var a = await check.AdminAudit.SingleAsync();
                Assert.Equal(("Retire", (await check.Clubs.SingleAsync(c => c.Id == club)).Name), (a.Action, Field(a.Before, "Club")));
            }
            Assert.Contains("đã giải nghệ", (await Assert.ThrowsAsync<AdminRuleException>(() => svc.RetireAsync(p.Id, "test"))).Message);

            await svc.SetVisibilityAsync(p.Id, true, "test");
            await svc.SetVisibilityAsync(p.Id, true, "test");   // không đổi → không ghi
            Assert.True((await db.Players.AsNoTracking().SingleAsync(x => x.Id == p.Id)).MarketVisibility);
            Assert.Equal(1, await db.AdminAudit.CountAsync(a => a.Action == "SetVisibility"));

            var free = await db.Players.AsNoTracking().FirstAsync(x => x.MarketVisibility == null && !db.SquadMemberships.Any(m => m.PlayerId == x.Id));
            Assert.Equal(new PlayerChangeResult(false), await svc.RetireAsync(free.Id, "test"));
            Assert.False((await db.Players.AsNoTracking().SingleAsync(x => x.Id == free.Id)).MarketVisibility);
        }

        await SetGoalkeepersAsync(t, other, 2);
        await using (var db = t.Context())
        {
            var svc = new PlayerAdminService(db, t.Clock);
            var squad = await SquadAsync(db, other);
            var gk = squad.First(x => x.P.Position == Position.GK).P.Id;
            Assert.Contains("thủ môn", (await Assert.ThrowsAsync<AdminRuleException>(() => svc.RetireAsync(gk, "test"))).Message);
            foreach (var x in squad.Where(x => x.P.Position != Position.GK).Skip(14)) x.M.LeftAt = t.Clock.GetUtcNow();   // còn 16 người
            await db.SaveChangesAsync();
            Assert.Contains("16", (await Assert.ThrowsAsync<AdminRuleException>(() => svc.RetireAsync(squad[5].P.Id, "test"))).Message);
            Assert.Equal(0, await db.AdminAudit.CountAsync(a => a.Action == "Retire" && a.EntityId == gk));
        }
    }

    [DbFact]
    public async Task Round_option_file_writes_edited_name_position_and_match_build_skips_admin_edits_after_lock()
    {
        await using var t = await TestDb.CreateAsync();
        var (season, patch) = await SetupAsync(t);
        await using var db = t.Context();
        var home = await ClubAsync(db, 0);
        var squad = await SquadAsync(db, home.Id);
        var (p, q) = (squad[5].P, squad[6].P);
        var pes = await db.PlayerPesMappings.Where(m => m.PatchVersionId == patch).ToDictionaryAsync(m => m.PlayerId, m => m.PesPlayerId);
        var field = PlayerField.Verified.Single(f => f.Name == AttributeEvolution.RoleFields(p.Position).First());
        var position = p.Position == Position.AMF ? Position.CF : Position.AMF;
        var svc = new PlayerAdminService(db, t.Clock);
        await svc.UpdateInfoAsync(p.Id, new("Nguyễn Văn A", position, null, null), "test");
        var orig = OptionFileCodec.Decode(Env.OrigOptionFile);
        var v1 = orig.Get(pes[p.Id], field) > 50 ? 40 : 60;
        await svc.SetAttributesAsync(p.Id, new Dictionary<string, int> { [field.Name] = v1 }, "test");

        var md = new Matchday { SeasonId = season, Number = 1, Status = MatchdayStatus.Locked };
        db.Matchdays.Add(md);
        await db.SaveChangesAsync();
        var rounds = new RoundOptionFileService(db, t.Store, t.Clock);
        var round = await rounds.BuildAsync(patch, md.Id);
        Assert.True(round.Ok, string.Join("; ", round.Issues.Select(i => i.Message)));
        md.OptionFileBuildId = round.BuildId;
        await db.SaveChangesAsync();
        var of = OptionFileCodec.Decode(await t.Store.ReadAsync(round.StoragePath!));
        Assert.Equal(("Nguyen Van A", position, v1), (of.GetPlayerName(pes[p.Id]), of.GetPosition(pes[p.Id]), of.Get(pes[p.Id], field)));
        Assert.Equal((orig.GetPlayerName(pes[q.Id]), orig.GetPosition(pes[q.Id])), (of.GetPlayerName(pes[q.Id]), of.GetPosition(pes[q.Id])));

        t.Clock.Advance(TimeSpan.FromMinutes(5));   // admin sửa sau khi khoá vòng → OF trận giữ như OF vòng
        await svc.SetAttributesAsync(p.Id, new Dictionary<string, int> { [field.Name] = v1 + 1 }, "test");
        var match = await rounds.BuildForMatchAsync(md.Id, [home.Id]);
        Assert.True(match.Ok);
        of = OptionFileCodec.Decode(await t.Store.ReadAsync(match.StoragePath!));
        Assert.Equal(("Nguyen Van A", v1), (of.GetPlayerName(pes[p.Id]), of.Get(pes[p.Id], field)));

        var next = await rounds.BuildAsync(patch, md.Id);   // OF vòng sau mang giá trị mới
        of = OptionFileCodec.Decode(await t.Store.ReadAsync(next.StoragePath!));
        Assert.Equal(v1 + 1, of.Get(pes[p.Id], field));
    }

    [DbFact]
    public async Task Create_takes_lowest_free_slot_from_position_template_and_becomes_visible_free_agent()
    {
        await using var t = await TestDb.CreateAsync();
        var (season, patch) = await SetupAsync(t);
        await using var db = t.Context();
        var svc = new PlayerAdminService(db, t.Clock);
        Assert.Equal(OptionFileImage.EditCount, await svc.FreeSlotsAsync());

        var template = await svc.TemplateAsync(Position.CB);
        var defense = (await (from pm in db.PlayerPesMappings
                              where pm.PatchVersionId == patch
                              join p in db.Players on pm.PlayerId equals p.Id
                              where p.Position == Position.CB
                              join a in db.PlayerAttributes on p.Id equals a.PlayerId
                              select new { pm.PesPlayerId, a.Values }).ToListAsync())
            .Where(x => !OptionFileImage.IsHiddenPlayerId(x.PesPlayerId)).Select(x => x.Values[PlayerField.Defense.Name]).Order().ToList();
        Assert.Equal(defense[(defense.Count - 1) / 2], template[PlayerField.Defense.Name]);
        Assert.True(template[PlayerField.Defense.Name] > (await svc.TemplateAsync(Position.CF))[PlayerField.Defense.Name]);

        var bad = new Dictionary<string, int>(template) { [PlayerField.Height.Name] = 147, ["XYZ"] = 1 };
        bad.Remove(PlayerField.Age.Name);
        var e = await Assert.ThrowsAsync<AdminRuleException>(() => svc.CreateAsync(new(" ", Position.CB, null, null), bad, "test"));
        Assert.Equal(4, e.Message.Split("; ").Length);   // tên, thiếu AGE, HEIGHT, XYZ

        var id = await svc.CreateAsync(new(" Nguyễn Văn Đức ", Position.CB, 2004, "vie"), template, "test");
        var id2 = await svc.CreateAsync(new("Trần B", Position.GK, null, null), await svc.TemplateAsync(Position.GK), "test");
        await using (var check = t.Context())
        {
            var p = await check.Players.SingleAsync(x => x.Id == id);
            Assert.Equal(("Nguyễn Văn Đức", Position.CB, 2004, "VIE", (Guid?)null, (bool?)null),
                (p.Name, p.Position, p.BirthYear, p.Nationality, p.OriginClubId, p.MarketVisibility));
            var slots = await check.PlayerPesMappings.Where(m => m.PlayerId == id || m.PlayerId == id2).ToDictionaryAsync(m => m.PlayerId);
            Assert.Equal((patch, OptionFileImage.FirstEditId, (int?)null), (slots[id].PatchVersionId, slots[id].PesPlayerId, slots[id].OriginPesClubIndex));
            Assert.Equal(OptionFileImage.FirstEditId + 1, slots[id2].PesPlayerId);
            var a = await check.PlayerAttributes.SingleAsync(x => x.PlayerId == id);
            Assert.Equal((season, t.Clock.GetUtcNow()), (a.SeasonId, a.CapturedAt));
            Assert.Equal(template.OrderBy(kv => kv.Key), a.Values.OrderBy(kv => kv.Key));
            var audit = await check.AdminAudit.SingleAsync(x => x.EntityId == id);
            Assert.Equal(("Create", OptionFileImage.FirstEditId.ToString(), "Nguyễn Văn Đức"),
                (audit.Action, Number(audit.After, "PesId"), JsonDocument.Parse(audit.After!).RootElement.GetProperty("Name").GetString()));
            Assert.False(await check.PlayerPrices.AnyAsync());   // mùa chưa khởi động → chưa có giá
            var admin = new PlayerAdminService(check, t.Clock);
            Assert.True((await admin.DetailAsync(id))!.Visible);
            Assert.Contains((await admin.ListAsync()).Rows, r => r.Id == id && r.ClubId == null && r.Visible);   // tự do, hiện trên thị trường
        }
        Assert.Equal(OptionFileImage.EditCount - 2, await svc.FreeSlotsAsync());

        await svc.DeleteAsync(id, "test");   // ô thấp nhất trống lại → lần tạo sau dùng lại
        await new EconomyService(db, t.Clock).RecalculatePricesAsync(season);
        var cf = await svc.TemplateAsync(Position.CF);
        var id3 = await svc.CreateAsync(new("Lê C", Position.CF, null, null), cf, "test");
        Assert.Equal(OptionFileImage.FirstEditId, await db.PlayerPesMappings.Where(m => m.PlayerId == id3).Select(m => m.PesPlayerId).SingleAsync());
        Assert.Equal(new PriceModel().Overall(Position.CF, cf), (await db.PlayerPrices.SingleAsync(x => x.PlayerId == id3)).Overall);   // mùa đã có giá → tính luôn
    }

    [DbFact]
    public async Task Delete_only_admin_created_players_never_played_or_signed()
    {
        await using var t = await TestDb.CreateAsync();
        var (season, _) = await SetupAsync(t);
        await using var db = t.Context();
        var svc = new PlayerAdminService(db, t.Clock);
        var club = await ClubAsync(db, 0);
        var patchPlayer = (await SquadAsync(db, club.Id))[5].P.Id;
        Assert.Contains("patch", await svc.DeleteBlockAsync(patchPlayer));
        await Assert.ThrowsAsync<AdminRuleException>(() => svc.DeleteAsync(patchPlayer, "test"));

        var template = await svc.TemplateAsync(Position.CMF);
        var played = await svc.CreateAsync(new("A", Position.CMF, null, null), template, "test");
        var signed = await svc.CreateAsync(new("B", Position.CMF, null, null), template, "test");
        var clean = await svc.CreateAsync(new("C", Position.CMF, null, null), template, "test");
        db.AttributeChanges.Add(new AttributeChange { SeasonId = season, PlayerId = played, Field = PlayerField.Attack.Name, Delta = 1 });
        db.SquadMemberships.Add(new SquadMembership { ClubId = club.Id, PlayerId = signed, ShirtNumber = 99, SlotOrder = 31,
            JoinedAt = t.Clock.GetUtcNow(), LeftAt = t.Clock.GetUtcNow() });   // đã rời CLB vẫn tính là từng thuộc CLB
        await db.SaveChangesAsync();
        await svc.SetAttributesAsync(clean, new Dictionary<string, int> { [PlayerField.Attack.Name] = template[PlayerField.Attack.Name] + 1 }, "test");
        Assert.Contains("ra sân", await svc.DeleteBlockAsync(played));
        Assert.Contains("CLB", await svc.DeleteBlockAsync(signed));
        Assert.Null(await svc.DeleteBlockAsync(clean));   // admin sửa chỉ số không tính là ra sân
        await Assert.ThrowsAsync<AdminRuleException>(() => svc.DeleteAsync(signed, "test"));

        await svc.DeleteAsync(clean, "test");
        await using var check = t.Context();
        Assert.False(await check.Players.AnyAsync(x => x.Id == clean));
        Assert.False(await check.PlayerPesMappings.AnyAsync(x => x.PlayerId == clean));
        Assert.False(await check.PlayerAttributes.AnyAsync(x => x.PlayerId == clean));
        Assert.False(await check.AttributeChanges.AnyAsync(x => x.PlayerId == clean));
        var a = await check.AdminAudit.SingleAsync(x => x.EntityId == clean && x.Action == "Delete");
        Assert.Equal(("C", (string?)null), (JsonDocument.Parse(a.Before!).RootElement.GetProperty("Name").GetString(), a.After));
        Assert.Equal(OptionFileImage.EditCount - 2, await new PlayerAdminService(check, t.Clock).FreeSlotsAsync());
    }

    [DbFact]
    public async Task Round_option_file_writes_created_player_record_and_match_build_adds_players_created_after_lock()
    {
        await using var t = await TestDb.CreateAsync();
        var (season, patch) = await SetupAsync(t);
        await using var db = t.Context();
        var home = await ClubAsync(db, 0);
        var squad = await SquadAsync(db, home.Id);
        var orig = OptionFileCodec.Decode(Env.OrigOptionFile);
        var svc = new PlayerAdminService(db, t.Clock);
        var cb = new Dictionary<string, int>(await svc.TemplateAsync(Position.CB));
        var a = await svc.CreateAsync(new("Nguyễn Văn Đức", Position.CB, 2007, "vie"), cb, "test");
        cb[PlayerField.Defense.Name] += 2;
        await svc.SetAttributesAsync(a, new Dictionary<string, int> { [PlayerField.Defense.Name] = cb[PlayerField.Defense.Name] }, "test");
        // Cầu thủ patch đổi vị trí đăng ký → bật thêm ô vị trí đá được của vị trí mới, giữ ô cũ (PO chọn 06/11).
        var p = squad[5].P;
        var pPes = await db.PlayerPesMappings.Where(m => m.PatchVersionId == patch && m.PlayerId == p.Id).Select(m => m.PesPlayerId).SingleAsync();
        var position = Enum.GetValues<Position>().First(x => x != Position.GK && orig.Get(pPes, PlayerField.Playable(x)) == 0);
        await svc.UpdateInfoAsync(p.Id, new(p.Name, position, null, null), "test");

        var md = new Matchday { SeasonId = season, Number = 1, Status = MatchdayStatus.Locked };
        db.Matchdays.Add(md);
        await db.SaveChangesAsync();
        var rounds = new RoundOptionFileService(db, t.Store, t.Clock);
        var round = await rounds.BuildAsync(patch, md.Id);
        Assert.True(round.Ok, string.Join("; ", round.Issues.Select(i => i.Message)));
        md.OptionFileBuildId = round.BuildId;
        await db.SaveChangesAsync();
        var of = OptionFileCodec.Decode(await t.Store.ReadAsync(round.StoragePath!));
        // Có năm sinh → tuổi trong game = năm build − năm sinh (thay AGE); quốc tịch theo mã trong game (VIE = 54).
        AssertCreated(of, OptionFileImage.FirstEditId, "Nguyen Van Duc", "DUC", Position.CB,
            new Dictionary<string, int>(cb) { [PlayerField.Age.Name] = t.Clock.GetUtcNow().Year - 2007 });
        Assert.Equal(54, of.Get(OptionFileImage.FirstEditId, PlayerField.Nationality));
        Assert.Equal(position, of.GetPosition(pPes));
        Assert.Equal(orig.Get(pPes, PlayerField.Nationality), of.Get(pPes, PlayerField.Nationality));
        Assert.All(Enum.GetValues<Position>(), x =>
            Assert.Equal(x == position ? 1 : orig.Get(pPes, PlayerField.Playable(x)), of.Get(pPes, PlayerField.Playable(x))));
        Assert.Equal(orig.GetShirtName(pPes), of.GetShirtName(pPes));
        Assert.Equal(("", Position.CF), (of.GetPlayerName(OptionFileImage.FirstEditId + 1), of.GetPosition(OptionFileImage.FirstEditId + 1)));   // ô chưa dùng giữ bản mẫu

        // Sau khi khoá vòng: tạo B, CLB nhận B thay 1 dự bị → OF trận ghi đủ B; A đổi tên → để OF vòng sau (A7).
        t.Clock.Advance(TimeSpan.FromMinutes(5));
        var ss = await svc.TemplateAsync(Position.SS);
        var b = await svc.CreateAsync(new("Trần Văn Bình", Position.SS, null, null), ss, "test");
        await svc.UpdateInfoAsync(a, new("Lê Văn Cường", Position.CB, null, null), "test");
        var sub = squad.Last(x => x.P.Position != Position.GK).M;
        sub.LeftAt = t.Clock.GetUtcNow();
        await db.SaveChangesAsync();
        db.SquadMemberships.Add(new SquadMembership { ClubId = home.Id, PlayerId = b, ShirtNumber = sub.ShirtNumber, SlotOrder = sub.SlotOrder, JoinedAt = t.Clock.GetUtcNow() });
        await db.SaveChangesAsync();
        var match = await rounds.BuildForMatchAsync(md.Id, [home.Id]);
        Assert.True(match.Ok, string.Join("; ", match.Issues.Select(i => i.Message)));
        of = OptionFileCodec.Decode(await t.Store.ReadAsync(match.StoragePath!));
        AssertCreated(of, OptionFileImage.FirstEditId + 1, "Tran Van Binh", "BINH", Position.SS, ss);
        Assert.Equal(GameText.FreeNationality, of.Get(OptionFileImage.FirstEditId + 1, PlayerField.Nationality));   // không quốc tịch → Tự do
        Assert.Contains(of.GetSquad(0).Players, x => x.PlayerId == OptionFileImage.FirstEditId + 1);
        Assert.Equal(("Nguyen Van Duc", "DUC"), (of.GetPlayerName(OptionFileImage.FirstEditId), of.GetShirtName(OptionFileImage.FirstEditId)));

        var next = await rounds.BuildAsync(patch, md.Id);
        of = OptionFileCodec.Decode(await t.Store.ReadAsync(next.StoragePath!));
        Assert.Equal(("Le Van Cuong", "CUONG"), (of.GetPlayerName(OptionFileImage.FirstEditId), of.GetShirtName(OptionFileImage.FirstEditId)));
        AssertCreated(of, OptionFileImage.FirstEditId + 1, "Tran Van Binh", "BINH", Position.SS, ss);
    }

    /// <summary>Bản ghi cầu thủ tạo mới trong OF: tên, tên in áo, vị trí đăng ký, ô vị trí đá được chỉ vị trí đăng ký, đủ 33 chỉ số.</summary>
    private static void AssertCreated(OptionFileImage of, int pesId, string name, string shirt, Position position, IReadOnlyDictionary<string, int> values)
    {
        Assert.Equal((name, shirt, position), (of.GetPlayerName(pesId), of.GetShirtName(pesId), of.GetPosition(pesId)));
        Assert.Equal([position], Enum.GetValues<Position>().Where(x => of.Get(pesId, PlayerField.Playable(x)) == 1));
        Assert.All(PlayerField.Verified, f => Assert.Equal(values[f.Name], of.Get(pesId, f)));
    }

    /// <summary>Mùa 1 (Planned) + patch nhập từ OF gốc với 2 CLB tham gia (ô 0, 1) và cầu thủ tự do.</summary>
    private static async Task<(Guid Season, Guid Patch)> SetupAsync(TestDb t)
    {
        await using var db = t.Context();
        var season = new Season { Name = "Mùa 1", Number = 2, CreatedAt = t.Clock.GetUtcNow() };
        db.Seasons.Add(season);
        await db.SaveChangesAsync();
        season.PatchVersionId = (await new ImportPatchService(db, t.Store, t.Clock)
            .ImportAsync("PES6VN 25-26", Env.OrigOptionFile, season.Id, [new LeagueClub(0), new LeagueClub(1)])).PatchVersionId;
        await db.SaveChangesAsync();
        return (season.Id, season.PatchVersionId.Value);
    }

    private static Task<Club> ClubAsync(ClubsDbContext db, int slot) =>
        (from cm in db.ClubPesMappings where cm.PesClubIndex == slot join c in db.Clubs on cm.ClubId equals c.Id select c).SingleAsync();

    private static async Task<List<(SquadMembership M, FootballPlayer P)>> SquadAsync(ClubsDbContext db, Guid clubId) =>
        [.. (await (from m in db.SquadMemberships
                    where m.ClubId == clubId && m.LeftAt == null
                    join p in db.Players on m.PlayerId equals p.Id
                    orderby m.SlotOrder
                    select new { m, p }).ToListAsync()).Select(x => (x.m, x.p))];

    /// <summary>Đưa số GK của CLB về đúng <paramref name="n"/>: bỏ GK dự bị cuối squad hoặc thêm GK tự do vào cuối.</summary>
    private static async Task SetGoalkeepersAsync(TestDb t, Guid clubId, int n)
    {
        await using var db = t.Context();
        var squad = await SquadAsync(db, clubId);
        var gks = squad.Where(x => x.P.Position == Position.GK).ToList();
        foreach (var x in gks.Skip(n)) x.M.LeftAt = t.Clock.GetUtcNow();
        var used = squad.Select(x => x.M.ShirtNumber).ToHashSet();
        var slot = squad.Max(x => x.M.SlotOrder);
        foreach (var p in await db.Players.Where(p => p.Position == Position.GK && !db.SquadMemberships.Any(m => m.PlayerId == p.Id && m.LeftAt == null))
                     .OrderBy(p => p.Name).Take(Math.Max(0, n - gks.Count)).ToListAsync())
        {
            var number = Enumerable.Range(1, 99).First(i => used.Add(i));
            db.SquadMemberships.Add(new SquadMembership { ClubId = clubId, PlayerId = p.Id, ShirtNumber = number, SlotOrder = ++slot, JoinedAt = t.Clock.GetUtcNow() });
        }
        await db.SaveChangesAsync();
    }

    private static string? Field(string? json, string name) => JsonDocument.Parse(json!).RootElement.GetProperty(name).GetString();

    private static string Number(string? json, string name) => JsonDocument.Parse(json!).RootElement.GetProperty(name).GetRawText();
}
