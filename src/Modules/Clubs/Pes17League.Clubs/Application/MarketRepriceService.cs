using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Domain;
using Pes17League.Clubs.Infrastructure;

namespace Pes17League.Clubs.Application;

/// <summary>Một CLB trước/sau khi áp giá thật. TopUp = khoản bù ngân sách đã ghi (≥ 0).</summary>
public sealed record MarketRepriceClub(Guid ClubId, string Name, long ValueBefore, long ValueAfter, long PayrollBefore, long PayrollAfter,
    long SponsorBefore, long SponsorAfter, long TopUp, long BalanceAfter);

public sealed record MarketRepriceResult(string Season, int Players, int Contracts, long SoftCapBefore, long SoftCapAfter, long FundBefore, long FundAfter,
    double ExponentAfter, int ToppedUpClubs, long TopUpTotal, int KeptClubs, IReadOnlyList<MarketRepriceClub> Clubs);

/// <summary>
/// Áp giá thật (<see cref="PriceModelOptions.Market"/>) cho mùa đang đá (PO 22/11 "áp dụng vào mùa 1 luôn", duyệt đề xuất), 1 transaction, chỉ 1 lần mỗi mùa:
/// 1) tính lại giá mọi cầu thủ (giữ hệ số phong độ); 2) lương hợp đồng Active × (giá gốc mới / giá gốc mô hình cũ) của cầu thủ — khớp hợp đồng ký sau
/// (lương = ‰ × giá); 3) tham số kinh tế như lúc bắt đầu mùa (<see cref="Pes17CareerService.EconomyAsync"/>: trần mềm = quỹ lương CLB thứ 2, quỹ 15%,
/// k −0,7); 4) bù ngân sách đầu mùa theo cách chia mới, CHỈ CỘNG (CLB lẽ ra bị bớt giữ nguyên tiền); 5) tài trợ tính lại theo giá trị squad.
/// Giữ nguyên: chuyển nhượng đã xong, thu chi các vòng đã chốt. <paramref name="dry"/> = chạy rồi huỷ transaction (xem số).
/// </summary>
public sealed class MarketRepriceService(ClubsDbContext db, IOptionFileStore store, TimeProvider clock)
{
    public const string Action = "ApplyMarketPrices";

    public async Task<MarketRepriceResult> ApplyAsync(string actor, bool dry = false, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var season = await db.Seasons.Where(s => s.Status == SeasonStatus.Active).OrderBy(s => s.Number).FirstOrDefaultAsync(ct)
            ?? throw new AdminRuleException("Không có mùa đang đá");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM clubs.\"Seasons\" WHERE \"Id\" = {season.Id} FOR UPDATE", ct);
        if (!ReferenceEquals(PriceModelOptions.ForSeason(season.Number), PriceModelOptions.Market))
            throw new AdminRuleException($"{season.Name} không dùng mô hình giá thật");
        if (await db.AdminAudit.AnyAsync(a => a.Action == Action && a.EntityId == season.Id, ct))
            throw new AdminRuleException($"{season.Name} đã áp giá thật");
        if (!await db.Ledger.AnyAsync(e => e.Reason == LedgerReason.InitialBudget && e.RefType == nameof(SeasonEconomy) && e.RefId == season.Id, ct))
            throw new AdminRuleException("Mùa chưa chia ngân sách đầu mùa");
        if (await db.Ledger.AnyAsync(e => e.Reason == LedgerReason.Adjustment && e.RefType == EconomyService.InitialBudgetTopUp && e.RefId == season.Id, ct))
            throw new AdminRuleException("Mùa đã bù ngân sách đầu mùa (chỉ bù 1 lần)");

        var clubs = await ClubsAsync(season.Id, ct);
        var before = await SnapshotAsync(season.Id, ct);
        var ecoBefore = await db.SeasonEconomies.AsNoTracking().SingleAsync(e => e.SeasonId == season.Id, ct);
        var eco = new EconomyService(db, clock);

        // 1) Giá mới; 2) lương theo tỉ lệ giá gốc mới / giá gốc mô hình cũ (cùng chỉ số hiện tại, không phụ thuộc giá đang lưu).
        var players = await eco.RecalculatePricesAsync(season.Id, ct);
        var current = await eco.CurrentAttributesAsync(season.Id, null, ct);
        var positions = await db.Players.AsNoTracking().Select(p => new { p.Id, p.Position }).ToDictionaryAsync(p => p.Id, p => p.Position, ct);
        var newBase = await db.PlayerPrices.AsNoTracking().Where(p => p.SeasonId == season.Id).ToDictionaryAsync(p => p.PlayerId, p => p.BasePrice, ct);
        var legacy = new PriceModel(PriceModelOptions.Legacy);
        var ratio = new Dictionary<Guid, double>();
        foreach (var (id, values) in current)
            if (newBase.TryGetValue(id, out var nb) && positions.TryGetValue(id, out var pos))
            {
                var ob = legacy.BasePrice(legacy.Overall(pos, values), values.GetValueOrDefault("AGE", 27));
                if (ob > 0) ratio[id] = (double)nb / ob;
            }
        var contracts = await db.Contracts.Where(c => c.Status == ContractStatus.Active && (c.Wage != null || c.LoanParentWage != null)).ToListAsync(ct);
        var scaled = 0;
        foreach (var c in contracts)
        {
            if (!ratio.TryGetValue(c.PlayerId, out var r)) continue;
            if (c.Wage is { } w) c.Wage = (long)Math.Round(w * r, MidpointRounding.AwayFromZero);
            if (c.LoanParentWage is { } lw) c.LoanParentWage = (long)Math.Round(lw * r, MidpointRounding.AwayFromZero);
            scaled++;
        }
        await db.SaveChangesAsync(ct);

