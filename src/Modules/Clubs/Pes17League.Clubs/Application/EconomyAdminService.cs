using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Infrastructure;

namespace Pes17League.Clubs.Application;

/// <summary>
/// Một CLB Active trong bảng xem trước. Payroll = quỹ lương mỗi khung ngày, LuxuryTax = thuế xa xỉ mỗi khung theo trần mềm đang xem;
/// Granted = ngân sách đầu mùa đã ghi sổ của mùa (chia + bù); Budget = chia theo tham số đang xem; Change = sẽ ghi sổ (lúc bắt đầu mùa / khi bù).
/// </summary>
public sealed record BudgetPreviewClub(Guid ClubId, string Name, long SquadValue, long Payroll, long LuxuryTax, long Balance, long Granted, long Budget, long Change)
{
    /// <summary>Số dư sau khi ghi.</summary>
    public long After => Balance + Change;
    /// <summary>Số khung ngày trả được lương + thuế bằng số dư sau khi ghi (chưa tính mua bán, chưa có khoản thu); null = không tốn gì.</summary>
    public double? Khung => Payroll + LuxuryTax > 0 ? (double)After / (Payroll + LuxuryTax) : null;
}

/// <summary>Xem trước ngân sách đầu mùa (không ghi). Allocated = mùa đã chia (có dòng InitialBudget); ToppedUp = đã bù;
/// Matchdays / Settled = số khung ngày của mùa / đã chốt; Economy = tham số đang xem (đã ghép phần đổi).</summary>
public sealed record BudgetPreview(SeasonStatus Status, bool Allocated, bool ToppedUp, int Matchdays, int Settled, SeasonEconomy Economy,
    IReadOnlyList<BudgetPreviewClub> Clubs)
{
    /// <summary>Hợp đồng Active của CLB Active / trong đó chưa có lương (Bắt đầu mùa đặt ‰ × giá cho các hợp đồng này).</summary>
    public int Contracts { get; init; }
    public int Unwaged { get; init; }

    /// <summary>Mùa chưa bắt đầu: quỹ chia lúc "Bắt đầu mùa".</summary>
    public bool AllocatesAtStart => Status == SeasonStatus.Planned && !Allocated;
    /// <summary>Mùa đang đá đã chia, chưa bù: bù được 1 lần.</summary>
    public bool CanTopUp => Status == SeasonStatus.Active && Allocated && !ToppedUp;
}

/// <summary>
/// Trang Quản trị → Kinh tế (làm thêm theo uỷ quyền PO 06/11): xem trước rồi mới đặt tham số kinh tế mùa / bù ngân sách đầu mùa, ghi AdminAuditEntry.
/// Mùa chưa bắt đầu: đổi trần mềm, ‰ lương, quỹ, k (số trận hợp đồng: <see cref="FederationSetupService.SetImportedContractLengthAsync"/>).
/// Mùa đang đá: chỉ trần mềm; quỹ + k đổi qua bù 1 lần (<see cref="EconomyService.TopUpInitialBudgetsAsync"/>); ‰ đã dùng đặt lương lúc bắt đầu → khoá.
/// </summary>
public sealed class EconomyAdminService(ClubsDbContext db, TimeProvider clock, PriceModelOptions? priceOptions = null)
{
    public const string SetAction = "SetEconomy", TopUpAction = "TopUpInitialBudget";

