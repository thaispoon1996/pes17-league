namespace Pes17League.OptionFile.Tests;

public class GameTextTests
{
    [Theory]
    [InlineData("Nguyễn Văn Đức", "Nguyen Van Duc")]
    [InlineData("Hà Nội FC", "Ha Noi FC")]          // có ký tự ngoài Latin-1 → bỏ dấu cả chuỗi
    [InlineData("đội Sông Lam", "doi Song Lam")]
    [InlineData("Piero Hincapié", "Piero Hincapié")] // hiển thị được → giữ nguyên
    [InlineData("Viktor Gyökeres", "Viktor Gyökeres")]
    [InlineData("Kristijan Jakić", "Kristijan Jakic")]
    [InlineData("東京", "??")]
    public void ToDisplayable(string input, string expected) => Assert.Equal(expected, GameText.ToDisplayable(input));

    [Theory]
    [InlineData("Nguyễn Văn Đức", "DUC")]
    [InlineData("  Phạm  Quốc Thái ", "THAI")]
    [InlineData("Viktor Gyökeres", "GYOKERES")]
    [InlineData("N'Golo Kanté", "KANTE")]
    [InlineData("Ole Gunnar Solskjær", "SOLSKJAER")]    // æ không tách dấu được → AE
    [InlineData("Wolfeschlegelsteinhausen", "WOLFESCHLEGELST")]
    [InlineData("東京", "")]
    public void ShirtName(string input, string expected) => Assert.Equal(expected, GameText.ShirtName(input));
}
