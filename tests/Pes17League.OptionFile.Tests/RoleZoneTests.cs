namespace Pes17League.OptionFile.Tests;

/// <summary>Vai trò theo vùng (career, PO 21/11: kéo thả / chọn tay đổi vai trò, ví dụ DMF → CMF).</summary>
public class RoleZoneTests
{
    private const int ManUtd = 10;

    /// <summary>Mọi ô của 21 sơ đồ mẫu ra đúng vai trò mẫu, trừ tiền vệ giữa đứng sát biên (mẫu CM, vùng LM/RM).</summary>
    [Fact]
    public void Zones_match_presets()
    {
        foreach (var name in Formations.Names)
        {
            var layout = Formations.Layout(name);
            for (var i = 0; i < 10; i++)
            {
                var (x, y) = layout.Positions[i];
                var preset = layout.Roles[i + 1];
                var zone = FormationRoles.At(x, y);
                if (preset == "CM" && zone is "LM" or "RM") continue;
                Assert.True(preset == zone, $"{name} ô {i + 1} ({x},{y}): mẫu {preset}, vùng {zone}");
            }
        }
    }

    [Fact]
    public void Codes_round_trip_to_role_names()
    {
        foreach (var role in FormationRoles.Pickable)
            foreach (var y in new[] { 20, 52, 84 })
                Assert.Equal(role, Formations.RoleName(FormationRoles.Code(role, y)));
    }

    [Fact]
    public void Anchor_lands_in_own_zone()
    {
        foreach (var role in FormationRoles.Pickable)
            foreach (var y in new[] { 19, 40, 52, 66, 85 })
            {
                var (ax, ay) = FormationRoles.Anchor(role, y);
                Assert.Equal(role, FormationRoles.At(ax, ay));
                Assert.InRange(ax, FormationBounds.MinX, FormationBounds.MaxX);
                Assert.InRange(ay, FormationBounds.MinY, FormationBounds.MaxY);
            }
    }

    [FixtureFact]
    public void Build_writes_roles()
    {
        var orig = Fixtures.Bytes("orig.OPT");
        var layout = Formations.Layout("4-3-3 A");
        var pos = layout.Positions.ToList();
        pos[4] = FormationRoles.Anchor("CM", pos[4].Y);                                  // ô 5 DM → CM
        var roles = Enumerable.Range(0, 10).Select(i => FormationRoles.Code(FormationRoles.At(pos[i].X, pos[i].Y), pos[i].Y)).ToList();
        var img = OptionFileCodec.Decode(OptionFileBuilder.Build(orig, [], tactics: new Dictionary<int, TeamTactics>
        {
            [ManUtd] = new("4-3-3 A", Positions: pos, Roles: roles),
        }));
        var got = img.GetFormationLayout(ManUtd);
        Assert.Equal(pos, got.Positions);
        Assert.Equal("CM", got.Roles[5]);
        Assert.Equal(roles.Select(Formations.RoleName), got.Roles.Skip(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => img.SetTactics(ManUtd, new(null, Roles: [.. Enumerable.Repeat(41, 10)])));
    }
}
