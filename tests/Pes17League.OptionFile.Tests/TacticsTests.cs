namespace Pes17League.OptionFile.Tests;

/// <summary>Sơ đồ/thiết lập đội (PO duyệt 03/11). Offset đã kiểm trong game: 3-4-3 A ghi cho MU → radar 3 hậu vệ.</summary>
public class TacticsTests
{
    private const int ManUtd = 10, Arsenal = 0;

    [FixtureFact]
    public void Reads_patch_formation()
    {
        var t = Fixtures.Image("orig.OPT").GetTactics(ManUtd);
        Assert.Equal("4-5-1 B", t.Formation);   // MU trong patch 25-26 (đọc thẳng bytes lúc spike)
        Assert.All(new[] { t.BackLine, t.Pressure, t.OffsideTrap, t.CounterAttack }, v => Assert.InRange(v!.Value, 0, 2));
    }

    [FixtureFact]
    public void Build_writes_formation_and_settings_only_for_given_club()
    {
        var orig = Fixtures.Bytes("orig.OPT");
        var before = OptionFileCodec.Decode(orig);
        var data = OptionFileBuilder.Build(orig, [], tactics: new Dictionary<int, TeamTactics> { [ManUtd] = new("3-4-3 A", BackLine: 2, CounterAttack: 1) });
        var img = OptionFileCodec.Decode(data);

        var t = img.GetTactics(ManUtd);
        var old = before.GetTactics(ManUtd);
        Assert.Equal(("3-4-3 A", 2, old.Pressure, old.OffsideTrap, 1), (t.Formation, t.BackLine, t.Pressure, t.OffsideTrap, t.CounterAttack));
        Assert.Equal(old.Strategies, t.Strategies);
        Assert.Equal(old.Jobs, t.Jobs);
        Assert.Equal(Key(before.GetTactics(Arsenal)), Key(img.GetTactics(Arsenal)));
        Assert.Equal(before.GetSquad(ManUtd), img.GetSquad(ManUtd), new SquadComparer());
    }

    /// <summary>Giai đoạn 2 (spike 05/11): vị trí tự do (cả 3 bộ), chiến thuật L2 (102..105), người đá phạt/đội trưởng (111..116).</summary>
    [FixtureFact]
    public void Build_writes_positions_strategies_and_jobs()
    {
        var orig = Fixtures.Bytes("orig.OPT");
        var before = OptionFileCodec.Decode(orig);
        var pos = Formations.Layout("4-4-2 A").Positions.ToList();
        pos[0] = (42, 30);
        var data = OptionFileBuilder.Build(orig, [], tactics: new Dictionary<int, TeamTactics>
        {
            [ManUtd] = new("4-4-2 A", Positions: pos, Strategies: [1, null, 6, 9], Jobs: [1, 2, 3, 4, 5, 7]),
        });
        var img = OptionFileCodec.Decode(data);

        var t = img.GetTactics(ManUtd);
        Assert.Null(t.Formation);                                   // vị trí khác mẫu → không còn khớp tên mẫu
        Assert.Equal(pos, img.GetFormationLayout(ManUtd).Positions);
        Assert.Equal([1, before.GetTactics(ManUtd).Strategies![1], 6, 9], t.Strategies);
        Assert.Equal([1, 2, 3, 4, 5, 7], t.Jobs);
        Assert.Equal(Key(before.GetTactics(Arsenal)), Key(img.GetTactics(Arsenal)));
    }

    [FixtureFact]
    public void Rejects_out_of_range()
    {
        var img = Fixtures.Image("orig.OPT");
        Assert.Throws<ArgumentOutOfRangeException>(() => img.SetTactics(ManUtd, new(null, Strategies: [12, 0, 0, 0])));
        Assert.Throws<ArgumentOutOfRangeException>(() => img.SetTactics(ManUtd, new(null, Jobs: [32, 0, 0, 0, 0, 0])));
        Assert.Throws<ArgumentOutOfRangeException>(() => img.SetTactics(ManUtd, new(null, Positions: [.. Enumerable.Repeat((60, 50), 10)])));
    }

    private static string Key(TeamTactics t) =>
        $"{t.Formation}|{t.BackLine}{t.Pressure}{t.OffsideTrap}{t.CounterAttack}|{string.Join(',', t.Strategies ?? [])}|{string.Join(',', t.Jobs ?? [])}";

    [Fact]
    public void Roles_of_preset()
    {
        Assert.Equal(["GK", "CB", "CB", "CB", "DM", "RM", "LM", "AM", "RW", "LW", "CF"], Formations.Roles("3-4-3 A"));
        Assert.Equal(21, Formations.Names.Distinct().Count());
        Assert.Throws<ArgumentException>(() => Formations.Roles("2-3-5"));
    }

    private sealed class SquadComparer : IEqualityComparer<ClubSquad>
    {
        public bool Equals(ClubSquad? a, ClubSquad? b) => a!.Club == b!.Club && a.Players.SequenceEqual(b.Players);
        public int GetHashCode(ClubSquad s) => s.Club;
    }
}
