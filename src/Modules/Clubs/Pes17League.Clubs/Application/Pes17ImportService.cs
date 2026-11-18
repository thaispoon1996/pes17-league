using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Domain;
using Pes17League.Clubs.Infrastructure;

namespace Pes17League.Clubs.Application;

/// <summary>Kết quả nhập dữ liệu PES 2017 (pesdb.json do tools/pes17db/pesdb.py xuất).</summary>
public sealed record Pes17ImportResult(Guid PatchVersionId, int Clubs, int SquadPlayers, int FreeAgents, int ShirtChanges,
    IReadOnlyList<AddedGoalkeeper> AddedGoalkeepers, IReadOnlyList<GoalkeeperShortage> GoalkeeperShortages, IReadOnlyList<int> DuplicatePlayers);

/// <summary>
/// Nhập patch PES 2017 (PO chọn 17/11/2026 phương án C): toàn bộ CLB (bỏ 127 đội tuyển) cùng đội hình, cầu thủ không thuộc
/// CLB nào thành tự do. Mọi CLB là BOT, Active; tham gia giải nào do bước tạo giải/hạng quyết định sau.
/// Mã PES lưu trong ClubPesMapping.PesClubIndex = mã đội PES17, PlayerPesMapping.PesPlayerId = mã cầu thủ PES17.
/// Chỉ số: tên trùng module OF PES6 khi nghĩa tương đương (để web/BOT dùng lại), còn lại tên PES17 (PES17_*).
/// </summary>
public sealed class Pes17ImportService(ClubsDbContext db, IOptionFileStore store, TimeProvider clock, SquadPolicyOptions? options = null, ImportOptions? importOptions = null)
{
    private readonly SquadPolicy<int> _policy = new(options);
    private readonly ImportOptions _import = importOptions ?? new();

    /// <summary>Tên PES17 trong pesdb.json → khoá lưu trong PlayerAttributes.Values.</summary>
    public static readonly IReadOnlyDictionary<string, string> AttributeKeys = new Dictionary<string, string>
    {
        ["age"] = "AGE", ["height"] = "HEIGHT", ["weight"] = "WEIGHT", ["form"] = "CONDITION / FITNESS",
        ["stamina"] = "STAMINA", ["speed"] = "TOP SPEED", ["explosivePower"] = "ACCELERATION", ["header"] = "HEADING",
        ["jump"] = "JUMP", ["goalkeeping"] = "GOAL KEEPING", ["attackingProwess"] = "ATTACK", ["defensiveProwess"] = "DEFENSE",
        ["physicalContact"] = "BALANCE", ["dribbling"] = "DRIBBLE ACCURACY", ["lowPass"] = "SHORT PASS ACCURACY",
        ["loftedPass"] = "LONG PASS ACCURACY", ["finishing"] = "SHOT ACCURACY", ["kickingPower"] = "SHOT POWER",
        ["setPieceTaking"] = "FREE KICK ACCURACY", ["curve"] = "SWERVE", ["ballControl"] = "TECHNIQUE",
        ["ballWinning"] = "PES17_BALL_WINNING", ["bodyControl"] = "PES17_BODY_CONTROL", ["catching"] = "PES17_CATCHING",
        ["clearing"] = "PES17_CLEARING", ["reflexes"] = "PES17_REFLEXES", ["coverage"] = "PES17_COVERAGE",
    };

    /// <summary>Vị trí PES17 (GK…CF) → enum PES6 dùng chung; giữ vị trí gốc ở khoá PES17_POSITION.</summary>
    public static Position MapPosition(string p) => p switch
    {
        "GK" => Position.GK, "CB" => Position.CB, "LB" or "RB" => Position.SB, "DMF" => Position.DMF, "CMF" => Position.CMF,
        "LMF" or "RMF" => Position.SMF, "AMF" => Position.AMF, "LWF" or "RWF" => Position.WF, "SS" => Position.SS, "CF" => Position.CF,
        _ => throw new FormatException($"Vị trí PES17 lạ: {p}"),
    };

    public static readonly string[] Pes17Positions = ["GK", "CB", "LB", "RB", "DMF", "CMF", "LMF", "RMF", "AMF", "LWF", "RWF", "SS", "CF"];

