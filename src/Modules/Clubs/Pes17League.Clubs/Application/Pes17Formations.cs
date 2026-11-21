using System.Text.Json;
using Pes17League.Clubs.Domain;
using Pes17League.OptionFile;

namespace Pes17League.Clubs.Application;

/// <summary>
/// Sơ đồ đội PES17 (PO 21/11/2026): vai trò PES17 (0 GK, 1 CB, 2 LB, 3 RB, 4 DMF, 5 CMF, 6 LMF, 7 RMF, 8 AMF, 9 LWF, 10 RWF, 11 SS, 12 CF)
/// ↔ tên vai trò web (bộ sơ đồ PES6: <see cref="Formations"/>). PES17 không có LWB/RWB/libero → LB/RB/CB.
/// Toạ độ chung một hệ: X sâu (GK 3 … CF 43), Y ngang (trái ~15 … phải ~89). Dữ liệu game: tools/pes17db/pesdb.py.
/// </summary>
public static class Pes17Formations
{
    private static readonly string[] Names = ["GK", "CB", "LB", "RB", "DM", "CM", "LM", "RM", "AM", "LW", "RW", "SS", "CF"];

    public static int Code(string role) => role switch
    {
        "GK" => 0, "CB" or "CWP" or "ASW" => 1, "LB" or "LWB" => 2, "RB" or "RWB" => 3,
        _ => Array.IndexOf(Names, role) is var i and > 0 ? i : throw new ArgumentException($"Vai trò không có trong PES17: {role}", nameof(role)),
    };

    public static string Name(int code) => code >= 0 && code < Names.Length ? Names[code] : "CM";

    /// <summary>Sơ đồ web của CLB (null = CLB chưa chọn sơ đồ/vị trí → giữ sơ đồ game).</summary>
    public static FormationLayout? WebLayout(ClubTactics? t)
    {
        if (t is null) return null;
        var b = t.Formation is { } f && Formations.Names.Contains(f) ? Formations.Layout(f) : null;
        var positions = TacticsService.PositionsOf(t) ?? b?.Positions;
        if (positions is null) return null;
        var roles = TacticsService.RolesOf(t) is { } codes ? codes.Select(Formations.RoleName).ToList()
            : b is not null ? [.. b.Roles.Skip(1)]
            : positions.Select(p => FormationRoles.At(p.X, p.Y)).ToList();
        return new FormationLayout(positions, ["GK", .. roles]);
    }

    /// <summary>11 ô [vai trò PES17, X, Y] (ô 0 = GK) cho khoá "_formations" của pesdb.py apply.</summary>
    public static int[][] ToGame(FormationLayout l) =>
        [[0, 3, 52], .. l.Positions.Select((p, i) => new[] { Code(l.Roles[i + 1]), p.X, p.Y })];
}

/// <summary>Sơ đồ game từng đội (pesdb.py formations → pesdb/formations.json): làm sơ đồ "Theo game" trên web cho CLB chưa chọn sơ đồ.</summary>
public sealed class Pes17GameFormations(string path)
{
    private Dictionary<int, FormationLayout>? cache;

    public FormationLayout? Of(int pesClubIndex)
    {
        cache ??= Load();
        return cache.GetValueOrDefault(pesClubIndex);
    }

    private Dictionary<int, FormationLayout> Load()
    {
        if (!File.Exists(path)) return [];
        var raw = JsonSerializer.Deserialize<Dictionary<string, int[][]>>(File.ReadAllText(path)) ?? [];
        return raw.Where(kv => kv.Value.Length == 11).ToDictionary(kv => int.Parse(kv.Key), kv => new FormationLayout(
            [.. kv.Value.Skip(1).Select(r => (r[1], r[2]))], [.. kv.Value.Select(r => Pes17Formations.Name(r[0]))]));
    }
}
