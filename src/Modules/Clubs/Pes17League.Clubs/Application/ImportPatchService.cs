using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Infrastructure;
using Pes17League.OptionFile;

namespace Pes17League.Clubs.Application;

/// <param name="PesClubIndex">CLB trong OF (0–139).</param>
/// <param name="Name">Tên hiển thị web (có dấu); null = lấy tên trong OF.</param>
public sealed record LeagueClub(int PesClubIndex, string? Name = null, string? ShortName = null);

public sealed record ImportedShirtChange(Guid ClubId, Guid PlayerId, string PlayerName, int From, int To);

/// <summary>GK tự do được tự thêm vào CLB thiếu thủ môn (báo admin).</summary>
public sealed record AddedGoalkeeper(Guid ClubId, string ClubName, Guid PlayerId, string PlayerName, int GoalKeeping, int ShirtNumber);

/// <summary>CLB vẫn thiếu GK sau khi bổ sung (hết GK tự do hoặc squad đủ 32) → OF vòng sẽ bị từ chối, admin phải xử lý.</summary>
public sealed record GoalkeeperShortage(Guid ClubId, string ClubName, int Goalkeepers);

public sealed record ImportPatchResult(Guid PatchVersionId, int Clubs, int SquadPlayers, int FreeAgents,
    IReadOnlyList<ImportedShirtChange> ShirtChanges, IReadOnlyList<AddedGoalkeeper> AddedGoalkeepers, IReadOnlyList<GoalkeeperShortage> GoalkeeperShortages);

/// <param name="GoalkeeperFloor">Bổ sung GK: GOAL KEEPING tối thiểu (PO chọn 70).</param>
/// <param name="ContractMatches">Số trận hợp đồng nhập từ patch (PO chọn 10).</param>
public sealed record ImportOptions(int GoalkeeperFloor = 70, int ContractMatches = ContractRules.MinMatches);

/// <summary>
/// Nhập patch đầu mùa (luồng 1): OF gốc → PatchVersion, CLB tham gia + squad, cầu thủ tự do, snapshot chỉ số mùa.
/// Chỉ nhập cầu thủ của CLB tham gia và cầu thủ tự do thật (không thuộc CLB nào trong OF);
/// cầu thủ của CLB không tham gia giải không vào DB.
/// CLB tham gia có dưới MinGoalkeepers GK → tự thêm GK tự do có GOAL KEEPING thấp nhất mà ≥ GoalkeeperFloor (hoà: trẻ hơn)
/// chưa được chọn (Kinh tế đợt 2). Mỗi cầu thủ lưu CLB gốc trong patch (hết hợp đồng thì về đó).
/// Hợp đồng nhập: ContractMatches trận, lương chưa đặt (EconomyService.SetImportedWagesAsync khi đã có giá).
/// </summary>
public sealed class ImportPatchService(ClubsDbContext db, IOptionFileStore store, TimeProvider clock, SquadPolicyOptions? options = null, ImportOptions? importOptions = null)
{
    private readonly SquadPolicy<int> _policy = new(options);
    private readonly ImportOptions _import = importOptions ?? new();

