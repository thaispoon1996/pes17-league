namespace Pes17League.OptionFile.Tests;

/// <summary>
/// Ô vị trí đá được + tên in áo, ô tạo cầu thủ 32768+ (bước 3c phần 2): đối chiếu dữ liệu OF gốc PES6VN 25-26.
/// Chưa kiểm màn Edit trong game (06/11 chỉ kiểm cầu thủ tạo mới ra sân được).
/// </summary>
public class EditSlotTests
{
    private const int EditZero = 32768;

    [FixtureFact]
    public void Orig_players_have_own_position_playable_and_uppercase_shirt_names_and_templates_only_cf()
    {
        var img = Fixtures.Image("orig.OPT");
        var named = Enumerable.Range(1, OptionFileImage.PlayerCount - 1).Where(id => img.GetPlayerName(id).Length > 0).ToList();
        Assert.Equal(4783, named.Count);
        Assert.All(named, id => Assert.Equal(1, img.Get(id, PlayerField.Playable(img.GetPosition(id)))));
        Assert.All(named, id => Assert.Matches("^[ -~]{0,15}$", img.GetShirtName(id)));
        Assert.All(named, id => Assert.Equal(img.GetShirtName(id).ToUpperInvariant(), img.GetShirtName(id)));
        foreach (var id in Enumerable.Range(OptionFileImage.FirstEditId, OptionFileImage.EditCount))
        {
            Assert.Equal(("", "", Position.CF), (img.GetPlayerName(id), img.GetShirtName(id), img.GetPosition(id)));
            Assert.Equal([Position.CF], Enum.GetValues<Position>().Where(p => img.Get(id, PlayerField.Playable(p)) == 1));
            Assert.All(PlayerField.Verified.Take(26), f => Assert.Equal(50, img.Get(id, f)));
        }
    }

    [FixtureFact]
    public void Playable_and_shirt_name_writes_touch_only_their_bits()
    {
        var img = Fixtures.Image("orig.OPT");
        var before = PlayerField.Verified.Append(PlayerField.RegisteredPosition).ToDictionary(f => f, f => img.Get(EditZero, f));
        foreach (var p in Enum.GetValues<Position>()) img.Set(EditZero, PlayerField.Playable(p), p == Position.CB ? 1 : 0);
        img.SetShirtName(EditZero, "Duc");
        img.SetPlayerName(EditZero, "Nguyễn Văn Đức");
        Assert.Equal([Position.CB], Enum.GetValues<Position>().Where(p => img.Get(EditZero, PlayerField.Playable(p)) == 1));
        Assert.Equal(("DUC", "Nguyen Van Duc"), (img.GetShirtName(EditZero), img.GetPlayerName(EditZero)));
        Assert.All(before, kv => Assert.Equal(kv.Value, img.Get(EditZero, kv.Key)));
        Assert.Equal("", img.GetShirtName(EditZero + 1));
        Assert.Throws<ArgumentException>(() => img.SetShirtName(EditZero, "Đức"));
        Assert.Throws<ArgumentException>(() => img.SetShirtName(EditZero, new string('A', 16)));
    }

    /// <summary>Quốc tịch: ô mẫu = Tự do; mã theo màn chọn quốc tịch trong game (kiểm 06/11: Heaven ENG, Bayindir TUR, Fernandes PRT).</summary>
    [FixtureFact]
    public void Nationality_matches_in_game_codes_and_write_keeps_age()
    {
        var img = Fixtures.Image("orig.OPT");
        Assert.Equal(GameText.FreeNationality, GameText.Nations.Count);
        Assert.Equal(GameText.FreeNationality, img.Get(EditZero, PlayerField.Nationality));
        Assert.Equal(["ENG", "TUR", "PRT"], new[] { 1034, 3977, 1664 }.Select(id => GameText.Nations[img.Get(id, PlayerField.Nationality)]));
        Assert.Equal((54, GameText.FreeNationality, GameText.FreeNationality), (GameText.NationIndex(" vie"), GameText.NationIndex("GER"), GameText.NationIndex(null)));
        var age = img.Get(EditZero, PlayerField.Age);
        img.Set(EditZero, PlayerField.Nationality, 54);
        Assert.Equal((54, age), (img.Get(EditZero, PlayerField.Nationality), img.Get(EditZero, PlayerField.Age)));
    }
}
