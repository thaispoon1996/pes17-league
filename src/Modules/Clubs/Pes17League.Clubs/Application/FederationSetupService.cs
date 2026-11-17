using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Infrastructure;

namespace Pes17League.Clubs.Application;

/// <summary>CLB liên đoàn trong OF: ô PES (0–139) + mã nước kiểu FIFA.</summary>
public sealed record FederationClub(int PesClubIndex, string Country);

public sealed record FederationSetupResult(Guid SeasonId, IReadOnlyList<Guid> ArchivedSeasons, int ArchivedClubs, ImportPatchResult Import);

/// <summary>
/// Liên đoàn bước 2 (docs/domain-admin-design.md mục 6–7, PO duyệt 05/11): dừng + lưu trữ mùa alpha, tạo mùa đầu của liên đoàn,
/// nhập lại từ patch toàn bộ CLB châu Âu (<see cref="Clubs"/>) kèm cầu thủ, nước, logo. PO chọn "Làm lại từ patch": CLB/cầu thủ alpha
/// giữ nguyên trong DB (Inactive, hết điều khiển) để xem lại, không mang sang mùa mới. Mọi CLB mới là BOT ("Chọn lại sau").
/// Chưa sinh lịch/giải (bước 4–6).
/// </summary>
public sealed class FederationSetupService(ClubsDbContext db, IOptionFileStore store, TimeProvider clock)
{
    /// <summary>4 giải quốc gia (Anh 0–19, TBN 20–39, Ý 40–59, Đức 60–77) + 23 CLB châu Âu lẻ chọn được trong game
    /// (trang Uefa League 78–97, Other Clubs 118/119/121; Slovan 125 không có trong menu). Bỏ đội 2006, V-League, đội ảo (Q4).</summary>
    public static readonly IReadOnlyList<FederationClub> Clubs =
    [
        .. Enumerable.Range(0, 20).Select(i => new FederationClub(i, "ENG")),
        .. Enumerable.Range(20, 20).Select(i => new FederationClub(i, "ESP")),
        .. Enumerable.Range(40, 20).Select(i => new FederationClub(i, "ITA")),
        .. Enumerable.Range(60, 18).Select(i => new FederationClub(i, "GER")),
        new(78, "NED"), new(79, "FRA"), new(80, "POR"), new(81, "TUR"), new(82, "SCO"), new(83, "BEL"), new(84, "UKR"), new(85, "NED"),
        new(86, "TUR"), new(87, "FRA"), new(88, "FRA"), new(89, "FRA"), new(90, "FRA"), new(91, "POR"), new(92, "FRA"), new(93, "NED"),
        new(94, "AUT"), new(95, "UKR"), new(96, "POR"), new(97, "RUS"), new(118, "SUI"), new(119, "AUT"), new(121, "TUR"),
    ];

    /// <summary>Logo chụp từ màn chọn đội, đặt trong wwwroot của web theo ô PES.</summary>
    public static string LogoPath(int pesClubIndex) => $"/img/clubs/{pesClubIndex}.png";

    /// <summary>Hợp đồng nhập + gia hạn mặc định mùa đầu (PO chọn 05/11): 36 trận ≈ hết mùa đầu (khoảng 33 khung ngày).</summary>
    public const int FirstSeasonContractMatches = 36;

