using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Application;
using Pes17League.Clubs.Domain;

namespace Pes17League.Clubs.Tests;

public class RealValueTests
{
    [DbFact]
    public async Task Real_value_sets_price_from_season_1()
    {
        await using var t = await TestDb.CreateAsync();
        await using var db = t.Context();
        var svc = new CompetitionService(db, t.Store, t.Clock);
        var s1 = await svc.CreateSeasonAsync("Mùa 1", 1);
        var s2 = await svc.CreateSeasonAsync("Mùa 2", 2);
        FootballPlayer star = new() { Name = "Sao", Position = Position.CF }, other = new() { Name = "Khác", Position = Position.CF };
        db.Players.AddRange(star, other);
        Dictionary<string, int> Attrs(int v, int age) => new()
        {
            ["ATTACK"] = v, ["SHOT ACCURACY"] = v, ["SHOT POWER"] = v, ["TOP SPEED"] = v, ["ACCELERATION"] = v,
            ["DRIBBLE ACCURACY"] = v, ["TECHNIQUE"] = v, ["RESPONSE"] = v, ["AGE"] = age,
        };
        foreach (var p in new[] { star, other })
            db.PlayerAttributes.Add(new PlayerAttributes { PlayerId = p.Id, SeasonId = s1.Id, Values = Attrs(90, 25), CapturedAt = t.Clock.GetUtcNow() });
        await db.SaveChangesAsync();

        var r = await new RealValueService(db, t.Clock).ImportAsync([new(star.Id, 180_000_000, "tm:1"), new(Guid.NewGuid(), 5, null)], "test");
        Assert.Equal((1, 0, 1, 0), (r.Set, r.Cleared, r.UnknownPlayers, r.NoAttributes));
        Assert.Equal((180_000_000L, 25, 90), (star.RealValue, star.RealValueAge, star.RealValueOverall));

        var eco = new EconomyService(db, t.Clock);
        await eco.RecalculatePricesAsync(s1.Id);
        var p1 = await db.PlayerPrices.Where(p => p.SeasonId == s1.Id).ToDictionaryAsync(p => p.PlayerId, p => p.Price);
        var m = new PriceModel(PriceModelOptions.Market);
        Assert.Equal(180_000_000, p1[star.Id]);                                      // mùa 1 (PO 22/11): giá thật, cùng tuổi + chỉ số lúc nhập
        Assert.Equal(m.BasePrice(90, 25), p1[other.Id]);                             // không có giá thật: đường giá mới
        await new EconomyService(db, t.Clock, PriceModelOptions.Legacy).RecalculatePricesAsync(s1.Id);
        p1 = await db.PlayerPrices.Where(p => p.SeasonId == s1.Id).ToDictionaryAsync(p => p.PlayerId, p => p.Price);
        Assert.Equal(p1[other.Id], p1[star.Id]);                                     // công thức cũ (chỉ khi chọn rõ): bỏ qua giá thật
        await eco.RecalculatePricesAsync(s1.Id);

        // Mùa 2: 1 tuổi + chỉ số giảm 1 → giá thật đi theo tuổi (27 → ×0,8) và đường giá.
        db.PlayerAttributes.Add(new PlayerAttributes { PlayerId = star.Id, SeasonId = s2.Id, Values = Attrs(89, 27), CapturedAt = t.Clock.GetUtcNow() });
        db.PlayerAttributes.Add(new PlayerAttributes { PlayerId = other.Id, SeasonId = s2.Id, Values = Attrs(89, 27), CapturedAt = t.Clock.GetUtcNow() });
        await db.SaveChangesAsync();
        await eco.RecalculatePricesAsync(s2.Id);
        var p2 = await db.PlayerPrices.Where(p => p.SeasonId == s2.Id).ToDictionaryAsync(p => p.PlayerId, p => p.Price);
        Assert.Equal((long)Math.Round(180_000_000 * 0.8 * Math.Exp(-0.2) / 1000) * 1000, p2[star.Id]);
        Assert.Equal(m.BasePrice(89, 27), p2[other.Id]);

        // Nhập lại không có "Sao" → xoá giá thật.
        r = await new RealValueService(db, t.Clock).ImportAsync([], "test");
        Assert.Equal((0, 1), (r.Set, r.Cleared));
        Assert.Null(star.RealValue);
    }
}