    /// <summary>
    /// Ngân sách đầu mùa với tham số <paramref name="change"/> (null = giữ), không ghi gì. Mùa chưa bắt đầu: giá và lương tính như lúc "Bắt đầu mùa"
    /// (RecalculatePrices → SetImportedWages → AllocateInitialBudgets); mùa đang đá: giá hiện tại như khi bù. Bảng xếp theo giá trị squad giảm dần.
    /// </summary>
    public async Task<BudgetPreview> PreviewAsync(Guid seasonId, SeasonEconomyChange? change = null, CancellationToken ct = default)
    {
        Validate(change);
        var season = await db.Seasons.AsNoTracking().SingleOrDefaultAsync(s => s.Id == seasonId, ct) ?? throw new AdminRuleException("Không tìm thấy mùa");
        var eco = Merge(await StoredAsync(seasonId, ct), change);
        var planned = season.Status == SeasonStatus.Planned;
        var prices = planned ? await StartPricesAsync(seasonId, ct)
            : await db.PlayerPrices.AsNoTracking().Where(p => p.SeasonId == seasonId).ToDictionaryAsync(p => p.PlayerId, p => p.Price, ct);
        // Như EconomyService.ComputeBudgetsAsync: mọi CLB Active, thứ tự theo Id (trùng giá trị squad → CLB đứng trước nhận phần dư).
        var only = await EconomyService.BudgetClubsAsync(db, seasonId, ct);
        var clubs = await db.Clubs.Where(c => c.Status == ClubStatus.Active && (only == null || only.Contains(c.Id))).OrderBy(c => c.Id).Select(c => new { c.Id, c.Name }).ToListAsync(ct);
        var squads = await (from m in db.SquadMemberships join c in db.Clubs on m.ClubId equals c.Id
                            where m.LeftAt == null && c.Status == ClubStatus.Active select new { m.ClubId, m.PlayerId }).ToListAsync(ct);
        var value = squads.GroupBy(m => m.ClubId).ToDictionary(g => g.Key, g => g.Sum(m => prices.GetValueOrDefault(m.PlayerId)));
        // Lương như SettleRoundAsync; mùa chưa bắt đầu: hợp đồng chưa có lương nhận ‰ × giá như SetImportedWagesAsync.
        var contracts = await (from c in db.Contracts join club in db.Clubs on c.ClubId equals club.Id
                               where c.Status == ContractStatus.Active && club.Status == ClubStatus.Active
                               select new { c.ClubId, c.PlayerId, c.Wage }).ToListAsync(ct);
        var payroll = contracts.GroupBy(c => c.ClubId).ToDictionary(g => g.Key, g => g.Sum(c =>
            c.Wage ?? (planned && prices.TryGetValue(c.PlayerId, out var p) ? ContractRules.Wage(p, eco.WagePermille) : 0)));
        var balance = await Sums(db.Ledger, ct);
        var initial = await Sums(db.Ledger.Where(e => e.Reason == LedgerReason.InitialBudget && e.RefType == nameof(SeasonEconomy) && e.RefId == seasonId), ct);
        var topUp = await Sums(db.Ledger.Where(e => e.Reason == LedgerReason.Adjustment && e.RefType == EconomyService.InitialBudgetTopUp && e.RefId == seasonId), ct);
        var budgets = BudgetAllocation.Allocate(clubs.Select(c => (c.Id, value.GetValueOrDefault(c.Id))).ToList(), eco.InitialBudgetFund, eco.BudgetExponent);

        var preview = new BudgetPreview(season.Status, initial.Count > 0, topUp.Count > 0,
            await db.Matchdays.CountAsync(m => m.SeasonId == seasonId, ct), await db.RoundSettlements.CountAsync(r => r.SeasonId == seasonId, ct), eco, []);
        long Change(Guid id) => preview.AllocatesAtStart ? budgets[id] : preview.CanTopUp ? budgets[id] - initial.GetValueOrDefault(id) : 0;
        return preview with
        {
            Contracts = contracts.Count,
            Unwaged = contracts.Count(c => c.Wage == null),
            Clubs = [.. clubs.Select(c =>
            {
                var pay = payroll.GetValueOrDefault(c.Id);
                return new BudgetPreviewClub(c.Id, c.Name, value.GetValueOrDefault(c.Id), pay, LuxuryTax.Compute(pay, eco.SoftWageCap), balance.GetValueOrDefault(c.Id),
                    initial.GetValueOrDefault(c.Id) + topUp.GetValueOrDefault(c.Id), budgets[c.Id], Change(c.Id));
            }).OrderByDescending(r => r.SquadValue)],
        };
    }

