using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Infrastructure;

namespace Pes17League.Clubs.Application;

public sealed record FreeMarketOpenResult(Guid WindowId, int Prices, int Wages, int Budgets);

/// <summary>
/// Chợ chuyển nhượng tự do PES17 (PO chọn 18/11/2026): không theo vòng, không khoá vòng. Một kỳ CN không gắn khung ngày
/// (MatchdayId null), mở được cả khi mùa chưa bắt đầu. Kinh tế giữ như PES6: lúc mở chợ tính giá, lương hợp đồng nhập,
/// chia ngân sách đầu mùa theo tham số đã lưu ở Quản trị → Kinh tế (chia 1 lần; quỹ 0 thì bù sau bằng "Bù ngân sách").
/// Đội hình đổi → tools/pes17db/sync.sh tự đóng gói vào game (khi game không đang đá).
/// </summary>
public sealed class FreeMarketService(ClubsDbContext db, TimeProvider clock, PriceModelOptions? priceOptions = null, SquadPolicyOptions? squadOptions = null)
{
    public static readonly TimeSpan Length = TimeSpan.FromDays(3650);

    public Task<TransferWindow?> WindowAsync(Guid seasonId, CancellationToken ct = default) =>
        db.TransferWindows.Where(w => w.SeasonId == seasonId && w.MatchdayId == null && w.Status == TransferWindowStatus.Open)
            .OrderByDescending(w => w.OpensAt).FirstOrDefaultAsync(ct);

    public async Task<FreeMarketOpenResult> OpenAsync(Guid seasonId, string actor, CancellationToken ct = default)
    {
        var season = await db.Seasons.FindAsync([seasonId], ct) ?? throw new AdminRuleException("Không tìm thấy mùa");
        if (season.Status == SeasonStatus.Finished) throw new AdminRuleException($"{season.Name} đã xong");
        if (season.PatchVersionId is null) throw new AdminRuleException($"{season.Name} chưa nhập patch");
        if (await WindowAsync(seasonId, ct) is not null) throw new AdminRuleException("Chợ tự do đã mở");
        // Quỹ chia đúng 1 lần lúc mở → không mở khi PO chưa đặt quỹ (tránh chia 0).
        if ((await db.SeasonEconomies.FindAsync([seasonId], ct))?.InitialBudgetFund is not > 0)
            throw new AdminRuleException("Chưa đặt quỹ ngân sách: nhập quỹ rồi bấm Lưu tham số trước khi mở chợ");

        var now = clock.GetUtcNow();
        var w = new TransferWindow { SeasonId = seasonId, OpensAt = now, ClosesAt = now + Length, Status = TransferWindowStatus.Open };
        db.TransferWindows.Add(w);
        db.AdminAudit.Add(new AdminAuditEntry { At = now, Actor = actor, EntityType = nameof(Season), EntityId = seasonId, Action = "OpenFreeMarket" });
        await db.SaveChangesAsync(ct);

        var eco = new EconomyService(db, clock, priceOptions, squadOptions);
        var prices = await eco.RecalculatePricesAsync(seasonId, ct);
        var wages = await eco.SetImportedWagesAsync(seasonId, ct);
        var budgets = await eco.AllocateInitialBudgetsAsync(seasonId, ct);
        return new(w.Id, prices, wages, budgets.Count);
    }

    /// <summary>
    /// Admin chuyển cầu thủ sang <paramref name="toClubId"/> (null = thả tự do) qua <see cref="TransferService"/>: đúng luật squad,
    /// phí = giá mùa (CLB bán là BOT), CLB mua trả từ ngân sách, hợp đồng mặc định của mùa, lương = giá × ‰.
    /// </summary>
    public async Task<TransferResult> AdminMoveAsync(Guid seasonId, Guid playerId, Guid? toClubId, string actor, CancellationToken ct = default)
    {
        var w = await WindowAsync(seasonId, ct) ?? throw new AdminRuleException("Chợ tự do chưa mở (Quản trị → Kinh tế)");
        var transfers = new TransferService(db, clock, squadOptions);
        TransferResult r;
        if (toClubId is { } to)
        {
            var price = await db.PlayerPrices.FindAsync([playerId, seasonId], ct);
            var eco = await db.SeasonEconomies.FindAsync([seasonId], ct) ?? new SeasonEconomy { SeasonId = seasonId };
            var fee = price?.Price ?? 0;
            r = await transfers.BuyAsync(w.Id, playerId, to, fee, eco.DefaultContractMatches, ContractRules.Wage(fee, eco.WagePermille), ct);
        }
        else r = await transfers.ReleaseAsync(w.Id, playerId, ct);
        if (r.Ok)
        {
            db.AdminAudit.Add(new AdminAuditEntry { At = clock.GetUtcNow(), Actor = actor, EntityType = "Player", EntityId = playerId,
                Action = toClubId is null ? "AdminRelease" : "AdminTransfer", After = JsonSerializer.Serialize(new { ToClubId = toClubId, r.TransferId }) });
            await db.SaveChangesAsync(ct);
        }
        return r;
    }
}
