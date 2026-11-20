using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Application;
using Pes17League.Clubs.Domain;
using Pes17League.Clubs.Infrastructure;
using Pes17League.OptionFile;

namespace Pes17League.Clubs.Tests;

/// <summary>Luồng đầy đủ trên Postgres + OF gốc PES6VN: nhập patch → chuyển nhượng → sinh OF; đối chiếu với SquadRules của module OF.</summary>
public class ClubsFlowTests
{
    private const int Arsenal = 0, AstonVilla = 1, Havertz = 144, Raya = 1598, Gyokeres = 965;
    private static readonly Guid Season = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private sealed record Ctx(TestDb T, ImportPatchResult Import, Guid Window);

    private static async Task<Ctx> SetupAsync(TestDb t, params int[] extraClubs)
    {
        await using var db = t.Context();
        var import = await new ImportPatchService(db, t.Store, t.Clock).ImportAsync("PES6VN 25-26", Env.OrigOptionFile, Season,
            [new LeagueClub(Arsenal, "Arsenal"), new LeagueClub(AstonVilla), .. extraClubs.Select(c => new LeagueClub(c))]);
        var w = new TransferWindow
        {
            SeasonId = Season, OpensAt = t.Clock.GetUtcNow().AddHours(-1), ClosesAt = t.Clock.GetUtcNow().AddDays(1), Status = TransferWindowStatus.Open,
        };
        db.TransferWindows.Add(w);
        await db.SaveChangesAsync();
        await new EconomyService(db, t.Clock).RecalculatePricesAsync(Season);
        return new(t, import, w.Id);
    }

    private static Task<long> PriceAsync(ClubsDbContext db, Guid player) =>
        db.PlayerPrices.Where(p => p.PlayerId == player && p.SeasonId == Season).Select(p => p.Price).SingleAsync();

    private static async Task FundAsync(ClubsDbContext db, Guid club, long amount, DateTimeOffset at)
    {
        db.Ledger.Add(new ClubLedgerEntry { ClubId = club, Amount = amount, Reason = LedgerReason.Adjustment, At = at });
        await db.SaveChangesAsync();
    }

    private static async Task SetHumanAsync(ClubsDbContext db, Guid club)
    {
        (await db.TeamAssignments.SingleAsync(a => a.ClubId == club && a.To == null)).ControllerType = ControllerType.Human;
        await db.SaveChangesAsync();
    }

    private static async Task<(Guid Club, Guid Player)> IdsAsync(ClubsDbContext db, Guid patch, int pesClub, int pesPlayer) =>
        (await db.ClubPesMappings.Where(m => m.PatchVersionId == patch && m.PesClubIndex == pesClub).Select(m => m.ClubId).SingleAsync(),
         await db.PlayerPesMappings.Where(m => m.PatchVersionId == patch && m.PesPlayerId == pesPlayer).Select(m => m.PlayerId).SingleAsync());

    private static async Task<OptionFileImage> BuildAsync(Ctx c)
    {
        await using var db = c.T.Context();
        var r = await new RoundOptionFileService(db, c.T.Store, c.T.Clock).BuildAsync(c.Import.PatchVersionId, null);
        Assert.True(r.Ok, string.Join("; ", r.Issues.Select(i => i.Message)));
        return OptionFileCodec.Decode(await c.T.Store.ReadAsync(r.StoragePath!));
    }

    [DbFact]
    public async Task Import_matches_option_file()
    {
        await using var t = await TestDb.CreateAsync();
        var c = await SetupAsync(t);
        var of = Env.OrigOptionFile;
        var image = OptionFileCodec.Decode(of);
        var expected = image.GetSquad(Arsenal).Players.Count + image.GetSquad(AstonVilla).Players.Count;
        Assert.Equal(2, c.Import.Clubs);
        Assert.Equal(expected, c.Import.SquadPlayers);
        Assert.True(c.Import.FreeAgents > 0);

        await using var db = t.Context();
        var (_, raya) = await IdsAsync(db, c.Import.PatchVersionId, Arsenal, Raya);
        var p = await db.Players.SingleAsync(x => x.Id == raya);
        Assert.Equal(Position.GK, p.Position);
        var attrs = await db.PlayerAttributes.SingleAsync(a => a.PlayerId == raya && a.SeasonId == Season);
        Assert.Equal(image.Get(Raya, PlayerField.GoalKeeping), attrs.Values["GOAL KEEPING"]);
        Assert.Equal(PlayerField.Verified.Count, attrs.Values.Count);
        Assert.Equal("Arsenal", (await db.Clubs.SingleAsync(x => x.Name == "Arsenal")).Name);

        // Chưa giao dịch: OF sinh ra giữ nguyên squad 2 CLB (trừ số áo đã chuẩn hoá).
        var built = await BuildAsync(c);
        foreach (var club in new[] { Arsenal, AstonVilla })
            Assert.Equal(image.GetSquad(club).Players.Select(s => s.PlayerId), built.GetSquad(club).Players.Select(s => s.PlayerId));
        if (c.Import.ShirtChanges.Count == 0) Assert.Equal(of, OptionFileCodec.Encode(built));
    }

    [DbFact]
    public async Task Transfer_between_clubs_matches_option_file_rules()
    {
        await using var t = await TestDb.CreateAsync();
        var c = await SetupAsync(t);
        Guid havertz, villa;
        long price;
        await using (var db = t.Context())
        {
            (villa, havertz) = await IdsAsync(db, c.Import.PatchVersionId, AstonVilla, Havertz);
            price = await PriceAsync(db, havertz);
            await FundAsync(db, villa, price + 1000, t.Clock.GetUtcNow());
            var r = await new TransferService(db, t.Clock).BuyAsync(c.Window, havertz, villa, price, 30, 0);   // Arsenal là BOT → đúng giá
            Assert.True(r.Ok, r.Error.ToString());
        }

        // Oracle: cùng giao dịch làm thẳng trên OF bằng SquadRules (đã kiểm trong game 01/11).
        var oracle = OptionFileCodec.Decode(Env.OrigOptionFile);
        foreach (var club in new[] { Arsenal, AstonVilla })
            oracle.SetSquad(SquadRules.NormalizeShirtNumbers(oracle.GetSquad(club)).Squad);   // như lúc nhập patch
        var (from, to) = new SquadRules(oracle).Transfer(Arsenal, AstonVilla, Havertz);
        Assert.True(from.Ok && to.Ok);
        var built = await BuildAsync(c);
        Assert.Equal(from.Squad.Players, built.GetSquad(Arsenal).Players);
        Assert.Equal(to.Squad.Players, built.GetSquad(AstonVilla).Players);

        await using (var db = t.Context())
        {
            var svc = new TransferService(db, t.Clock);
            Assert.Equal(1000, await svc.BalanceAsync(villa));
            var arsenal = (await IdsAsync(db, c.Import.PatchVersionId, Arsenal, Havertz)).Club;
            Assert.Equal(price, await svc.BalanceAsync(arsenal));
            Assert.Equal(villa, (await db.Contracts.SingleAsync(x => x.PlayerId == havertz && x.Status == ContractStatus.Active)).ClubId);
            Assert.Single(db.Contracts.Where(x => x.PlayerId == havertz && x.Status == ContractStatus.Ended));
            var tr = await db.Transfers.SingleAsync();
            Assert.Equal((TransferStatus.Completed, from.NeedsLineupReview), (tr.Status, tr.ResultingLineupReview));
            Assert.Equal(1, (await db.Clubs.FindAsync(villa))!.SquadVersion);
        }
    }

