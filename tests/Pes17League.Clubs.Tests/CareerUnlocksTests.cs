using Pes17League.Clubs.Domain;

namespace Pes17League.Clubs.Tests;

/// <summary>Luật mở dần tính năng career (UI đợt 1, PO 19/11/2026).</summary>
public class CareerUnlocksTests
{
    static CareerState S(int slot = 1, int played = 0, bool market = false, bool offer = false, bool expiring = false, bool finished = false) =>
        new(true, finished, slot, 69, played, market, offer, expiring);

    [Fact]
    public void Not_started_unlocks_nothing() =>
        Assert.Empty(CareerUnlocks.Unlocked(new CareerState(false, false, 0, 0, 0, false, false, false)));

    [Fact]
    public void Start_of_season_has_only_home_squad_league()
    {
        Assert.Equal(new[] { "home", "league", "squad" }, CareerUnlocks.Unlocked(S()).Order());
    }

    [Fact]
    public void Features_open_with_progress()
    {
        var after1 = CareerUnlocks.Unlocked(S(slot: 2, played: 1));
        Assert.Contains(CareerUnlocks.Finance, after1);
        Assert.DoesNotContain(CareerUnlocks.Training, after1);
        Assert.Contains(CareerUnlocks.Training, CareerUnlocks.Unlocked(S(slot: 4, played: CareerUnlocks.TrainingAfter)));
        Assert.Contains(CareerUnlocks.Transfer, CareerUnlocks.Unlocked(S(market: true)));
        Assert.DoesNotContain(CareerUnlocks.Offers, CareerUnlocks.Unlocked(S(market: true)));
        Assert.Contains(CareerUnlocks.Offers, CareerUnlocks.Unlocked(S(market: true, offer: true)));
        Assert.Contains(CareerUnlocks.Contracts, CareerUnlocks.Unlocked(S(expiring: true)));
        Assert.DoesNotContain(CareerUnlocks.Academy, CareerUnlocks.Unlocked(S(slot: Pes17TransferWindows.WinterStart(69) - 1)));
        Assert.Contains(CareerUnlocks.Academy, CareerUnlocks.Unlocked(S(slot: Pes17TransferWindows.WinterStart(69))));
        Assert.Contains(CareerUnlocks.SeasonEnd, CareerUnlocks.Unlocked(S(slot: 0, finished: true)));
    }

    [Fact]
    public void Market_slots_follow_transfer_windows()
    {
        Assert.Equal(6, CareerUnlocks.MarketSlotsLeft(1, 69));
        Assert.Equal(1, CareerUnlocks.MarketSlotsLeft(6, 69));
        Assert.Equal(0, CareerUnlocks.MarketSlotsLeft(7, 69));
        Assert.Equal(Pes17TransferWindows.WinterStart(69), CareerUnlocks.NextMarketSlot(7, 69));
        Assert.Null(CareerUnlocks.NextMarketSlot(Pes17TransferWindows.WinterStart(69) + 4, 69));
    }
}