    private sealed record P17Player(int Id, string Name, Position Position, Dictionary<string, int> Values);
    private sealed record P17Team(int Id, string Name, bool National);
    private sealed record P17Assign(int Player, int Team, int Shirt, int Order);

    public async Task<Pes17ImportResult> ImportAsync(string patchName, byte[] pesdbJson, Guid seasonId, CancellationToken ct = default)
    {
        if (await db.PatchVersions.AnyAsync(p => p.Name == patchName, ct))
            throw new InvalidOperationException($"Patch '{patchName}' đã nhập");
        if (!ContractRules.IsValidLength(_import.ContractMatches))
            throw new ArgumentOutOfRangeException(nameof(importOptions), "ContractMatches ngoài 10–60");

        var (playersById, teams, assigns) = Parse(pesdbJson);   // lỗi định dạng → ném trước khi ghi gì
        var now = clock.GetUtcNow();
        var sha = Hash.Sha256(pesdbJson);
        var patch = new PatchVersion
        {
            Name = patchName,
            BaseOptionFileSha256 = sha,
            BaseOptionFilePath = await store.SaveAsync($"base/{sha}.pesdb.json", pesdbJson, ct),
            ImportedAt = now,
        };
        db.PatchVersions.Add(patch);

        var clubTeams = teams.Where(t => !t.National).ToDictionary(t => t.Id);
        // Đội hình CLB theo thứ tự trong game; cầu thủ ở 2 CLB (lỗi dữ liệu) giữ CLB gặp trước.
        var originTeam = new Dictionary<int, int>();
        var duplicates = new List<int>();
        var squads = new Dictionary<int, List<P17Assign>>();
        foreach (var a in assigns.Where(a => clubTeams.ContainsKey(a.Team) && playersById.ContainsKey(a.Player))
                     .OrderBy(a => a.Team).ThenBy(a => a.Order))
        {
            if (!originTeam.TryAdd(a.Player, a.Team)) { duplicates.Add(a.Player); continue; }
            if (!squads.TryGetValue(a.Team, out var list)) squads[a.Team] = list = [];
            list.Add(a);
        }

        var dbPlayers = new Dictionary<int, FootballPlayer>();
        FootballPlayer PlayerFor(int pesId, Guid? originClubId)
        {
            if (dbPlayers.TryGetValue(pesId, out var p)) return p;
            var src = playersById[pesId];
            p = new FootballPlayer { Name = src.Name, Position = src.Position, OriginClubId = originClubId };
            dbPlayers[pesId] = p;
            db.Players.Add(p);
            db.PlayerPesMappings.Add(new PlayerPesMapping
            {
                PlayerId = p.Id, PatchVersionId = patch.Id, PesPlayerId = pesId,
                OriginPesClubIndex = originTeam.TryGetValue(pesId, out var o) ? o : null,
            });
            db.PlayerAttributes.Add(new PlayerAttributes { PlayerId = p.Id, SeasonId = seasonId, CapturedAt = now, Values = src.Values });
            return p;
        }

        var free = playersById.Values.Where(p => !originTeam.ContainsKey(p.Id) && !string.IsNullOrWhiteSpace(p.Name)).Select(p => p.Id).ToList();
        var freeGoalkeepers = new Queue<int>(free
            .Where(id => playersById[id].Position == Position.GK && Gk(id) >= _import.GoalkeeperFloor)
            .OrderBy(Gk).ThenBy(id => playersById[id].Values.GetValueOrDefault("AGE")).ThenBy(id => id));
        int Gk(int id) => playersById[id].Values.GetValueOrDefault("GOAL KEEPING");

        var addedGks = new List<AddedGoalkeeper>();
        var shortages = new List<GoalkeeperShortage>();
        int squadPlayers = 0, shirtChanges = 0, clubCount = 0;
        foreach (var (teamId, squad) in squads.OrderBy(s => s.Key))
        {
            var club = new Club { Name = ClubNames.Pretty(clubTeams[teamId].Name) };
            db.Clubs.Add(club);
            db.ClubPesMappings.Add(new ClubPesMapping { ClubId = club.Id, PatchVersionId = patch.Id, PesClubIndex = teamId });
            db.TeamAssignments.Add(new TeamAssignment { ClubId = club.Id, ControllerType = ControllerType.Bot, From = now });
            clubCount++;

            var (entries, fixes) = SquadPolicy<int>.NormalizeShirtNumbers(squad.Take(_policy.Options.MaxSquadSize)
                .Select(a => new SquadEntry<int>(a.Player, a.Shirt, playersById[a.Player].Position)).ToList());
            // Quá MaxSquadSize: phần thừa thành tự do (không mất cầu thủ).
            foreach (var extra in squad.Skip(_policy.Options.MaxSquadSize)) { originTeam.Remove(extra.Player); free.Add(extra.Player); }
            var added = new List<int>();
            while (entries.Count(e => e.Position == Position.GK) < _policy.Options.MinGoalkeepers && freeGoalkeepers.Count > 0)
            {
                var r = _policy.Add(entries, freeGoalkeepers.Peek(), Position.GK);
                if (!r.Ok) break;
                added.Add(freeGoalkeepers.Dequeue());
                entries = r.Squad;
            }
            for (var i = 0; i < entries.Count; i++)
            {
                var p = PlayerFor(entries[i].PlayerId, added.Contains(entries[i].PlayerId) ? null : club.Id);
                db.SquadMemberships.Add(new SquadMembership
                {
                    ClubId = club.Id, PlayerId = p.Id, ShirtNumber = entries[i].ShirtNumber, SlotOrder = i, JoinedAt = now,
                });
                db.Contracts.Add(new PlayerContract { ClubId = club.Id, PlayerId = p.Id, MatchesTotal = _import.ContractMatches, StartedAt = now });
                squadPlayers++;
            }
            addedGks.AddRange(added.Select(id => new AddedGoalkeeper(club.Id, club.Name, dbPlayers[id].Id, dbPlayers[id].Name,
                Gk(id), entries.Single(e => e.PlayerId == id).ShirtNumber)));
            if (entries.Count(e => e.Position == Position.GK) is var gk && gk < _policy.Options.MinGoalkeepers)
                shortages.Add(new(club.Id, club.Name, gk));
            shirtChanges += fixes.Count;
        }

        var freeAgents = 0;
        foreach (var id in free)
        {
            if (dbPlayers.ContainsKey(id)) continue;   // GK đã bổ sung vào CLB
            PlayerFor(id, null);
            freeAgents++;
        }

        await db.SaveChangesAsync(ct);
        return new(patch.Id, clubCount, squadPlayers, freeAgents, shirtChanges, addedGks, shortages, duplicates);
    }