    /// <summary>Đặt tham số mùa, ghi nhật ký (không đổi gì → bỏ qua, trả false). Mùa đang đá: chỉ trần mềm.</summary>
    public async Task<bool> SetAsync(Guid seasonId, SeasonEconomyChange change, string actor, CancellationToken ct = default)
    {
        Validate(change);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var season = await LockSeasonAsync(seasonId, ct);
        var before = await StoredAsync(seasonId, ct);
        if (season.Status == SeasonStatus.Finished) throw new AdminRuleException("Mùa đã kết thúc");
        if (season.Status == SeasonStatus.Active && (Differs(change.InitialBudgetFund, before.InitialBudgetFund) || Differs(change.BudgetExponent, before.BudgetExponent)))
            throw new AdminRuleException("Mùa đang đá: quỹ và k chỉ đổi khi bù ngân sách đầu mùa");
        CheckPermille(season, before, change);
        var after = Merge(before, change);
        if (Snapshot(after).Equals(Snapshot(before))) return false;
        await new EconomyService(db, clock).SetParametersAsync(seasonId, change with { DefaultContractMatches = null }, ct);
        Audit(seasonId, SetAction, Snapshot(before), Snapshot(after), actor);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    /// <summary>Bù ngân sách đầu mùa 1 lần (mùa đang đá, đã chia): đặt trần mềm / quỹ / k rồi <see cref="EconomyService.TopUpInitialBudgetsAsync"/>,
    /// ghi nhật ký. Trả phần chênh từng CLB.</summary>
    public async Task<IReadOnlyDictionary<Guid, long>> TopUpAsync(Guid seasonId, SeasonEconomyChange change, string actor, CancellationToken ct = default)
    {
        Validate(change);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var season = await LockSeasonAsync(seasonId, ct);   // 2 tab bấm cùng lúc → lần sau thấy đã bù
        if (season.Status != SeasonStatus.Active)
            throw new AdminRuleException(season.Status == SeasonStatus.Planned ? "Mùa chưa bắt đầu: quỹ chia lúc Bắt đầu mùa, không cần bù" : "Mùa đã kết thúc");
        if (!await db.Ledger.AnyAsync(e => e.Reason == LedgerReason.InitialBudget && e.RefType == nameof(SeasonEconomy) && e.RefId == seasonId, ct))
            throw new AdminRuleException("Mùa chưa chia ngân sách đầu mùa");
        if (await db.Ledger.AnyAsync(e => e.Reason == LedgerReason.Adjustment && e.RefType == EconomyService.InitialBudgetTopUp && e.RefId == seasonId, ct))
            throw new AdminRuleException("Mùa đã bù ngân sách đầu mùa (chỉ bù 1 lần)");
        var before = await StoredAsync(seasonId, ct);
        CheckPermille(season, before, change);
        var eco = new EconomyService(db, clock);
        await eco.SetParametersAsync(seasonId, new(SoftWageCap: change.SoftWageCap, InitialBudgetFund: change.InitialBudgetFund, BudgetExponent: change.BudgetExponent), ct);
        var diff = await eco.TopUpInitialBudgetsAsync(seasonId, ct);
        var a = Snapshot(Merge(before, change));
        Audit(seasonId, TopUpAction, Snapshot(before), new
        {
            a.SoftWageCap, a.WagePermille, a.InitialBudgetFund, a.BudgetExponent, Clubs = diff.Count(d => d.Value != 0), Total = diff.Values.Sum(),
        }, actor);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return diff;
    }

    /// <summary>Giá như <see cref="EconomyService.RecalculatePricesAsync"/> sẽ tính lúc "Bắt đầu mùa" (không ghi): chỉ số hiện tại → chỉ số tổng
    /// → giá gốc × hệ số phong độ đã có; cầu thủ không có chỉ số mùa giữ giá đã có.</summary>
    private async Task<Dictionary<Guid, long>> StartPricesAsync(Guid seasonId, CancellationToken ct)
    {
        var stored = await db.PlayerPrices.AsNoTracking().Where(p => p.SeasonId == seasonId).ToDictionaryAsync(p => p.PlayerId, ct);
        var rows = await (from a in db.PlayerAttributes join p in db.Players on a.PlayerId equals p.Id
                          where a.SeasonId == seasonId select new { a.PlayerId, p.Position }).ToListAsync(ct);
        var current = await new EconomyService(db, clock, priceOptions).CurrentAttributesAsync(seasonId, null, ct);
        var model = new PriceModel(priceOptions);
        var prices = stored.ToDictionary(x => x.Key, x => x.Value.Price);
        foreach (var r in rows)
        {
            var values = current[r.PlayerId];
            var basePrice = model.BasePrice(model.Overall(r.Position, values), values.GetValueOrDefault("AGE", 27));
            prices[r.PlayerId] = model.Price(basePrice, stored.TryGetValue(r.PlayerId, out var pp) ? pp.FormMultiplier : 1);
        }
        return prices;
    }

    private static async Task<Dictionary<Guid, long>> Sums(IQueryable<ClubLedgerEntry> q, CancellationToken ct) =>
        await q.GroupBy(e => e.ClubId).Select(g => new { g.Key, Sum = g.Sum(e => e.Amount) }).ToDictionaryAsync(x => x.Key, x => x.Sum, ct);

    private async Task<Season> LockSeasonAsync(Guid seasonId, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM clubs.\"Seasons\" WHERE \"Id\" = {seasonId} FOR UPDATE", ct);
        return await db.Seasons.SingleOrDefaultAsync(s => s.Id == seasonId, ct) ?? throw new AdminRuleException("Không tìm thấy mùa");
    }