    [DbFact]
    public async Task Rejections_are_recorded_and_change_nothing()
    {
        await using var t = await TestDb.CreateAsync();
        var c = await SetupAsync(t);
        await using var db = t.Context();
        var (villa, havertz) = await IdsAsync(db, c.Import.PatchVersionId, AstonVilla, Havertz);
        var (arsenal, raya) = await IdsAsync(db, c.Import.PatchVersionId, Arsenal, Raya);
        var svc = new TransferService(db, t.Clock);

        Assert.Equal(TransferRejectReason.InsufficientBudget, (await svc.BuyAsync(c.Window, havertz, villa, await PriceAsync(db, havertz), 30, 0)).Error);
        Assert.Equal(TransferRejectReason.SameClub, (await svc.BuyAsync(c.Window, havertz, arsenal, 0, 30, 0)).Error);
        Assert.Equal(TransferRejectReason.BelowMinGoalkeepers,
            (await new TransferService(db, t.Clock, new() { MinGoalkeepers = 99 }).ReleaseAsync(c.Window, raya)).Error);
        Assert.Equal(TransferRejectReason.BelowMinSquadSize,
            (await new TransferService(db, t.Clock, new() { MinSquadSize = 99 }).ReleaseAsync(c.Window, havertz)).Error);
        t.Clock.Advance(TimeSpan.FromDays(2));
        Assert.Equal(TransferRejectReason.WindowClosed, (await svc.BuyAsync(c.Window, havertz, villa, 0, 30, 0)).Error);

        Assert.Equal(5, await db.Transfers.CountAsync(x => x.Status == TransferStatus.Rejected));
        Assert.Equal(0, await db.Ledger.CountAsync());
        Assert.Equal(arsenal, (await db.SquadMemberships.SingleAsync(m => m.PlayerId == havertz && m.LeftAt == null)).ClubId);
    }

    [DbFact]
    public async Task Legend_clubs_are_out_of_the_transfer_market()
    {
        // PO 20/11: đội huyền thoại "… Classics" — không mua/bán/thanh lý, không trinh sát; đội huyền thoại cũng không mua ai.
        await using var t = await TestDb.CreateAsync();
        var c = await SetupAsync(t);
        await using var db = t.Context();
        var (villa, havertz) = await IdsAsync(db, c.Import.PatchVersionId, AstonVilla, Havertz);
        var (arsenal, raya) = await IdsAsync(db, c.Import.PatchVersionId, Arsenal, Raya);
        (await db.Clubs.SingleAsync(x => x.Id == arsenal)).Name = "Pes Classics";
        await db.SaveChangesAsync();
        var price = await PriceAsync(db, havertz);
        await FundAsync(db, villa, price * 10, t.Clock.GetUtcNow());
        await FundAsync(db, arsenal, price * 10, t.Clock.GetUtcNow());
        var villaPlayer = await db.SquadMemberships.Where(m => m.ClubId == villa && m.LeftAt == null).Select(m => m.PlayerId).FirstAsync();
        var svc = new TransferService(db, t.Clock);

        Assert.True(await LegendClubs.HasPlayerAsync(db, havertz));
        Assert.False(await LegendClubs.HasPlayerAsync(db, villaPlayer));
        Assert.Equal(TransferRejectReason.LegendClub, (await svc.BuyAsync(c.Window, havertz, villa, price, 30, 0)).Error);
        Assert.Equal(TransferRejectReason.LegendClub, (await svc.ReleaseAsync(c.Window, raya)).Error);
        Assert.Equal(TransferRejectReason.LegendClub, (await svc.BuyAsync(c.Window, villaPlayer, arsenal, await PriceAsync(db, villaPlayer), 30, 0)).Error);
        Assert.Equal(LegendClubs.Message, await new YouthService(db, t.Clock).ScoutAsync(villa, havertz));
        Assert.Equal(arsenal, (await db.SquadMemberships.SingleAsync(m => m.PlayerId == havertz && m.LeftAt == null)).ClubId);
        Assert.Equal(0, await db.ScoutReports.CountAsync());
    }

    [DbFact]
    public async Task Release_then_sign_free_agent_and_shirt_number()
    {
        await using var t = await TestDb.CreateAsync();
        var c = await SetupAsync(t);
        await using (var db = t.Context())
        {
            var (villa, havertz) = await IdsAsync(db, c.Import.PatchVersionId, AstonVilla, Havertz);
            var svc = new TransferService(db, t.Clock);
            Assert.True((await svc.ReleaseAsync(c.Window, havertz)).Ok);
            Assert.False(await db.SquadMemberships.AnyAsync(m => m.PlayerId == havertz && m.LeftAt == null));
            var price = await PriceAsync(db, havertz);
            await FundAsync(db, villa, price, t.Clock.GetUtcNow());
            Assert.Equal(TransferRejectReason.FeeOutOfRange, (await svc.BuyAsync(c.Window, havertz, villa, price - 1000, 30, 0)).Error);   // tự do = đúng giá
            Assert.True((await svc.BuyAsync(c.Window, havertz, villa, price, 30, 0)).Ok);   // tự do → Villa

            var taken = await db.SquadMemberships.Where(m => m.ClubId == villa && m.LeftAt == null && m.PlayerId != havertz).Select(m => m.ShirtNumber).FirstAsync();
            Assert.Equal(TransferRejectReason.ShirtNumberTaken, await svc.ChangeShirtNumberAsync(villa, havertz, taken));
            var free = Enumerable.Range(1, 99).Except(await db.SquadMemberships.Where(m => m.ClubId == villa && m.LeftAt == null).Select(m => m.ShirtNumber).ToListAsync()).Max();
            Assert.Null(await svc.ChangeShirtNumberAsync(villa, havertz, free));
        }
        var built = await BuildAsync(c);
        Assert.DoesNotContain(built.GetSquad(Arsenal).Players, p => p.PlayerId == Havertz);
        Assert.Equal(Havertz, built.GetSquad(AstonVilla).Players[^1].PlayerId);
    }