    public async Task<ImportPatchResult> ImportAsync(string patchName, byte[] baseOptionFile, Guid seasonId,
        IReadOnlyList<LeagueClub> leagueClubs, CancellationToken ct = default)
    {
        if (await db.PatchVersions.AnyAsync(p => p.Name == patchName, ct))
            throw new InvalidOperationException($"Patch '{patchName}' đã nhập");
        if (leagueClubs.Select(c => c.PesClubIndex).Distinct().Count() != leagueClubs.Count)
            throw new ArgumentException("Trùng CLB", nameof(leagueClubs));
        if (!ContractRules.IsValidLength(_import.ContractMatches))
            throw new ArgumentOutOfRangeException(nameof(importOptions), "ContractMatches ngoài 10–60");

        var image = OptionFileCodec.Decode(baseOptionFile);   // lỗi định dạng → ném trước khi ghi gì
        var now = clock.GetUtcNow();
        var sha = Hash.Sha256(baseOptionFile);
        var patch = new PatchVersion
        {
            Name = patchName,
            BaseOptionFileSha256 = sha,
            BaseOptionFilePath = await store.SaveAsync($"base/{sha}.OPT", baseOptionFile, ct),
            ImportedAt = now,
        };
        db.PatchVersions.Add(patch);

        // CLB gốc trong OF của mọi cầu thủ (kể cả CLB không tham gia); không có = tự do.
        var originIndex = new Dictionary<int, int>();
        for (var c = 0; c < OptionFileImage.ClubCount; c++)
            foreach (var s in image.GetSquad(c).Players) originIndex.TryAdd(s.PlayerId, c);

        var players = new Dictionary<int, FootballPlayer>();
        FootballPlayer PlayerFor(int pesId, Guid? originClubId)
        {
            if (players.TryGetValue(pesId, out var p)) return p;
            p = new FootballPlayer { Name = image.GetPlayerName(pesId), Position = image.GetPosition(pesId), OriginClubId = originClubId };
            players[pesId] = p;
            db.Players.Add(p);
            db.PlayerPesMappings.Add(new PlayerPesMapping
            {
                PlayerId = p.Id, PatchVersionId = patch.Id, PesPlayerId = pesId,
                OriginPesClubIndex = originIndex.TryGetValue(pesId, out var o) ? o : null,
            });
            db.PlayerAttributes.Add(new PlayerAttributes
            {
                PlayerId = p.Id,
                SeasonId = seasonId,
                CapturedAt = now,
                Values = PlayerField.Verified.ToDictionary(f => f.Name, f => image.Get(pesId, f)),
            });
            return p;
        }

        // Cầu thủ tự do: có tên, không nằm trong squad CLB nào (kể cả CLB không tham gia). Dải ẩn/ML vẫn nhập (thị trường ẩn theo
        // luật MarketVisibility, admin ép hiện được) nhưng không dùng bổ sung GK.
        var free = AllPlayerIds().Where(id => !originIndex.ContainsKey(id) && !string.IsNullOrWhiteSpace(image.GetPlayerName(id))).ToList();
        var freeGoalkeepers = new Queue<int>(free
            .Where(id => !OptionFileImage.IsHiddenPlayerId(id) && image.GetPosition(id) == Position.GK && image.Get(id, PlayerField.GoalKeeping) >= _import.GoalkeeperFloor)
            .OrderBy(id => image.Get(id, PlayerField.GoalKeeping)).ThenBy(id => image.Get(id, PlayerField.Age)).ThenBy(id => id));

        var changes = new List<ImportedShirtChange>();
        var addedGks = new List<AddedGoalkeeper>();
        var shortages = new List<GoalkeeperShortage>();
        var squadPlayers = 0;
        foreach (var lc in leagueClubs)
        {
            var club = new Club
            {
                Name = lc.Name ?? image.GetClubName(lc.PesClubIndex),
                ShortName = lc.ShortName ?? image.GetClubAbbreviation(lc.PesClubIndex),
            };
            db.Clubs.Add(club);
            db.ClubPesMappings.Add(new ClubPesMapping { ClubId = club.Id, PatchVersionId = patch.Id, PesClubIndex = lc.PesClubIndex });
            db.TeamAssignments.Add(new TeamAssignment { ClubId = club.Id, ControllerType = ControllerType.Bot, From = now });

            var squad = image.GetSquad(lc.PesClubIndex);
            var (entries, fixes) = SquadPolicy<int>.NormalizeShirtNumbers(
                squad.Players.Select(s => new SquadEntry<int>(s.PlayerId, s.ShirtNumber, image.GetPosition(s.PlayerId))).ToList());
            var added = new List<int>();
            while (entries.Count(e => e.Position == Position.GK) < _policy.Options.MinGoalkeepers && freeGoalkeepers.Count > 0)
            {
                var r = _policy.Add(entries, freeGoalkeepers.Peek(), Position.GK);
                if (!r.Ok) break;   // squad đủ 32 → giữ GK cho CLB khác
                added.Add(freeGoalkeepers.Dequeue());
                entries = r.Squad;
            }
            for (var i = 0; i < entries.Count; i++)
            {
                if (players.ContainsKey(entries[i].PlayerId))
                    throw new InvalidOperationException($"Cầu thủ PES {entries[i].PlayerId} có ở 2 CLB tham gia");
                var p = PlayerFor(entries[i].PlayerId, added.Contains(entries[i].PlayerId) ? null : club.Id);
                db.SquadMemberships.Add(new SquadMembership
                {
                    ClubId = club.Id, PlayerId = p.Id, ShirtNumber = entries[i].ShirtNumber, SlotOrder = i, JoinedAt = now,
                });
                db.Contracts.Add(new PlayerContract { ClubId = club.Id, PlayerId = p.Id, MatchesTotal = _import.ContractMatches, StartedAt = now });
                squadPlayers++;
            }
            addedGks.AddRange(added.Select(id => new AddedGoalkeeper(club.Id, club.Name, players[id].Id, players[id].Name,
                image.Get(id, PlayerField.GoalKeeping), entries.Single(e => e.PlayerId == id).ShirtNumber)));
            if (entries.Count(e => e.Position == Position.GK) is var gk && gk < _policy.Options.MinGoalkeepers)
                shortages.Add(new(club.Id, club.Name, gk));
            changes.AddRange(fixes.Select(f => new ImportedShirtChange(club.Id, players[f.PlayerId].Id, players[f.PlayerId].Name, f.From, f.To)));
        }

        var freeAgents = 0;
        foreach (var id in free)
        {
            if (players.ContainsKey(id)) continue;   // GK đã bổ sung vào CLB
            PlayerFor(id, null);
            freeAgents++;
        }

        await db.SaveChangesAsync(ct);
        return new(patch.Id, leagueClubs.Count, squadPlayers, freeAgents, changes, addedGks, shortages);
    }

    private static IEnumerable<int> AllPlayerIds() =>
        Enumerable.Range(1, OptionFileImage.PlayerCount - 1)
            .Concat(Enumerable.Range(OptionFileImage.FirstEditId, OptionFileImage.EditCount));
}
