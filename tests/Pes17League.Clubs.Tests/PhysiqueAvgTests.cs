using Pes17League.Clubs.Application;

namespace Pes17League.Clubs.Tests;

public class PhysiqueAvgTests
{
    [Fact]
    public void Of_averages_positive_height_and_weight_one_decimal()
    {
        var avg = PhysiqueAvg.Of([(180, 75), (184, 80), (null, 70), (0, 0)]);
        Assert.Equal(182.0, avg.HeightCm);
        Assert.Equal(75.0, avg.WeightKg);
    }

    [Fact]
    public void Of_all_missing_returns_nulls()
    {
        var avg = PhysiqueAvg.Of([(null, null), (0, -1)]);
        Assert.Null(avg.HeightCm);
        Assert.Null(avg.WeightKg);
    }
}