    [DbFact]
    public async Task Concurrent_transfers_from_same_club_conflict()
    {
        await using var t = await TestDb.CreateAsync();
        var c = await SetupAsync(t);
        await using var db1 = t.Context();
        await using var db2 = t.Context();
        var (_, havertz) = await IdsAsync(db1, c.Import.PatchVersionId, AstonVilla, Havertz);
        var (_, gyokeres) = await IdsAsync(db1, c.Import.PatchVersionId, Arsenal, Gyokeres);
        // Cả hai đọc squad Arsenal cùng phiên bản: nạp trước rồi mới ghi.
        await db2.Clubs.LoadAsync();
        Assert.True((await new TransferService(db1, t.Clock).ReleaseAsync(c.Window, havertz)).Ok);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => new TransferService(db2, t.Clock).ReleaseAsync(c.Window, gyokeres));
    }

    [DbFact]
    public async Task Invalid_squad_is_not_exported()
    {
        await using var t = await TestDb.CreateAsync();
        var c = await SetupAsync(t);
        await using var db = t.Context();
        // Phá dữ liệu trực tiếp (bỏ qua service): Arsenal mất hết thủ môn.
        var gks = await (from m in db.SquadMemberships join p in db.Players on m.PlayerId equals p.Id
                         join cm in db.ClubPesMappings on m.ClubId equals cm.ClubId
                         where cm.PesClubIndex == Arsenal && p.Position == Position.GK && m.LeftAt == null select m).ToListAsync();
        gks.ForEach(m => m.LeftAt = t.Clock.GetUtcNow());
        await db.SaveChangesAsync();
        var r = await new RoundOptionFileService(db, t.Store, t.Clock).BuildAsync(c.Import.PatchVersionId, null);
        Assert.False(r.Ok);
        Assert.Contains(r.Issues, i => i.Code == "TOO_FEW_GK");
        Assert.Null(r.StoragePath);
        Assert.Equal(OptionFileBuildStatus.Failed, (await db.OptionFileBuilds.SingleAsync()).Status);
    }

    private const int Koln = 61, DongAThanhHoa = 138;

    [DbFact]
    public async Task Import_adds_free_goalkeepers_to_clubs_short_of_gk()
    {
        await using var t = await TestDb.CreateAsync();
        var c = await SetupAsync(t, Koln, DongAThanhHoa);   // Köln 1 GK, Đông Á Thanh Hoá 0 GK trong OF gốc
        var added = c.Import.AddedGoalkeepers;
        Assert.Empty(c.Import.GoalkeeperShortages);
        Assert.Equal(3, added.Count);
        Assert.Equal(added.Select(a => a.GoalKeeping).Order(), added.Select(a => a.GoalKeeping));   // thấp nhất trước
        Assert.All(added, a => Assert.True(a.GoalKeeping >= 70));
        Assert.Equal(3, added.Select(a => a.PlayerId).Distinct().Count());

        var image = OptionFileCodec.Decode(Env.OrigOptionFile);
        var built = await BuildAsync(c);   // validator (≥ 2 GK) phải qua
        foreach (var club in new[] { Koln, DongAThanhHoa })
        {
            var squad = built.GetSquad(club).Players;
            Assert.Equal(image.GetSquad(club).Players.Select(p => p.PlayerId), squad.Take(image.GetSquad(club).Players.Count).Select(p => p.PlayerId));
            Assert.Equal(2, squad.Count(p => built.GetPosition(p.PlayerId) == Position.GK));
            Assert.Equal(squad.Count, squad.Select(p => p.ShirtNumber).Distinct().Count());
        }
        await using var db = t.Context();
        Assert.Equal(c.Import.FreeAgents, await db.Players.CountAsync(p => !db.SquadMemberships.Any(m => m.PlayerId == p.Id)));
        // GK tự do ≥ 70 thấp nhất toàn OF phải là người đầu tiên được chọn.
        var lowest = Enumerable.Range(1, OptionFileImage.PlayerCount - 1)
            .Where(id => image.GetPosition(id) == Position.GK && !Enumerable.Range(0, OptionFileImage.ClubCount).Any(cl => image.GetSquad(cl).Players.Any(p => p.PlayerId == id))
                && image.GetPlayerName(id).Trim() != "" && image.Get(id, PlayerField.GoalKeeping) >= 70 && !OptionFileImage.IsHiddenPlayerId(id))
            .Min(id => image.Get(id, PlayerField.GoalKeeping));
        Assert.Equal(lowest, added[0].GoalKeeping);
        var gkIds = added.Select(a => a.PlayerId).ToList();
        Assert.All(await db.Players.Where(p => gkIds.Contains(p.Id)).ToListAsync(), p => Assert.Null(p.OriginClubId));   // gốc tự do
        // GK bổ sung không lấy từ dải ẩn/ML; dải ẩn vẫn được nhập làm cầu thủ tự do (thị trường ẩn theo luật).
        var addedPes = await db.PlayerPesMappings.Where(m => gkIds.Contains(m.PlayerId)).Select(m => m.PesPlayerId).ToListAsync();
        Assert.DoesNotContain(addedPes, OptionFileImage.IsHiddenPlayerId);
        Assert.Contains(await db.PlayerPesMappings.Select(m => m.PesPlayerId).ToListAsync(), OptionFileImage.IsHiddenPlayerId);
    }

    [DbFact]
    public async Task Market_visibility_rule_override_and_retired_hiding()
    {
        await using var t = await TestDb.CreateAsync();
        var c = await SetupAsync(t);
        var image = OptionFileCodec.Decode(Env.OrigOptionFile);
        var nationals = image.GetNationalTeamPlayerIds();
        Assert.NotEmpty(nationals);

        await using var db = t.Context();
        var svc = new MarketVisibilityService(db, t.Store);
        var hidden = await svc.HideRetiredAsync(c.Import.PatchVersionId);
        Assert.NotEmpty(hidden);
        Assert.All(hidden, id => Assert.False(nationals.Contains(id) || OptionFileImage.IsHiddenPlayerId(id)));
        var mapped = await db.PlayerPesMappings.ToDictionaryAsync(m => m.PesPlayerId, m => m.PlayerId);
        Assert.False((await db.Players.FindAsync(mapped[hidden[0]]))!.MarketVisibility);
        // Tự do thuộc đội tuyển → vẫn theo luật (null).
        var free = await db.PlayerPesMappings.Where(m => m.OriginPesClubIndex == null).Select(m => m.PesPlayerId).ToListAsync();
        Assert.Null((await db.Players.FindAsync(mapped[free.First(nationals.Contains)]))!.MarketVisibility);

        // Admin ép hiện người dải ẩn; chạy lại tiêu chí A không ghi đè.
        var ml = free.First(OptionFileImage.IsHiddenPlayerId);
        Assert.False(MarketVisibilityService.IsVisible(null, ml, inClub: false));
        Assert.True(MarketVisibilityService.IsVisible(null, ml, inClub: true));
        Assert.Equal(1, await svc.SetByPesIdsAsync(c.Import.PatchVersionId, [ml], true));
        await svc.SetAsync(mapped[hidden[0]], true);
        Assert.Empty(await svc.HideRetiredAsync(c.Import.PatchVersionId));
        Assert.True((await db.Players.FindAsync(mapped[hidden[0]]))!.MarketVisibility);
    }

    [DbFact]
    public async Task Economy_hard_cap_contract_length_and_round_settlement()
    {
        await using var t = await TestDb.CreateAsync();
        var c = await SetupAsync(t);
        await using var db = t.Context();
        var (villa, havertz) = await IdsAsync(db, c.Import.PatchVersionId, AstonVilla, Havertz);
        var eco = new EconomyService(db, t.Clock);
        var svc = new TransferService(db, t.Clock);
        await eco.SetSoftWageCapAsync(Season, 1000);
        var price = await PriceAsync(db, havertz);
        await FundAsync(db, villa, price, t.Clock.GetUtcNow());

        Assert.Equal(TransferRejectReason.InvalidContractLength, (await svc.BuyAsync(c.Window, havertz, villa, price, 9, 100)).Error);
        Assert.Equal(TransferRejectReason.HardCapExceeded, (await svc.BuyAsync(c.Window, havertz, villa, price, 30, 1401)).Error);
        Assert.True((await svc.BuyAsync(c.Window, havertz, villa, price, 30, 1100)).Ok);

        var matchday = Guid.NewGuid();
        var r = await eco.SettleRoundAsync(Season, matchday);   // hợp đồng nhập chưa đặt lương → payroll chỉ có Havertz
        Assert.Contains(new ClubSettlement(villa, 1100, 50), r.Clubs);
        Assert.Equal(-1150, await svc.BalanceAsync(villa));
        Assert.True((await eco.SettleRoundAsync(Season, matchday)).AlreadySettled);
        Assert.Equal(-1150, await svc.BalanceAsync(villa));
        Assert.Equal(1, (await db.Contracts.SingleAsync(x => x.PlayerId == havertz && x.Status == ContractStatus.Active)).MatchesPlayed);

        Assert.Equal(c.Import.SquadPlayers + c.Import.FreeAgents, await eco.RecalculatePricesAsync(Season));
        var before = (await db.PlayerPrices.SingleAsync(p => p.PlayerId == havertz)).Price;
        await eco.ApplyFormAsync(Season, new Dictionary<Guid, double> { [havertz] = 1 });
        var after = await db.PlayerPrices.AsNoTracking().SingleAsync(p => p.PlayerId == havertz);
        Assert.Equal(1.05, after.FormMultiplier, 6);
        Assert.True(after.Price > before && after.Overall > 0);
    }

    // ---- Kinh tế đợt 2 ----

    [DbFact]
    public async Task Import_records_origin_club_and_10_match_contracts()
    {
        await using var t = await TestDb.CreateAsync();
        var c = await SetupAsync(t);
        await using var db = t.Context();
        var (arsenal, havertz) = await IdsAsync(db, c.Import.PatchVersionId, Arsenal, Havertz);
        Assert.Equal(arsenal, (await db.Players.FindAsync(havertz))!.OriginClubId);
        Assert.Equal(Arsenal, (await db.PlayerPesMappings.SingleAsync(m => m.PlayerId == havertz)).OriginPesClubIndex);
        Assert.False(await db.PlayerPesMappings.AnyAsync(m => m.OriginPesClubIndex != null && !db.SquadMemberships.Any(s => s.PlayerId == m.PlayerId)));   // tự do thật
        Assert.True(await db.Contracts.AllAsync(x => x.MatchesTotal == 10 && x.Wage == null));
    }

    [DbFact]
    public async Task Fee_between_human_clubs_is_80_to_200_percent()
    {
        await using var t = await TestDb.CreateAsync();
        var c = await SetupAsync(t);
        await using var db = t.Context();
        var (villa, havertz) = await IdsAsync(db, c.Import.PatchVersionId, AstonVilla, Havertz);
        var (arsenal, _) = await IdsAsync(db, c.Import.PatchVersionId, Arsenal, Havertz);
        var price = await PriceAsync(db, havertz);
        await FundAsync(db, villa, 3 * price, t.Clock.GetUtcNow());
        var svc = new TransferService(db, t.Clock);
        Assert.Equal(TransferRejectReason.FeeOutOfRange, (await svc.BuyAsync(c.Window, havertz, villa, price * 3 / 2, 30, 0)).Error);   // Arsenal BOT → đúng giá
        await SetHumanAsync(db, arsenal);
        Assert.Equal(TransferRejectReason.FeeOutOfRange, (await svc.BuyAsync(c.Window, havertz, villa, price * 79 / 100, 30, 0)).Error);
        Assert.Equal(TransferRejectReason.FeeOutOfRange, (await svc.BuyAsync(c.Window, havertz, villa, price * 201 / 100, 30, 0)).Error);
        Assert.True((await svc.BuyAsync(c.Window, havertz, villa, price * 3 / 2, 30, 0)).Ok);
        Assert.Equal(price * 3 / 2, await svc.BalanceAsync(arsenal));
    }

    [DbFact]
    public async Task Season_start_sets_imported_wages_and_inverse_budgets()
    {
        await using var t = await TestDb.CreateAsync();
        var c = await SetupAsync(t);
        await using var db = t.Context();
        var (villa, havertz) = await IdsAsync(db, c.Import.PatchVersionId, AstonVilla, Havertz);
        var (arsenal, _) = await IdsAsync(db, c.Import.PatchVersionId, Arsenal, Havertz);
        var eco = new EconomyService(db, t.Clock);
        await eco.SetParametersAsync(Season, new(InitialBudgetFund: 100_000_000, BudgetExponent: 0.5));

        Assert.Equal(c.Import.SquadPlayers, await eco.SetImportedWagesAsync(Season));
        Assert.Equal((await PriceAsync(db, havertz)) / 100, (await db.Contracts.SingleAsync(x => x.PlayerId == havertz && x.Status == ContractStatus.Active)).Wage);

        var budgets = await eco.AllocateInitialBudgetsAsync(Season);
        Assert.Equal(100_000_000, budgets.Values.Sum());
        long Value(Guid club) => (from m in db.SquadMemberships join p in db.PlayerPrices on m.PlayerId equals p.PlayerId
                                  where m.ClubId == club && m.LeftAt == null select p.Price).Sum();
        var (weak, strong) = Value(villa) < Value(arsenal) ? (villa, arsenal) : (arsenal, villa);
        Assert.True(budgets[weak] > budgets[strong]);
        Assert.Empty(await eco.AllocateInitialBudgetsAsync(Season));   // 1 lần mỗi mùa
        Assert.Equal(budgets[weak], await new TransferService(db, t.Clock).BalanceAsync(weak));
    }

    /// <summary>Mùa đã chia lúc quỹ = 0 → bù bằng Adjustment, giữ dòng InitialBudget cũ, chỉ bù 1 lần.</summary>
    [DbFact]
    public async Task Initial_budget_top_up_adds_adjustments_once()
    {
        await using var t = await TestDb.CreateAsync();
        var c = await SetupAsync(t);
        await using var db = t.Context();
        var (villa, _) = await IdsAsync(db, c.Import.PatchVersionId, AstonVilla, Havertz);
        var eco = new EconomyService(db, t.Clock);
        Assert.Empty(await eco.TopUpInitialBudgetsAsync(Season));                  // chưa chia → không bù
        await eco.SetImportedWagesAsync(Season);
        Assert.All((await eco.AllocateInitialBudgetsAsync(Season)).Values, v => Assert.Equal(0, v));
        await eco.SetParametersAsync(Season, new(InitialBudgetFund: 100_000_000, BudgetExponent: 0.5));

        var topUp = await eco.TopUpInitialBudgetsAsync(Season);
        Assert.Equal(100_000_000, topUp.Values.Sum());
        Assert.Equal(topUp.Count, await db.Ledger.CountAsync(e => e.Reason == LedgerReason.InitialBudget));
        Assert.Equal(topUp.Count(x => x.Value != 0), await db.Ledger.CountAsync(e => e.Reason == LedgerReason.Adjustment && e.RefType == EconomyService.InitialBudgetTopUp));
        Assert.Empty(await eco.TopUpInitialBudgetsAsync(Season));
        Assert.Equal(topUp[villa], await new TransferService(db, t.Clock).BalanceAsync(villa));
    }

    [DbFact]
    public async Task Economy_preview_before_start_matches_start_and_writes_nothing()
    {
        await using var t = await TestDb.CreateAsync();
        await SetupAsync(t);
        await using var db = t.Context();
        db.Seasons.Add(new Season { Id = Season, Name = "Mùa thử", Number = 2, CreatedAt = t.Clock.GetUtcNow() });
        await db.SaveChangesAsync();
        await db.PlayerPrices.Where(p => p.SeasonId == Season).ExecuteDeleteAsync();   // như dev: mùa chưa bắt đầu chưa có giá
        var change = new SeasonEconomyChange(SoftWageCap: 2_000_000, WagePermille: 15, InitialBudgetFund: 100_000_000, BudgetExponent: 0.5);
        var (ledger, economies) = (await db.Ledger.CountAsync(), await db.SeasonEconomies.CountAsync());

        var preview = await new EconomyAdminService(db, t.Clock).PreviewAsync(Season, change);
        Assert.True(preview.AllocatesAtStart);
        Assert.Equal(100_000_000, preview.Clubs.Sum(c => c.Change));
        Assert.Contains(preview.Clubs, c => c.Payroll > 0);
        Assert.Equal((0, ledger, economies), (await db.PlayerPrices.CountAsync(), await db.Ledger.CountAsync(), await db.SeasonEconomies.CountAsync()));
        Assert.False(await db.Contracts.AnyAsync(c => c.Wage != null));
        var active = await db.Contracts.CountAsync(c => c.Status == ContractStatus.Active);
        Assert.Equal((active, active), (preview.Contracts, preview.Unwaged));

        // Như StartScheduledSeasonAsync.
        var eco = new EconomyService(db, t.Clock);
        await eco.SetParametersAsync(Season, change);
        await eco.RecalculatePricesAsync(Season);
        await eco.SetImportedWagesAsync(Season);
        var budgets = await eco.AllocateInitialBudgetsAsync(Season);
        var prices = await db.PlayerPrices.Where(p => p.SeasonId == Season).ToDictionaryAsync(p => p.PlayerId, p => p.Price);
        Assert.Equal(budgets.Count, preview.Clubs.Count);
        var started = await new EconomyAdminService(db, t.Clock).PreviewAsync(Season);
        Assert.Equal((active, 0), (started.Contracts, started.Unwaged));
        foreach (var c in preview.Clubs)
        {
            Assert.Equal((budgets[c.ClubId], budgets[c.ClubId]), (c.Budget, c.Change));
            Assert.Equal(await db.Contracts.Where(x => x.ClubId == c.ClubId && x.Status == ContractStatus.Active).SumAsync(x => x.Wage ?? 0), c.Payroll);
            var squad = await db.SquadMemberships.Where(m => m.ClubId == c.ClubId && m.LeftAt == null).Select(m => m.PlayerId).ToListAsync();
            Assert.Equal(squad.Sum(p => prices.GetValueOrDefault(p)), c.SquadValue);
        }
    }

    [DbFact]
    public async Task Economy_top_up_matches_preview_once_and_audits()
    {
        await using var t = await TestDb.CreateAsync();
        await SetupAsync(t);
        await using var db = t.Context();
        db.Seasons.Add(new Season { Id = Season, Name = "Mùa thử", Number = 2, Status = SeasonStatus.Active, CreatedAt = t.Clock.GetUtcNow() });
        await db.SaveChangesAsync();
        var eco = new EconomyService(db, t.Clock);
        await eco.SetImportedWagesAsync(Season);
        await eco.AllocateInitialBudgetsAsync(Season);                                    // bắt đầu mùa khi quỹ = 0
        var admin = new EconomyAdminService(db, t.Clock);
        var change = new SeasonEconomyChange(SoftWageCap: 2_000_000, InitialBudgetFund: 100_000_000, BudgetExponent: 0.5);

        var preview = await admin.PreviewAsync(Season, change);
        Assert.True(preview.CanTopUp);
        await Assert.ThrowsAsync<AdminRuleException>(() => admin.SetAsync(Season, new(WagePermille: 15), "admin"));
        await Assert.ThrowsAsync<AdminRuleException>(() => admin.SetAsync(Season, new(InitialBudgetFund: 1), "admin"));

        var diff = await admin.TopUpAsync(Season, change, "admin");
        Assert.Equal(100_000_000, diff.Values.Sum());
        Assert.All(preview.Clubs, c => Assert.Equal(c.Change, diff[c.ClubId]));
        Assert.Equal(1, await db.AdminAudit.CountAsync(a => a.Action == EconomyAdminService.TopUpAction));
        await Assert.ThrowsAsync<AdminRuleException>(() => admin.TopUpAsync(Season, change, "admin"));

        var after = await admin.PreviewAsync(Season);
        Assert.True(after.ToppedUp);
        Assert.All(after.Clubs, c => Assert.Equal((0, diff[c.ClubId]), (c.Change, c.Granted)));
        Assert.True(await admin.SetAsync(Season, new(SoftWageCap: 3_000_000), "admin"));
        Assert.False(await admin.SetAsync(Season, new(SoftWageCap: 3_000_000), "admin"));
        Assert.Equal(1, await db.AdminAudit.CountAsync(a => a.Action == EconomyAdminService.SetAction));
    }

    [Fact]
    public void Budget_allocation_exponent()
    {
        var equal = BudgetAllocation.Allocate<int>([(1, 100), (2, 400)], 1_000_000, 0);
        Assert.Equal((500_000, 500_000), (equal[1], equal[2]));
        var inverse = BudgetAllocation.Allocate<int>([(1, 100), (2, 400)], 1_000_000, 1);   // 4 : 1
        Assert.Equal((800_000, 200_000), (inverse[1], inverse[2]));
        var half = BudgetAllocation.Allocate<int>([(1, 100), (2, 400)], 1_000_000, 0.5);    // 2 : 1, dư dồn CLB yếu
        Assert.Equal((667_000, 333_000), (half[1], half[2]));
    }

    [DbFact]
    public async Task Expired_contract_returns_to_origin_or_becomes_free()
    {
        await using var t = await TestDb.CreateAsync();
        var c = await SetupAsync(t);
        await using var db = t.Context();
        var (villa, havertz) = await IdsAsync(db, c.Import.PatchVersionId, AstonVilla, Havertz);
        var (arsenal, _) = await IdsAsync(db, c.Import.PatchVersionId, Arsenal, Havertz);
        var freeAgent = await db.Players.Where(p => p.Position != Position.GK && !db.SquadMemberships.Any(m => m.PlayerId == p.Id)).Select(p => p.Id).FirstAsync();
        var svc = new TransferService(db, t.Clock);
        await FundAsync(db, villa, await PriceAsync(db, havertz) + await PriceAsync(db, freeAgent), t.Clock.GetUtcNow());
        Assert.True((await svc.BuyAsync(c.Window, havertz, villa, await PriceAsync(db, havertz), 10, 0)).Ok);
        Assert.True((await svc.BuyAsync(c.Window, freeAgent, villa, await PriceAsync(db, freeAgent), 10, 0)).Ok);
        var arsenalSize = await db.SquadMemberships.CountAsync(m => m.ClubId == arsenal && m.LeftAt == null);

        var eco = new EconomyService(db, t.Clock);
        RoundSettlementResult r = null!;
        for (var round = 1; round <= 10; round++)
        {
            r = await eco.SettleRoundAsync(Season, Guid.NewGuid());
            if (round == 7) Assert.Contains(r.RenewalReminders, x => x.PlayerId == havertz && x.MatchesLeft == 3);
            else Assert.DoesNotContain(r.RenewalReminders, x => x.PlayerId == havertz);
            if (round < 10) Assert.Empty(r.ExpiredContracts);
        }
        Assert.Contains(r.ExpiredContracts, x => x.PlayerId == havertz && x.Outcome == ExpiryOutcome.ReturnedToOrigin && x.NewClubId == arsenal);
        Assert.Contains(r.ExpiredContracts, x => x.PlayerId == freeAgent && x.Outcome == ExpiryOutcome.BecameFree && x.NewClubId == null);
        // Hợp đồng nhập 10 trận của cầu thủ đang ở CLB gốc → hợp đồng mới tại chỗ.
        Assert.Equal(c.Import.SquadPlayers - 1, r.ExpiredContracts.Count(x => x.Outcome == ExpiryOutcome.RenewedAtOrigin));

        Assert.Equal(arsenal, (await db.SquadMemberships.SingleAsync(m => m.PlayerId == havertz && m.LeftAt == null)).ClubId);
        Assert.Equal(arsenalSize + 1, await db.SquadMemberships.CountAsync(m => m.ClubId == arsenal && m.LeftAt == null));
        Assert.False(await db.SquadMemberships.AnyAsync(m => m.PlayerId == freeAgent && m.LeftAt == null));
        Assert.False(await db.Contracts.AnyAsync(x => x.PlayerId == freeAgent && x.Status == ContractStatus.Active));
        var back = await db.Contracts.SingleAsync(x => x.PlayerId == havertz && x.Status == ContractStatus.Active);
        Assert.Equal((arsenal, 10, 0), (back.ClubId, back.MatchesTotal!.Value, back.MatchesPlayed));
        Assert.Equal(ContractEndReason.ReturnedToOrigin, (await db.Contracts.SingleAsync(x => x.PlayerId == havertz && x.ClubId == villa)).EndReason);
        await BuildAsync(c);   // OF vòng kế hợp lệ
    }

    [DbFact]
    public async Task Expiry_auto_renews_when_leaving_breaks_squad_rules()
    {
        await using var t = await TestDb.CreateAsync();
        var c = await SetupAsync(t);
        await using var db = t.Context();
        var (villa, havertz) = await IdsAsync(db, c.Import.PatchVersionId, AstonVilla, Havertz);
        await FundAsync(db, villa, await PriceAsync(db, havertz), t.Clock.GetUtcNow());
        Assert.True((await new TransferService(db, t.Clock).BuyAsync(c.Window, havertz, villa, await PriceAsync(db, havertz), 10, 777)).Ok);
        var eco = new EconomyService(db, t.Clock, squadOptions: new() { MinSquadSize = 99 });
        RoundSettlementResult r = null!;
        for (var round = 1; round <= 10; round++) r = await eco.SettleRoundAsync(Season, Guid.NewGuid());
        Assert.Contains(r.ExpiredContracts, x => x.PlayerId == havertz && x.Outcome == ExpiryOutcome.AutoRenewed && x.NewClubId == villa);
        var renewed = await db.Contracts.SingleAsync(x => x.PlayerId == havertz && x.Status == ContractStatus.Active);
        Assert.Equal((villa, 10, 777L), (renewed.ClubId, renewed.MatchesTotal!.Value, renewed.Wage!.Value));
    }

    [DbFact]
    public async Task Renewal_needs_origin_owner_consent()
    {
        await using var t = await TestDb.CreateAsync();
        var c = await SetupAsync(t);
        await using var db = t.Context();
        var (villa, havertz) = await IdsAsync(db, c.Import.PatchVersionId, AstonVilla, Havertz);
        var (arsenal, gyokeres) = await IdsAsync(db, c.Import.PatchVersionId, Arsenal, Gyokeres);
        var (_, raya) = await IdsAsync(db, c.Import.PatchVersionId, Arsenal, Raya);
        var price = await PriceAsync(db, havertz);
        await FundAsync(db, villa, 3 * price + await PriceAsync(db, gyokeres), t.Clock.GetUtcNow());
        var svc = new TransferService(db, t.Clock);
        Assert.True((await svc.BuyAsync(c.Window, havertz, villa, price, 10, 0)).Ok);
        Assert.True((await svc.BuyAsync(c.Window, gyokeres, villa, await PriceAsync(db, gyokeres), 10, 0)).Ok);
        var contracts = new ContractService(db, t.Clock);

        // CLB gốc là BOT → tự đồng ý, lương = 1% giá.
        var auto = await contracts.RequestRenewalAsync(Season, gyokeres, 20, 0);
        Assert.Equal((true, RenewalStatus.Completed), (auto.Ok, auto.Status));
        var gc = await db.Contracts.SingleAsync(x => x.PlayerId == gyokeres && x.Status == ContractStatus.Active);
        Assert.Equal((20, await PriceAsync(db, gyokeres) / 100), (gc.MatchesTotal!.Value, gc.Wage!.Value));

        await SetHumanAsync(db, arsenal);
        Assert.Equal(RenewalRejectReason.FeeOutOfRange, (await contracts.RequestRenewalAsync(Season, havertz, 20, price + 1000)).Error);   // > 100% giá
        Assert.Equal(RenewalRejectReason.InvalidContractLength, (await contracts.RequestRenewalAsync(Season, havertz, 61, 0)).Error);
        var declined = await contracts.RequestRenewalAsync(Season, havertz, 20, 0);
        Assert.Equal(RenewalStatus.Pending, declined.Status);
        Assert.Equal(RenewalRejectReason.AlreadyPending, (await contracts.RequestRenewalAsync(Season, havertz, 30, 0)).Error);
        Assert.Equal(RenewalStatus.Rejected, (await contracts.DecideAsync(declined.RenewalId!.Value, false)).Status);

        var paid = await contracts.RequestRenewalAsync(Season, havertz, 30, price / 2);
        Assert.Equal(RenewalStatus.Pending, paid.Status);
        Assert.Equal(RenewalStatus.Completed, (await contracts.DecideAsync(paid.RenewalId!.Value, true)).Status);
        Assert.Equal(price / 2, await svc.BalanceAsync(arsenal) - price - await PriceAsync(db, gyokeres));
        Assert.Equal(30, (await db.Contracts.SingleAsync(x => x.PlayerId == havertz && x.Status == ContractStatus.Active)).MatchesTotal);
        Assert.Equal(RenewalRejectReason.NotPending, (await contracts.DecideAsync(paid.RenewalId!.Value, true)).Error);

        // Cầu thủ đang ở CLB gốc: không ai nhận phí → phí phải 0, tự đồng ý.
        Assert.Equal(RenewalRejectReason.FeeOutOfRange, (await contracts.RequestRenewalAsync(Season, raya, 20, 1000)).Error);
        Assert.Equal(RenewalStatus.Completed, (await contracts.RequestRenewalAsync(Season, raya, 20, 0)).Status);
    }

    [DbFact]
    public async Task Offer_counter_instalments_and_sell_on()
    {
        await using var t = await TestDb.CreateAsync();
        var c = await SetupAsync(t);
        await using var db = t.Context();
        var (villa, havertz) = await IdsAsync(db, c.Import.PatchVersionId, AstonVilla, Havertz);
        var (arsenal, _) = await IdsAsync(db, c.Import.PatchVersionId, Arsenal, Havertz);
        await SetHumanAsync(db, arsenal);
        await SetHumanAsync(db, villa);
        var price = await PriceAsync(db, havertz);
        await FundAsync(db, villa, 3 * price, t.Clock.GetUtcNow());
        var svc = new TransferOfferService(db, t.Clock);

        Assert.False((await svc.MakeAsync(c.Window, new(havertz, villa, OfferKind.Permanent, price * 79 / 100, 30, 0))).Ok);
        var made = await svc.MakeAsync(c.Window, new(havertz, villa, OfferKind.Permanent, price, 30, 0, SellOnPercent: 20, Instalments: 2));
        Assert.True(made.Ok, made.Error);
        Assert.False((await svc.MakeAsync(c.Window, new(havertz, villa, OfferKind.Permanent, price, 30, 0))).Ok);   // trùng
        Assert.False((await svc.RespondAsync(made.OfferId!.Value, villa, OfferReply.Accept)).Ok);   // không phải CLB bán
        var counter = price * 3 / 2;
        Assert.Equal(OfferStatus.Countered, (await svc.RespondAsync(made.OfferId!.Value, arsenal, OfferReply.Counter, counter)).Status);
        var done = await svc.BuyerReplyAsync(made.OfferId!.Value, villa, acceptCounter: true);
        Assert.True(done.Ok, done.Error);
        var upfront = counter - counter / 2;
        Assert.Equal(upfront, await new TransferService(db, t.Clock).BalanceAsync(arsenal));
        Assert.Equal(3 * price - upfront, await new TransferService(db, t.Clock).BalanceAsync(villa));

        await new EconomyService(db, t.Clock).SettleRoundAsync(Season, Guid.NewGuid());   // kỳ 2
        Assert.Equal(counter, await db.Ledger.Where(l => l.ClubId == arsenal && l.Reason == LedgerReason.TransferOut).SumAsync(l => l.Amount));
        Assert.Equal(2, (await db.Transfers.SingleAsync(x => x.OfferId == made.OfferId)).InstalmentsPaid);

        // Arsenal mua lại: Villa trả 20% phí cho Arsenal (CLB bán lần trước).
        await FundAsync(db, arsenal, 3 * price, t.Clock.GetUtcNow());
        Assert.True((await new TransferService(db, t.Clock).BuyAsync(c.Window, havertz, arsenal, price, 30, 0)).Ok);
        Assert.Equal(price * 20 / 100, await db.Ledger.Where(l => l.ClubId == arsenal && l.Reason == LedgerReason.SellOn).SumAsync(l => l.Amount));
    }

    [DbFact]
    public async Task Bot_buyer_answers_counter_by_walk_away()
    {
        await using var t = await TestDb.CreateAsync();
        var c = await SetupAsync(t);
        await using var db = t.Context();
        var (villa, havertz) = await IdsAsync(db, c.Import.PatchVersionId, AstonVilla, Havertz);
        var (arsenal, gyokeres) = await IdsAsync(db, c.Import.PatchVersionId, Arsenal, Gyokeres);
        await SetHumanAsync(db, arsenal);   // Villa vẫn là BOT
        var (p1, p2) = (await PriceAsync(db, havertz), await PriceAsync(db, gyokeres));
        await FundAsync(db, villa, 3 * (p1 + p2), t.Clock.GetUtcNow());
        var svc = new TransferOfferService(db, t.Clock);

        var a = await svc.MakeAsync(c.Window, new(havertz, villa, OfferKind.Permanent, p1, 60, 0));
        Assert.True(a.Ok, a.Error);
        var ok = await svc.RespondAsync(a.OfferId!.Value, arsenal, OfferReply.Counter, BotMarketRules.WalkAway(p1, a.OfferId.Value));
        Assert.Equal(OfferStatus.Completed, ok.Status);   // trả giá đúng mức trần → BOT đồng ý ngay
        Assert.Equal(villa, (await db.SquadMemberships.SingleAsync(m => m.PlayerId == havertz && m.LeftAt == null)).ClubId);

        var b = await svc.MakeAsync(c.Window, new(gyokeres, villa, OfferKind.Permanent, p2, 60, 0));
        Assert.True(b.Ok, b.Error);
        var over = BotMarketRules.WalkAway(p2, b.OfferId!.Value) + 10_000;
        Assert.Equal(OfferStatus.Withdrawn, (await svc.RespondAsync(b.OfferId.Value, arsenal, OfferReply.Counter, over)).Status);
        Assert.Equal(arsenal, (await db.SquadMemberships.SingleAsync(m => m.PlayerId == gyokeres && m.LeftAt == null)).ClubId);
    }

    [DbFact]
    public async Task Release_clause_player_refusal_and_loan_return()
    {
        await using var t = await TestDb.CreateAsync();
        var c = await SetupAsync(t);
        await using var db = t.Context();
        var (villa, havertz) = await IdsAsync(db, c.Import.PatchVersionId, AstonVilla, Havertz);
        var (arsenal, gyokeres) = await IdsAsync(db, c.Import.PatchVersionId, Arsenal, Gyokeres);
        await SetHumanAsync(db, arsenal);
        await SetHumanAsync(db, villa);
        var price = await PriceAsync(db, havertz);
        await FundAsync(db, villa, 3 * price, t.Clock.GetUtcNow());
        (await db.Contracts.SingleAsync(x => x.PlayerId == havertz && x.Status == ContractStatus.Active)).Wage = 100;
        await db.SaveChangesAsync();
        var svc = new TransferOfferService(db, t.Clock);

        Assert.NotNull(await svc.SetReleaseClauseAsync(arsenal, havertz, price * 3));   // > 200%
        Assert.Null(await svc.SetReleaseClauseAsync(arsenal, havertz, price * 6 / 5));
        var refused = await svc.MakeAsync(c.Window, new(havertz, villa, OfferKind.Permanent, price * 6 / 5, 30, 50));
        Assert.Equal(OfferStatus.Failed, refused.Status);   // lương < 90%
        var clause = await svc.MakeAsync(c.Window, new(havertz, villa, OfferKind.Permanent, price * 6 / 5, 30, 100));
        Assert.Equal(OfferStatus.Completed, clause.Status);   // đủ phí giải phóng → không cần Arsenal đồng ý
        Assert.Equal(villa, (await db.SquadMemberships.SingleAsync(m => m.PlayerId == havertz && m.LeftAt == null)).ClubId);

        // Mượn Gyökeres 10 trận: không bán tiếp được, hết hạn về Arsenal.
        var loan = await svc.MakeAsync(c.Window, new(gyokeres, villa, OfferKind.Loan, 0, 10, 0));
        Assert.True(loan.Ok, loan.Error);
        Assert.Equal(OfferStatus.Completed, (await svc.RespondAsync(loan.OfferId!.Value, arsenal, OfferReply.Accept)).Status);
        var lc = await db.Contracts.SingleAsync(x => x.PlayerId == gyokeres && x.Status == ContractStatus.Active);
        Assert.Equal((villa, arsenal), (lc.ClubId, lc.LoanFromClubId));
        Assert.Equal(TransferRejectReason.OnLoan, (await new TransferService(db, t.Clock).ReleaseAsync(c.Window, gyokeres)).Error);
        lc.MatchesPlayed = 9;
        await db.SaveChangesAsync();
        var r = await new EconomyService(db, t.Clock).SettleRoundAsync(Season, Guid.NewGuid());
        Assert.Contains(r.ExpiredContracts, e => e.PlayerId == gyokeres && e.Outcome == ExpiryOutcome.LoanReturned);
        Assert.Equal(arsenal, (await db.SquadMemberships.SingleAsync(m => m.PlayerId == gyokeres && m.LeftAt == null)).ClubId);
    }

    [DbFact]
    public async Task Training_every_five_rounds_grows_young_and_respects_cap()
    {
        await using var t = await TestDb.CreateAsync();
        var c = await SetupAsync(t);
        await using var db = t.Context();
        var (arsenal, _) = await IdsAsync(db, c.Import.PatchVersionId, Arsenal, Havertz);
        var svc = new TrainingService(db, t.Clock);
        await svc.SetAsync(arsenal, TrainingFocus.Attack, TrainingIntensity.Hard);
        Assert.Equal(0, await svc.ApplyAsync(Season, 3));
        Assert.True(await svc.ApplyAsync(Season, 5) > 0);
        var changes = await db.AttributeChanges.Where(x => x.Source == AttributeChangeSource.Training).ToListAsync();
        Assert.Contains(changes, x => x.Delta > 0 && x.Field == "ATTACK");
        for (var r = 10; r <= 40; r += 5) await svc.ApplyAsync(Season, r);
        var sums = await db.AttributeChanges.Where(x => x.Source != AttributeChangeSource.Admin)
            .GroupBy(x => new { x.PlayerId, x.Field }).Select(g => g.Sum(x => x.Delta)).ToListAsync();
        Assert.All(sums, d => Assert.InRange(d, -AttributeEvolution.MaxChangePerSeason, AttributeEvolution.MaxChangePerSeason));
        Assert.NotEmpty(await svc.SquadAsync(Season, arsenal));
    }

    [Fact]
    public void Training_rules()
    {
        Assert.Equal(3, TrainingRules.GrowthFields(19, TrainingIntensity.Hard));
        Assert.Equal(0, TrainingRules.GrowthFields(27, TrainingIntensity.Light));
        Assert.Equal(0, TrainingRules.GrowthFields(33, TrainingIntensity.Hard));
        Assert.Equal(0, TrainingRules.DeclineFields(33, TrainingFocus.Physical, TrainingIntensity.Hard));
        Assert.Equal(2, TrainingRules.DeclineFields(33, TrainingFocus.Attack, TrainingIntensity.Hard));
        var id = Guid.NewGuid();
        Assert.Equal(TrainingRules.Potential(id, 70, 20), TrainingRules.Potential(id, 70, 20));
        Assert.InRange(TrainingRules.Potential(id, 70, 20), 75, 84);
        Assert.Equal(70, TrainingRules.Potential(id, 70, 30));
    }
}

