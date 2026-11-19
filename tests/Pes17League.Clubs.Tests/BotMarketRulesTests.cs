using Pes17League.Clubs.Domain;

namespace Pes17League.Clubs.Tests;

public class BotMarketRulesTests
{
    static MarketPlayer P(Position pos, int ovr, int age = 25, long price = 1_000_000, Guid? club = null) =>
        new(Guid.NewGuid(), pos, ovr, age, price, club);

    static List<MarketPlayer> Squad() =>
    [
        P(Position.GK, 80), P(Position.GK, 60),
        P(Position.CB, 78), P(Position.CB, 77), P(Position.SB, 76), P(Position.SB, 66),
        P(Position.CMF, 79), P(Position.CMF, 78), P(Position.AMF, 77), P(Position.DMF, 76), P(Position.CMF, 60),
        P(Position.CF, 81), P(Position.WF, 80), P(Position.CF, 55),
    ];

    [Fact]
    public void Need_finds_weakest_line_and_level()
    {
        var need = BotMarketRules.Need(Squad().Select(p => (p.Position.Group(), p.Ovr)));
        Assert.Equal(PositionGroup.Defender, need.Group);
        Assert.Equal(66, need.Weakest);
        Assert.Equal((80 + 78 + 77 + 76 + 66 + 79 + 78 + 77 + 76 + 81 + 80) / 11.0, need.Level, 3);

        var noKeeper = BotMarketRules.Need(Squad().Where(p => p.Position != Position.GK).Select(p => (p.Position.Group(), p.Ovr)));
        Assert.Equal(PositionGroup.Goalkeeper, noKeeper.Group);
        Assert.Equal(0, noKeeper.Weakest);
    }

    [Fact]
    public void Fits_requires_line_upgrade_within_level_age_and_budget()
    {
        var need = new SquadNeed(75, PositionGroup.Defender, 66);
        Assert.True(BotMarketRules.Fits(P(Position.CB, 70), need, 10_000_000));
        Assert.False(BotMarketRules.Fits(P(Position.CMF, 70), need, 10_000_000));         // sai tuyến
        Assert.False(BotMarketRules.Fits(P(Position.CB, 66), need, 10_000_000));          // không hơn người yếu nhất
        Assert.False(BotMarketRules.Fits(P(Position.CB, 80), need, 10_000_000));          // quá trình độ đội + 4
        Assert.False(BotMarketRules.Fits(P(Position.CB, 70, age: 31), need, 10_000_000)); // quá tuổi
        Assert.False(BotMarketRules.Fits(P(Position.CB, 70, price: 7_000_000), need, 10_000_000)); // > 60% tiền
    }

    [Fact]
    public void Seller_keeps_squad_and_starters_go_only_to_stronger_clubs()
    {
        Assert.False(BotMarketRules.SellerLets(BotMarketRules.SellerKeeps, false, 70, 80));
        Assert.True(BotMarketRules.SellerLets(25, false, 80, 70));
        Assert.False(BotMarketRules.SellerLets(25, true, 80, 70));
        Assert.True(BotMarketRules.SellerLets(25, true, 70, 80));
    }

    [Fact]
    public void Best_and_trim_pick()
    {
        var a = P(Position.CB, 72, age: 27); var b = P(Position.CB, 72, age: 22); var c = P(Position.CB, 70);
        Assert.Equal(b, BotMarketRules.Best([a, b, c]));
        Assert.Null(BotMarketRules.Best([]));

        var squad = Squad();
        var starters = BotMarketRules.Starters(squad);
        Assert.Equal(11, starters.Count);
        Assert.Equal(squad.Single(p => p.Ovr == 55).Id, BotMarketRules.TrimPick(squad, starters, new HashSet<Guid>())!.Id);
        // Không thanh lý thủ môn khi chỉ còn 2.
        var gkOnly = squad.Where(p => p.Position == Position.GK || starters.Contains(p.Id)).ToList();
        Assert.Null(BotMarketRules.TrimPick(gkOnly, starters, new HashSet<Guid>()));
    }

    [Fact]
    public void Offer_fee_and_walk_away_ranges()
    {
        for (var roll = 0; roll < 1000; roll += 13)
            Assert.InRange(BotMarketRules.OfferFee(5_000_000, roll), 5_000_000, 6_500_000);
        for (var i = 0; i < 200; i++)
        {
            var w = BotMarketRules.WalkAway(5_000_000, Guid.NewGuid());
            Assert.InRange(w, 6_500_000, 8_500_000);
            Assert.Equal(0, w % 10_000);
        }
        var id = Guid.NewGuid();
        Assert.Equal(BotMarketRules.WalkAway(5_000_000, id), BotMarketRules.WalkAway(5_000_000, id));
    }
}