        // 3) Tham số như lúc bắt đầu mùa (giá + lương mới); 4) bù chỉ cộng; 5) tài trợ.
        var change = await new Pes17CareerService(db, store, clock).EconomyAsync(season.Id, ct);
        await eco.SetParametersAsync(season.Id, change with { WagePermille = null }, ct);
        var topUp = await eco.TopUpInitialBudgetsAsync(season.Id, ct, onlyAdd: true);
        await new FinanceService(db, clock).RefreshSponsorsAsync(season.Id, ct);

        var after = await SnapshotAsync(season.Id, ct);
        var rows = clubs.Select(c => new MarketRepriceClub(c.Id, c.Name,
                before.Value.GetValueOrDefault(c.Id), after.Value.GetValueOrDefault(c.Id), before.Payroll.GetValueOrDefault(c.Id), after.Payroll.GetValueOrDefault(c.Id),
                before.Sponsor.GetValueOrDefault(c.Id), after.Sponsor.GetValueOrDefault(c.Id), topUp.GetValueOrDefault(c.Id), after.Balance.GetValueOrDefault(c.Id)))
            .OrderByDescending(c => c.ValueAfter).ToList();
        var result = new MarketRepriceResult(season.Name, players, scaled, ecoBefore.SoftWageCap, change.SoftWageCap ?? ecoBefore.SoftWageCap,
            ecoBefore.InitialBudgetFund, change.InitialBudgetFund ?? ecoBefore.InitialBudgetFund, change.BudgetExponent ?? ecoBefore.BudgetExponent,
            topUp.Count(t => t.Value > 0), topUp.Values.Sum(), topUp.Count(t => t.Value == 0), rows);
        db.AdminAudit.Add(new AdminAuditEntry
        {
            At = clock.GetUtcNow(), Actor = actor, EntityType = nameof(Season), EntityId = season.Id, Action = Action,
            Before = JsonSerializer.Serialize(new { ecoBefore.SoftWageCap, ecoBefore.InitialBudgetFund, ecoBefore.BudgetExponent,
                Value = before.Value.Values.Sum(), Payroll = before.Payroll.Values.Sum() }),
            After = JsonSerializer.Serialize(new { result.SoftCapAfter, result.FundAfter, result.ExponentAfter, result.Players, result.Contracts,
                result.ToppedUpClubs, result.TopUpTotal, Value = after.Value.Values.Sum(), Payroll = after.Payroll.Values.Sum() }),
        });
        await db.SaveChangesAsync(ct);
        if (dry)
        {
            await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();   // thực thể đang theo dõi giữ số đã huỷ → bỏ, tránh dùng lại context với số sai
        }
        else await tx.CommitAsync(ct);
        return result;
    }

    private async Task<List<(Guid Id, string Name)>> ClubsAsync(Guid seasonId, CancellationToken ct)
    {
        var only = await EconomyService.BudgetClubsAsync(db, seasonId, ct);
        return (await db.Clubs.AsNoTracking().Where(c => c.Status == ClubStatus.Active && (only == null || only.Contains(c.Id)))
            .Select(c => new { c.Id, c.Name }).ToListAsync(ct)).Select(c => (c.Id, c.Name)).ToList();
    }

    private sealed record Snapshot(Dictionary<Guid, long> Value, Dictionary<Guid, long> Payroll, Dictionary<Guid, long> Sponsor, Dictionary<Guid, long> Balance);

    private async Task<Snapshot> SnapshotAsync(Guid seasonId, CancellationToken ct)
    {
        var value = await (from m in db.SquadMemberships join p in db.PlayerPrices on m.PlayerId equals p.PlayerId
                           where m.LeftAt == null && p.SeasonId == seasonId
                           group p.Price by m.ClubId into g select new { g.Key, V = g.Sum() }).ToDictionaryAsync(x => x.Key, x => x.V, ct);
        var payroll = await db.Contracts.Where(c => c.Status == ContractStatus.Active && c.Wage != null)
            .GroupBy(c => c.ClubId).Select(g => new { g.Key, V = g.Sum(c => c.Wage!.Value) }).ToDictionaryAsync(x => x.Key, x => x.V, ct);
        var sponsor = await db.ClubFinances.AsNoTracking().ToDictionaryAsync(f => f.ClubId, f => f.SponsorPerRound, ct);
        var balance = await db.Ledger.GroupBy(e => e.ClubId).Select(g => new { g.Key, V = g.Sum(e => e.Amount) }).ToDictionaryAsync(x => x.Key, x => x.V, ct);
        return new(value, payroll, sponsor, balance);
    }
}
