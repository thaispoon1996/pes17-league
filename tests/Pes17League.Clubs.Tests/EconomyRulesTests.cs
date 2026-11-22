using Pes17League.Clubs.Application;
namespace Pes17League.Clubs.Tests;

public class EconomyRulesTests
{
    [Theory]
    [InlineData(1000, 1000, 0)]
    [InlineData(1100, 1000, 50)]              // vượt 10% → 100 × 50%
    [InlineData(1250, 1000, 50 + 150)]        // + 150 × 100%
    [InlineData(1400, 1000, 50 + 150 + 225)]  // + 150 × 150% (trần cứng)
    [InlineData(500, 0, 0)]                   // chưa đặt trần
    public void Luxury_tax_is_progressive_on_excess(long payroll, long cap, long tax) =>
        Assert.Equal(tax, LuxuryTax.Compute(payroll, cap));

    [Fact]
    public void Hard_cap_is_plus_40_percent() => Assert.Equal(1400, LuxuryTax.HardCap(1000));

    [Fact]
    public void Contract_length_10_to_60()
    {
        Assert.False(ContractRules.IsValidLength(9));
        Assert.True(ContractRules.IsValidLength(10));
        Assert.True(ContractRules.IsValidLength(60));
        Assert.False(ContractRules.IsValidLength(61));
    }

    [Fact]
    public void Price_model_A()
    {
        var m = new PriceModel();
        var gk = new Dictionary<string, int> { ["GOAL KEEPING"] = 80, ["RESPONSE"] = 70, ["JUMP"] = 70, ["BALANCE"] = 70, ["AGILITY"] = 70 };
        Assert.Equal(75, m.Overall(Position.GK, gk));   // (80×5 + 70×5) / 10
        Assert.Equal(1_000_000, m.BasePrice(70, 25));
        Assert.True(m.BasePrice(80, 25) > 3 * m.BasePrice(70, 25));       // e^1.2 ≈ 3.32
        Assert.True(m.BasePrice(80, 20) > m.BasePrice(80, 25));            // trẻ đắt hơn
        Assert.True(m.BasePrice(80, 34) < m.BasePrice(80, 30));
        Assert.Equal(1.05, m.NextFormMultiplier(1, 1), 6);                  // tối đa +5%/vòng
        Assert.Equal(1.05, m.NextFormMultiplier(1, 9), 6);
        Assert.Equal(0.95, m.NextFormMultiplier(1, -1), 6);
        Assert.Equal(0.5, m.NextFormMultiplier(0.5, -1), 6);                // sàn
        Assert.Equal(1_050_000, m.Price(1_000_000, 1.05));
    }

    [Fact]
    public void Attribute_evolution_capped_3() => Assert.Equal((73, 67, 71), (AttributeEvolution.Clamp(70, 80), AttributeEvolution.Clamp(70, 60), AttributeEvolution.Clamp(70, 71)));
    [Fact]
    public void Market_model_only_from_season_2_and_legacy_unchanged()
    {
        Assert.Same(PriceModelOptions.Legacy, PriceModelOptions.ForSeason(1));
        Assert.Same(PriceModelOptions.Market, PriceModelOptions.ForSeason(2));
        var legacy = new PriceModel();
        Assert.Equal(1_000_000, legacy.BasePrice(70, 25));
        Assert.Equal(legacy.BasePrice(80, 25), legacy.BasePrice(80, 25, new RealValue(200_000_000, 25, 80)));   // mùa cũ bỏ qua giá thật
    }

    [Fact]
    public void Market_model_uses_real_value_and_follows_age_and_overall()
    {
        var m = new PriceModel(PriceModelOptions.Market);
        var real = new RealValue(200_000_000, 25, 91);
        Assert.Equal(200_000_000, m.BasePrice(91, 25, real));
        Assert.Equal(200_000_000, m.BasePrice(91, 26, real));                      // 24–26 cùng hệ số
        Assert.Equal(160_000_000, m.BasePrice(91, 27, real));                      // 27 tuổi × 0,8
        Assert.True(m.BasePrice(93, 25, real) > 240_000_000);                      // +2 chỉ số trên 75: e^(2 × 0,2) ≈ ×1,49
        Assert.Equal(m.BasePrice(70, 25), m.BasePrice(70, 25, null));              // không ghép được → công thức
        Assert.Equal(1_000_000, m.BasePrice(70, 25));
        Assert.True(m.BasePrice(85, 25) > 12_000_000);                             // ngôi sao đắt vọt (cũ ≈ 6 tr)
        Assert.True(m.BasePrice(75, 34) < m.BasePrice(75, 25) / 5);
    }

    [Fact]
    public void Sponsor_at_least_1_5_permille_of_squad_value()
    {
        Assert.Equal(FinanceRules.Sponsor(1.0), FinanceRules.Sponsor(1.0, 20_000_000));     // tầm trung giữ mức cũ
        Assert.Equal(2_000_000, FinanceRules.Sponsor(1.4, 1_333_333_333));
    }
    [Fact]
    public void Market_seasons_give_rich_clubs_more_budget()
    {
        Assert.Equal((0.20, 0.5), Pes17CareerService.BudgetRule(1));
        Assert.Equal((0.15, -0.7), Pes17CareerService.BudgetRule(2));
        var b = BudgetAllocation.Allocate([("big", 1_000_000_000L), ("small", 10_000_000L)], 100_000_000, -0.7);
        Assert.Equal(100_000_000, b["big"] + b["small"]);
        Assert.True(b["big"] > b["small"] * 20);                                      // (100×)^0,7 ≈ 25×
    }
}