    public async Task<FederationSetupResult> SetupAsync(string patchName, byte[] baseOptionFile, string seasonName, string actor,
        int contractMatches = FirstSeasonContractMatches, CancellationToken ct = default)
    {
        if (!ContractRules.IsValidLength(contractMatches)) throw new ArgumentOutOfRangeException(nameof(contractMatches), "Số trận ngoài 10–60");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var now = clock.GetUtcNow();
        if (await db.Seasons.AnyAsync(s => s.Status == SeasonStatus.Planned, ct))
            throw new InvalidOperationException("Đã có mùa chưa bắt đầu");

        // Mùa alpha dừng tại chỗ (Q3): mùa + giải → Finished, kỳ CN đang mở đóng lại; vòng/trận giữ nguyên trạng thái để xem lại.
        var archived = await db.Seasons.Where(s => s.Status == SeasonStatus.Active).ToListAsync(ct);
        var archivedIds = archived.Select(s => s.Id).ToList();
        foreach (var s in archived) s.Status = SeasonStatus.Finished;
        foreach (var e in await db.Editions.Where(e => archivedIds.Contains(e.SeasonId) && e.Status != EditionStatus.Finished).ToListAsync(ct))
            e.Status = EditionStatus.Finished;
        foreach (var w in await db.TransferWindows.Where(w => archivedIds.Contains(w.SeasonId) && w.Status != TransferWindowStatus.Closed).ToListAsync(ct))
            w.Status = TransferWindowStatus.Closed;
        var oldClubs = await db.Clubs.Where(c => c.Status == ClubStatus.Active).ToListAsync(ct);
        foreach (var c in oldClubs) c.Status = ClubStatus.Inactive;
        foreach (var a in await db.TeamAssignments.Where(a => a.To == null).ToListAsync(ct)) a.To = now;
        await db.SaveChangesAsync(ct);

        var season = await new CompetitionService(db, store, clock).CreateSeasonAsync(seasonName, await db.Seasons.CountAsync(ct) + 1, ct);
        var import = await new ImportPatchService(db, store, clock, importOptions: new(ContractMatches: contractMatches)).ImportAsync(patchName, baseOptionFile, season.Id,
            [.. Clubs.Select(c => new LeagueClub(c.PesClubIndex))], ct);
        season.PatchVersionId = import.PatchVersionId;
        await new EconomyService(db, clock).SetParametersAsync(season.Id, new(DefaultContractMatches: contractMatches), ct);

        var country = Clubs.ToDictionary(c => c.PesClubIndex, c => c.Country);
        var mapped = await (from m in db.ClubPesMappings.Where(m => m.PatchVersionId == import.PatchVersionId)
                            join c in db.Clubs on m.ClubId equals c.Id
                            select new { Club = c, m.PesClubIndex }).ToListAsync(ct);
        foreach (var x in mapped)
        {
            x.Club.Country = country[x.PesClubIndex];
            x.Club.LogoPath = LogoPath(x.PesClubIndex);
        }
        db.AdminAudit.Add(new AdminAuditEntry
        {
            At = now, Actor = actor, EntityType = nameof(Season), EntityId = season.Id, Action = "FederationSetup",
            Before = JsonSerializer.Serialize(new { ArchivedSeasons = archivedIds, ArchivedClubs = oldClubs.Count }),
            After = JsonSerializer.Serialize(new { Patch = patchName, import.Clubs, import.SquadPlayers, import.FreeAgents }),
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new(season.Id, archivedIds, oldClubs.Count, import);
    }

    /// <summary>Đổi số trận hợp đồng nhập của mùa chưa bắt đầu (mùa 1 nhập với 10 trước khi PO chọn 36): hợp đồng Active chưa đá, chưa có lương
    /// của CLB thuộc patch mùa → <paramref name="matches"/> trận; gia hạn mặc định của mùa đổi theo. Trả số hợp đồng đã đổi.</summary>
    public async Task<int> SetImportedContractLengthAsync(Guid seasonId, int matches, string actor, CancellationToken ct = default)
    {
        if (!ContractRules.IsValidLength(matches)) throw new ArgumentOutOfRangeException(nameof(matches), "Số trận ngoài 10–60");
        var season = await db.Seasons.SingleAsync(s => s.Id == seasonId, ct);
        if (season.Status != SeasonStatus.Planned || season.PatchVersionId is not { } patch)
            throw new InvalidOperationException("Chỉ đổi được khi mùa chưa bắt đầu và đã nhập patch");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var clubs = db.ClubPesMappings.Where(m => m.PatchVersionId == patch).Select(m => m.ClubId);
        var contracts = await db.Contracts.Where(c => clubs.Contains(c.ClubId) && c.Status == ContractStatus.Active && c.MatchesPlayed == 0 && c.Wage == null)
            .ToListAsync(ct);
        var before = contracts.GroupBy(c => c.MatchesTotal?.ToString() ?? "null").ToDictionary(g => g.Key, g => g.Count());
        var oldDefault = (await db.SeasonEconomies.FindAsync([seasonId], ct))?.DefaultContractMatches;
        foreach (var c in contracts) c.MatchesTotal = matches;
        await new EconomyService(db, clock).SetParametersAsync(seasonId, new(DefaultContractMatches: matches), ct);
        db.AdminAudit.Add(new AdminAuditEntry
        {
            At = clock.GetUtcNow(), Actor = actor, EntityType = nameof(Season), EntityId = seasonId, Action = "SetImportedContractLength",
            Before = JsonSerializer.Serialize(new { Contracts = before, DefaultContractMatches = oldDefault }),
            After = JsonSerializer.Serialize(new { Contracts = contracts.Count, MatchesTotal = matches, DefaultContractMatches = matches }),
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return contracts.Count;
    }
}