    private static (Dictionary<int, P17Player>, List<P17Team>, List<P17Assign>) Parse(byte[] json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var players = new Dictionary<int, P17Player>();
        foreach (var e in root.GetProperty("players").EnumerateArray())
        {
            var values = new Dictionary<string, int>();
            foreach (var (src, key) in AttributeKeys)
                values[key] = e.GetProperty(src).GetInt32();
            var pos = e.GetProperty("position").GetString() ?? "";
            values["PES17_POSITION"] = Array.IndexOf(Pes17Positions, pos);
            values["PES17_LEFT_FOOT"] = e.GetProperty("leftFoot").GetBoolean() ? 1 : 0;
            var mapped = MapPosition(pos);
            values["REGISTERED POSITION"] = (int)mapped;
            var id = e.GetProperty("id").GetInt32();
            players[id] = new P17Player(id, e.GetProperty("name").GetString() ?? "", mapped, values);
        }
        var teams = root.GetProperty("teams").EnumerateArray()
            .Select(e => new P17Team(e.GetProperty("id").GetInt32(), e.GetProperty("name").GetString() ?? "", e.GetProperty("national").GetBoolean()))
            .ToList();
        var assigns = root.GetProperty("assignments").EnumerateArray()
            .Select(e => new P17Assign(e.GetProperty("player").GetInt32(), e.GetProperty("team").GetInt32(),
                e.GetProperty("shirt").GetInt32(), e.GetProperty("order").GetInt32()))
            .ToList();
        return (players, teams, assigns);
    }
}
