using Pes17League.Clubs.Domain;

namespace Pes17League.Clubs.Tests;

public class MoneyTextTests
{
    [Theory]
    [InlineData(0, "€0")]
    [InlineData(500, "€500")]
    [InlineData(35_000, "€35K")]
    [InlineData(35_500, "€35.5K")]
    [InlineData(120_000, "€120K")]
    [InlineData(999_999, "€1M")]          // làm tròn chạm 1000K → lên M
    [InlineData(1_000_000, "€1M")]
    [InlineData(8_150_000, "€8.15M")]
    [InlineData(8_100_000, "€8.1M")]
    [InlineData(123_456_789, "€123.46M")]
    [InlineData(1_250_000_000, "€1.25B")]
    [InlineData(-1_200_000, "-€1.2M")]
    [InlineData(-25_000, "-€25K")]
    public void Euro_with_K_M_B(long v, string expected) => Assert.Equal(expected, MoneyText.Format(v));
}
