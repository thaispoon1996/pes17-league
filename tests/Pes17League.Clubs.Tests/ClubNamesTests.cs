using Pes17League.Clubs.Application;
using Xunit;

namespace Pes17League.Clubs.Tests;

public class ClubNamesTests
{
    [Theory]
    [InlineData("Arsenal Fc", "Arsenal FC")]
    [InlineData("Afc Bournemouth", "AFC Bournemouth")]
    [InlineData("Sunderland Αfc", "Sunderland AFC")]          // Α Hy Lạp trong patch
    [InlineData("Paris Saint-germain", "Paris Saint-Germain")]
    [InlineData("Rayo Vallecano De Madrid", "Rayo Vallecano de Madrid")]
    [InlineData("Csd Defensa Y Justicia", "CSD Defensa y Justicia")]
    [InlineData("1. Fc Köln", "1. FC Köln")]
    [InlineData("Red Star FC", "Red Star FC")]                // đã đúng → giữ
    [InlineData("Al-Ittihad SC (Libya)", "Al-Ittihad SC (Libya)")]
    [InlineData("Stade d'Abidjan", "Stade d'Abidjan")]
    [InlineData("Panathinaikos F.C.", "Panathinaikos F.C.")]
    [InlineData("De Graafschap", "De Graafschap")]
    [InlineData("Vfb Stuttgart", "VfB Stuttgart")]
    [InlineData("VFL Wolfsburg", "VfL Wolfsburg")]
    [InlineData("Heart Of Midlothian Fc", "Heart of Midlothian FC")]
    [InlineData("Ca Newell'S Old Boys", "CA Newell's Old Boys")]            // "De" đầu tên giữ hoa
    public void Pretty(string raw, string want) => Assert.Equal(want, ClubNames.Pretty(raw));
}