    private async Task<SeasonEconomy> StoredAsync(Guid seasonId, CancellationToken ct) =>
        await db.SeasonEconomies.AsNoTracking().SingleOrDefaultAsync(e => e.SeasonId == seasonId, ct) ?? new SeasonEconomy { SeasonId = seasonId };

    /// <summary>‰ chỉ đổi khi mùa chưa bắt đầu: lương hợp đồng nhập đặt lúc "Bắt đầu mùa".</summary>
    private static void CheckPermille(Season season, SeasonEconomy before, SeasonEconomyChange change)
    {
        if (season.Status != SeasonStatus.Planned && Differs(change.WagePermille, before.WagePermille))
            throw new AdminRuleException("Lương hợp đồng nhập đã đặt lúc bắt đầu mùa: ‰ chỉ đổi khi mùa chưa bắt đầu");
    }

    private static bool Differs<T>(T? value, T current) where T : struct => value is { } v && !v.Equals(current);

    private static void Validate(SeasonEconomyChange? c)
    {
        if (c is null) return;
        if (c.SoftWageCap < 0 || c.WagePermille < 0 || c.InitialBudgetFund < 0 || c.BudgetExponent < 0 || c.BudgetExponent is { } k && !double.IsFinite(k))
            throw new AdminRuleException("Tham số kinh tế phải là số không âm");
    }

    private static SeasonEconomy Merge(SeasonEconomy e, SeasonEconomyChange? c) => new()
    {
        SeasonId = e.SeasonId, SoftWageCap = c?.SoftWageCap ?? e.SoftWageCap, WagePermille = c?.WagePermille ?? e.WagePermille,
        InitialBudgetFund = c?.InitialBudgetFund ?? e.InitialBudgetFund, BudgetExponent = c?.BudgetExponent ?? e.BudgetExponent,
        DefaultContractMatches = e.DefaultContractMatches, UpdatedAt = e.UpdatedAt,
    };

    private sealed record EconomySnapshot(long SoftWageCap, int WagePermille, long InitialBudgetFund, double BudgetExponent);

    private static EconomySnapshot Snapshot(SeasonEconomy e) => new(e.SoftWageCap, e.WagePermille, e.InitialBudgetFund, e.BudgetExponent);

    private void Audit(Guid seasonId, string action, object before, object after, string actor) =>
        db.AdminAudit.Add(new AdminAuditEntry
        {
            At = clock.GetUtcNow(), Actor = actor, EntityType = nameof(Season), EntityId = seasonId, Action = action,
            Before = JsonSerializer.Serialize(before), After = JsonSerializer.Serialize(after),
        });
}
