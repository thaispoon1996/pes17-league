using Pes17League.Clubs.Application;
using Xunit;

namespace Pes17League.Clubs.Tests;

public class ScorerNameTests
{
    [Theory]
    [InlineData("K.Rodriguez", "Kevin Rodríguez", true)]
    [InlineData("K. Rodríguez", "K. Rodríguez", true)]
    [InlineData("E. Konsa", "Ezri Konsa Ngoyo", true)]
    [InlineData("M.Fuseini", "Kevin Rodríguez", false)]   // phản lưới: số áo trùng nhưng tên khác
    [InlineData(null, "Ai Cũng Được", true)]
    public void Matches_surname(string? banner, string name, bool ok) => Assert.Equal(ok, ScorerName.Matches(banner, name));
}
