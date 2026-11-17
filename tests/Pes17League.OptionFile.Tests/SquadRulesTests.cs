namespace Pes17League.OptionFile.Tests;

/// <summary>Luật nghiệp vụ squad (decisions.md 01/11) — gồm các ca editor không có.</summary>
public class SquadRulesTests
{
    private const int Arsenal = 0, AstonVilla = 1, Raya = 1598, Gyokeres = 965;

    [FixtureFact]
    public void Release_starter_without_same_position_sub_uses_same_group_and_flags_review()
    {
        var img = Fixtures.Image("orig.OPT");
        var rules = new SquadRules(img);
        var s = img.GetSquad(Arsenal);
        // bỏ các CF dự bị để không còn dự bị cùng vị trí với Gyökeres (CF đá chính)
        var noCfSubs = s with { Players = s.Players.Where((p, i) => i < 11 || img.GetPosition(p.PlayerId) != Position.CF).ToList() };
        var gi = noCfSubs.IndexOf(Gyokeres);
        Assert.InRange(gi, 0, 10);

        var r = rules.Release(noCfSubs, Gyokeres);

        Assert.True(r.Ok);
        Assert.True(r.NeedsLineupReview);
        var replacement = r.Squad.Players[gi].PlayerId;
        Assert.Equal(PositionGroup.Forward, img.GetPosition(replacement).Group());
        var firstForwardSub = noCfSubs.Players.Skip(11).First(p => img.GetPosition(p.PlayerId).Group() == PositionGroup.Forward);
        Assert.Equal(firstForwardSub.PlayerId, replacement);
        Assert.Equal(noCfSubs.Players.Count - 1, r.Squad.Players.Count);
    }

    [FixtureFact]
    public void Release_blocked_below_min_squad_size()
    {
        var img = Fixtures.Image("orig.OPT");
        var s = img.GetSquad(Arsenal);
        var sixteen = s with { Players = s.Players.Take(16).ToList() };
        var r = new SquadRules(img).Release(sixteen, sixteen.Players[15].PlayerId);
        Assert.Equal(SquadError.BelowMinSquadSize, r.Error);
        Assert.Same(sixteen, r.Squad);
    }

    [FixtureFact]
    public void Release_blocked_when_fewer_than_two_goalkeepers_remain()
    {
        var img = Fixtures.Image("orig.OPT");
        var rules = new SquadRules(img);
        var s = img.GetSquad(Arsenal);
        Assert.Equal(2, rules.Goalkeepers(s));
        Assert.Equal(SquadError.BelowMinGoalkeepers, rules.Release(s, Raya).Error);
    }

    [FixtureFact]
    public void Sign_blocked_when_squad_full_or_player_in_other_club()
    {
        var img = Fixtures.Image("orig.OPT");
        var rules = new SquadRules(img);
        var free = Enumerable.Range(1, 4999).First(id => rules.FindClub(id) is null);
        var full = new ClubSquad(Arsenal, Enumerable.Range(0, 32).Select(i => new SquadSlot(4000 + i, i + 1)).ToList());
        Assert.Equal(SquadError.SquadFull, rules.Sign(full, free).Error);
        Assert.Equal(SquadError.PlayerInOtherClub, rules.Sign(img.GetSquad(AstonVilla), Raya).Error);

        var ok = rules.Sign(img.GetSquad(AstonVilla), free);
        Assert.True(ok.Ok);
        Assert.Equal(img.GetSquad(AstonVilla).LowestFreeNumber(), ok.Squad.Players[^1].ShirtNumber);
    }

    [FixtureFact]
    public void Transfer_does_not_write_when_buyer_is_full()
    {
        var img = Fixtures.Image("orig.OPT");
        var full = new ClubSquad(AstonVilla, Enumerable.Range(0, 32).Select(i => new SquadSlot(4000 + i, i + 1)).ToList());
        img.SetSquad(full);
        var before = OptionFileCodec.Encode(img);
        var (_, to) = new SquadRules(img).Transfer(Arsenal, AstonVilla, Gyokeres);
        Assert.Equal(SquadError.SquadFull, to.Error);
        Assert.Equal(before, OptionFileCodec.Encode(img));
    }

