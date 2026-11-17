using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Infrastructure;
using Pes17League.OptionFile;

namespace Pes17League.Clubs.Application;

/// <summary>
/// Ẩn/hiện cầu thủ trên thị trường (Players.MarketVisibility). Luật mặc định (null): cầu thủ tự do thuộc dải ẩn/ML thì ẩn.
/// Giải nghệ (tiêu chí A): tự do trong patch và không thuộc đội tuyển quốc gia nào → ghi false (admin có thể ép lại true).
/// </summary>
public sealed class MarketVisibilityService(ClubsDbContext db, IOptionFileStore store)
{
    public static bool IsVisible(bool? overrideValue, int pesPlayerId, bool inClub) =>
        overrideValue ?? (inClub || !OptionFileImage.IsHiddenPlayerId(pesPlayerId));

    public async Task SetAsync(Guid playerId, bool? visible, CancellationToken ct = default)
    {
        var p = await db.Players.SingleAsync(x => x.Id == playerId, ct);
        p.MarketVisibility = visible;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Ghi giá trị cho các ID PES của patch; trả số cầu thủ đã ghi.</summary>
    public async Task<int> SetByPesIdsAsync(Guid patchVersionId, IReadOnlyCollection<int> pesIds, bool? visible, CancellationToken ct = default)
    {
        var ids = db.PlayerPesMappings.Where(m => m.PatchVersionId == patchVersionId && pesIds.Contains(m.PesPlayerId)).Select(m => m.PlayerId);
        var players = await db.Players.Where(p => ids.Contains(p.Id)).ToListAsync(ct);
        foreach (var p in players) p.MarketVisibility = visible;
        await db.SaveChangesAsync(ct);
        return players.Count;
    }

    /// <summary>Tiêu chí A: chỉ ghi false cho người chưa bị admin ép (null), không thuộc dải ẩn/ML; trả ID PES đã ẩn.</summary>
    public async Task<IReadOnlyList<int>> HideRetiredAsync(Guid patchVersionId, CancellationToken ct = default)
    {
        var patch = await db.PatchVersions.SingleAsync(p => p.Id == patchVersionId, ct);
        var nationals = OptionFileCodec.Decode(await store.ReadAsync(patch.BaseOptionFilePath, ct)).GetNationalTeamPlayerIds();
        var rows = await (from m in db.PlayerPesMappings
                          join p in db.Players on m.PlayerId equals p.Id
                          where m.PatchVersionId == patchVersionId && m.OriginPesClubIndex == null && p.MarketVisibility == null
                          select new { p, m.PesPlayerId }).ToListAsync(ct);
        var hidden = rows.Where(r => !nationals.Contains(r.PesPlayerId) && !OptionFileImage.IsHiddenPlayerId(r.PesPlayerId)).ToList();
        foreach (var r in hidden) r.p.MarketVisibility = false;
        await db.SaveChangesAsync(ct);
        return [.. hidden.Select(r => r.PesPlayerId)];
    }
}
