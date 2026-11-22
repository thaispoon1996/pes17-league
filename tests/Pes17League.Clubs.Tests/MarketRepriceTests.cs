using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Application;
using Pes17League.Clubs.Domain;

namespace Pes17League.Clubs.Tests;

/// <summary>Áp giá thật giữa mùa 1 (PO 22/11): mùa bắt đầu theo công thức cũ → giá, lương, trần mềm, bù ngân sách chỉ cộng, tài trợ; chỉ 1 lần.</summary>
public class MarketRepriceTests
{
    private static Dictionary<string, int> Attrs(int v, int age) => new()
    {
        ["ATTACK"] = v, ["SHOT ACCURACY"] = v, ["SHOT POWER"] = v, ["TOP SPEED"] = v, ["ACCELERATION"] = v,
        ["DRIBBLE ACCURACY"] = v, ["TECHNIQUE"] = v, ["RESPONSE"] = v, ["AGE"] = age,
    };

    private sealed record State(Dictionary<Guid, PlayerPrice> Prices, Dictionary<Guid, long> Wages, Dictionary<Guid, long> Granted, int Ledger, int Audit);

    private static async Task<State> StateAsync(TestDb t, Guid season)
    {
        await using var v = t.Context();
        return new(await v.PlayerPrices.AsNoTracking().Where(p => p.SeasonId == season).ToDictionaryAsync(p => p.PlayerId),
            await v.Contracts.AsNoTracking().ToDictionaryAsync(c => c.PlayerId, c => c.Wage!.Value),
            await v.Ledger.Where(e => e.Reason == LedgerReason.InitialBudget).ToDictionaryAsync(e => e.ClubId, e => e.Amount),
            await v.Ledger.CountAsync(), await v.AdminAudit.CountAsync());
    }