    [Fact]
    public void Shirt_number_rules()
    {
        var s = new ClubSquad(5, [new(10, 1), new(11, 7), new(12, 7), new(13, 0), new(14, 2)]);
        var (n, changes) = SquadRules.NormalizeShirtNumbers(s);
        Assert.Equal([1, 7, 3, 4, 2], n.Players.Select(p => p.ShirtNumber));
        Assert.Equal([12, 13], changes.Select(c => c.PlayerId));
        Assert.Equal(5, n.LowestFreeNumber());
    }

    [FixtureFact]
    public void Set_shirt_number_rejects_taken_and_out_of_range()
    {
        var img = Fixtures.Image("orig.OPT");
        var rules = new SquadRules(img);
        var s = img.GetSquad(Arsenal);
        var a = s.Players[0];
        Assert.Equal(SquadError.ShirtNumberTaken, rules.SetShirtNumber(s, a.PlayerId, s.Players[1].ShirtNumber).Error);
        Assert.Equal(SquadError.InvalidShirtNumber, rules.SetShirtNumber(s, a.PlayerId, 100).Error);
        var free = s.LowestFreeNumber()!.Value;
        Assert.Equal(free, rules.SetShirtNumber(s, a.PlayerId, free).Squad.Players[0].ShirtNumber);
    }
}

public class ValidatorAndBuilderTests
{
    private const int AstonVilla = 1;

    [FixtureFact]
    public void Original_patch_has_duplicate_shirt_numbers_and_builder_rejects_until_normalized()
    {
        var raw = Fixtures.Bytes("orig.OPT");
        var img = OptionFileCodec.Decode(raw);
        var villa = img.GetSquad(AstonVilla);
        Assert.Contains(OptionFileValidator.Validate(img, [AstonVilla]), i => i.Code == "DUPLICATE_SHIRT_NUMBER");

        var ex = Assert.Throws<OptionFileValidationException>(() => OptionFileBuilder.Build(raw, [villa]));
        Assert.Contains(ex.Issues, i => i.Code == "DUPLICATE_SHIRT_NUMBER");

        var (fixedVilla, changes) = SquadRules.NormalizeShirtNumbers(villa);
        Assert.NotEmpty(changes);
        var built = OptionFileCodec.Decode(OptionFileBuilder.Build(raw, [fixedVilla]));
        Assert.Equal(fixedVilla, built.GetSquad(AstonVilla), new SquadComparer());
    }

    [FixtureFact]
    public void Name_and_club_name_limits()
    {
        var img = Fixtures.Image("orig.OPT");
        Assert.Throws<ArgumentException>(() => img.SetPlayerName(1598, new string('x', 16)));
        img.SetPlayerName(1598, "Nguyễn Văn Đức");
        img.SetPlayerName(1599, "Piero Hincapié");
        img.SetClubName(AstonVilla, "Hà Nội FC", "HAN");
        var back = OptionFileCodec.Decode(OptionFileCodec.Encode(img));
        Assert.Equal("Nguyen Van Duc", back.GetPlayerName(1598));   // game không hiện được ễ, ă, Đ → bỏ dấu
        Assert.Equal("Piero Hincapié", back.GetPlayerName(1599));   // Latin-1 giữ nguyên
        Assert.Equal("Ha Noi FC", back.GetClubName(AstonVilla));
        Assert.Equal("HAN", back.GetClubAbbreviation(AstonVilla));
    }

    private sealed class SquadComparer : IEqualityComparer<ClubSquad>
    {
        public bool Equals(ClubSquad? x, ClubSquad? y) => x!.Club == y!.Club && x.Players.SequenceEqual(y.Players);
        public int GetHashCode(ClubSquad obj) => obj.Club;
    }
}
