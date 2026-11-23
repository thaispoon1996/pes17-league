using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Infrastructure;
using Pes17League.OptionFile;

namespace Pes17League.Clubs.Application;

/// <summary>
/// Vòng đấu PES17 (PO chọn 18/11/2026: chuyển nhượng tự do, không khoá vòng). PES17 không có OF: đội hình vào game qua
/// tools/pes17db (sync.sh / deploy.sh). "Bản dựng vòng" = ảnh chụp đội hình JSON (<see cref="Pes17SquadExportService"/>)
/// lưu như OptionFileBuild để bộ chạy trận (tools/match17) so với bản đang cài trong game và cài lại nếu khác.
/// </summary>
public sealed class Pes17RoundService(ClubsDbContext db, IOptionFileStore store, TimeProvider clock)
{
    public const string PatchPrefix = "PES17";

    public static bool IsPes17(string? patchName) => patchName?.StartsWith(PatchPrefix, StringComparison.Ordinal) == true;

    public async Task<bool> IsPes17SeasonAsync(Guid seasonId, CancellationToken ct = default) =>
        IsPes17(await (from s in db.Seasons join p in db.PatchVersions on s.PatchVersionId equals p.Id
                       where s.Id == seasonId select p.Name).FirstOrDefaultAsync(ct));

    /// <param name="fixtureId">Trận sắp đá (PO 23/11): đội hình riêng của trận (<see cref="MatchLineupService"/>) đè đội hình chính của 2 CLB trong ảnh chụp.</param>
    public async Task<OptionFileBuild> SnapshotAsync(Guid patchVersionId, Guid? matchdayId, CancellationToken ct = default,
        (Guid FixtureId, Guid[] ClubIds)? fixture = null)
    {
        string json;
        if (fixture is { } fx)
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            try
            {
                await new MatchLineupService(store).OverlayAsync(db, fx.FixtureId, fx.ClubIds, ct);
                json = (await new Pes17SquadExportService(db, clock).ExportAsync(patchVersionId, ct)).ToJson();
            }
            finally
            {
                await tx.RollbackAsync(CancellationToken.None);   // đội hình chính giữ nguyên
                db.ChangeTracker.Clear();
            }
        }
        else json = (await new Pes17SquadExportService(db, clock).ExportAsync(patchVersionId, ct)).ToJson();
        var data = Encoding.UTF8.GetBytes(json);
        var build = new OptionFileBuild
        {
            PatchVersionId = patchVersionId, MatchdayId = matchdayId, BuiltAt = clock.GetUtcNow(), SquadSnapshot = json,
            Sha256 = Hash.Sha256(data), Status = OptionFileBuildStatus.Built,
        };
        build.StoragePath = await store.SaveAsync($"rounds/{build.Id}.pes17.json", data, ct);
        db.OptionFileBuilds.Add(build);
        await db.SaveChangesAsync(ct);
        return build;
    }
}
