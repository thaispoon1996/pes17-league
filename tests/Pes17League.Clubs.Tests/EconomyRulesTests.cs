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
}