    [DbFact]
    public async Task Apply_market_prices_mid_season_once()
    {
        await using var t = await TestDb.CreateAsync();
        Guid season, star, formed;
        // Mùa 1 đã bắt đầu theo công thức cũ: giá (1 người phong độ ×1,2), lương 2‰, ngân sách đầu mùa k 0,5, tài trợ.
        await using (var db = t.Context())
        {
            var s = await new CompetitionService(db, t.Store, t.Clock).CreateSeasonAsync("Mùa 1", 1);
            season = s.Id;
            var now = t.Clock.GetUtcNow();
            var players = new List<FootballPlayer>();
            foreach (var (name, squad) in new (string, (int Ovr, int Age)[])[]
                     { ("Lớn", [(88, 25), (85, 24), (84, 29)]), ("Vừa", [(80, 26), (78, 31)]), ("Nhỏ", [(72, 23), (70, 33)]) })
            {
                var club = new Club { Name = name };
                db.Clubs.Add(club);
                foreach (var (ovr, age) in squad)
                {
                    var p = new FootballPlayer { Name = $"{name} {ovr}", Position = Position.CF };
                    players.Add(p);
                    db.Players.Add(p);
                    db.PlayerAttributes.Add(new PlayerAttributes { PlayerId = p.Id, SeasonId = season, Values = Attrs(ovr, age), CapturedAt = now });
                    db.SquadMemberships.Add(new SquadMembership { ClubId = club.Id, PlayerId = p.Id, ShirtNumber = players.Count, SlotOrder = players.Count, JoinedAt = now });
                    db.Contracts.Add(new PlayerContract { ClubId = club.Id, PlayerId = p.Id, MatchesTotal = 40, StartedAt = now });
                }
            }
            (star, formed) = (players[0].Id, players[3].Id);
            (players[0].RealValue, players[0].RealValueAge, players[0].RealValueOverall) = (150_000_000, 25, 88);
            await db.SaveChangesAsync();

            await new EconomyService(db, t.Clock, PriceModelOptions.Legacy).RecalculatePricesAsync(season);
            var pf = await db.PlayerPrices.SingleAsync(p => p.PlayerId == formed && p.SeasonId == season);
            (pf.FormMultiplier, pf.Price) = (1.2, new PriceModel(PriceModelOptions.Legacy).Price(pf.BasePrice, 1.2));
            await db.SaveChangesAsync();
            var eco = new EconomyService(db, t.Clock);
            await eco.SetParametersAsync(season, new(SoftWageCap: 60_000, WagePermille: 2, InitialBudgetFund: 30_000_000, BudgetExponent: 0.5));
            Assert.Equal(7, await eco.SetImportedWagesAsync(season));
            await eco.AllocateInitialBudgetsAsync(season);
            await new FinanceService(db, t.Clock).RefreshSponsorsAsync(season);
            s.Status = SeasonStatus.Active;
            await db.SaveChangesAsync();
        }
        var before = await StateAsync(t, season);

        // Chạy thử: ra số nhưng DB giữ nguyên.
        await using (var db = t.Context())
        {
            var dry = await new MarketRepriceService(db, t.Store, t.Clock).ApplyAsync("test", dry: true);
            Assert.Equal((7, 7), (dry.Players, dry.Contracts));
        }
        var still = await StateAsync(t, season);
        Assert.Equal(before.Prices.ToDictionary(p => p.Key, p => p.Value.Price), still.Prices.ToDictionary(p => p.Key, p => p.Value.Price));
        Assert.Equal(before.Wages, still.Wages);
        Assert.Equal((before.Ledger, before.Audit), (still.Ledger, still.Audit));

        MarketRepriceResult r;
        await using (var db = t.Context()) r = await new MarketRepriceService(db, t.Store, t.Clock).ApplyAsync("test");
        var after = await StateAsync(t, season);
        var m = new PriceModel(PriceModelOptions.Market);
        Assert.Equal(150_000_000, after.Prices[star].Price);                                    // giá thật (cùng tuổi + chỉ số lúc nhập)
        Assert.Equal(1.2, after.Prices[formed].FormMultiplier);                                 // giữ phong độ
        Assert.Equal(m.Price(m.BasePrice(80, 26), 1.2), after.Prices[formed].Price);
        foreach (var (id, w) in before.Wages)                                                   // lương × giá gốc mới / giá gốc cũ
            Assert.Equal((long)Math.Round(w * (double)after.Prices[id].BasePrice / before.Prices[id].BasePrice, MidpointRounding.AwayFromZero), after.Wages[id]);
        Assert.True(after.Wages[star] > before.Wages[star] * 10);

        await using var v = t.Context();
        var e = await v.SeasonEconomies.SingleAsync(x => x.SeasonId == season);
        var byClub = (await v.Contracts.GroupBy(c => c.ClubId).Select(g => g.Sum(c => c.Wage!.Value)).ToListAsync()).OrderDescending().ToList();
        Assert.Equal(byClub[1] / 1000 * 1000, e.SoftWageCap);                                   // trần mềm = quỹ lương CLB thứ 2
        Assert.Equal((2, -0.7), (e.WagePermille, e.BudgetExponent));
        Assert.Equal((long)Math.Round(after.Prices.Values.Sum(p => p.Price) * 0.15 / 1e6) * 1_000_000, e.InitialBudgetFund);
        Assert.Equal((e.SoftWageCap, e.InitialBudgetFund), (r.SoftCapAfter, r.FundAfter));

        // Bù chỉ cộng: CLB lớn nhận phần chênh theo cách chia mới; CLB lẽ ra bị bớt giữ nguyên.
        var expected = BudgetAllocation.Allocate(r.Clubs.OrderBy(c => c.ClubId).Select(c => (c.ClubId, c.ValueAfter)).ToList(), e.InitialBudgetFund, -0.7);
        Assert.Equal(["Lớn", "Vừa", "Nhỏ"], r.Clubs.Select(c => c.Name));
        Assert.Equal((1, 2), (r.ToppedUpClubs, r.KeptClubs));
        foreach (var c in r.Clubs)
            Assert.Equal(Math.Max(0, expected[c.ClubId] - before.Granted[c.ClubId]), c.TopUp);
        var topUps = await v.Ledger.Where(x => x.Reason == LedgerReason.Adjustment && x.RefType == EconomyService.InitialBudgetTopUp).ToListAsync();
        Assert.Equal((1, r.TopUpTotal), (topUps.Count, topUps.Sum(x => x.Amount)));
        Assert.All(r.Clubs, c => Assert.Equal(before.Granted[c.ClubId] + c.TopUp, c.BalanceAfter));

        // Tài trợ theo hạng giá trị mới, tối thiểu 1,5‰ giá trị squad.
        for (var i = 0; i < r.Clubs.Count; i++)
            Assert.Equal(FinanceRules.Sponsor(FinanceRules.Reputation(i, r.Clubs.Count), r.Clubs[i].ValueAfter), r.Clubs[i].SponsorAfter);
        Assert.Equal(1, await v.AdminAudit.CountAsync(a => a.Action == MarketRepriceService.Action && a.EntityId == season));

        // Chỉ 1 lần mỗi mùa.
        await using (var db = t.Context())
            await Assert.ThrowsAsync<AdminRuleException>(() => new MarketRepriceService(db, t.Store, t.Clock).ApplyAsync("test"));
        Assert.Equal(after.Wages, (await StateAsync(t, season)).Wages);
    }
}
