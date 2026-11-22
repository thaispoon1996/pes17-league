using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Infrastructure;

namespace Pes17League.Clubs.Application;

public sealed record ClubSettlement(Guid ClubId, long Payroll, long LuxuryTax);

public enum ExpiryOutcome
{
    /// <summary>Đang ở CLB gốc → hợp đồng mới DefaultContractMatches trận, lương theo giá.</summary>
    RenewedAtOrigin,
    /// <summary>Rời đi làm CLB hiện tại vi phạm luật squad → tự gia hạn 10 trận, giữ lương.</summary>
    AutoRenewed,
    ReturnedToOrigin,
    BecameFree,
    /// <summary>Hết hạn mượn → về CLB chủ quản với hợp đồng cũ.</summary>
    LoanReturned,
}

/// <summary>Hợp đồng hết hạn khi chốt vòng và cách đã xử lý. NeedsLineupReview: CLB cũ cần xếp lại đội hình.</summary>
public sealed record ExpiredContract(Guid ContractId, Guid PlayerId, Guid ClubId, ExpiryOutcome Outcome, Guid? NewClubId, bool NeedsLineupReview);

/// <summary>Nhắc gia hạn: hợp đồng còn đúng 3 trận.</summary>
public sealed record RenewalReminder(Guid ContractId, Guid PlayerId, Guid ClubId, int MatchesLeft);

public sealed record RoundSettlementResult(bool AlreadySettled, IReadOnlyList<ClubSettlement> Clubs,
    IReadOnlyList<ExpiredContract> ExpiredContracts, IReadOnlyList<RenewalReminder> RenewalReminders);

/// <summary>Tham số mùa admin đặt; null = giữ nguyên.</summary>
public sealed record SeasonEconomyChange(long? SoftWageCap = null, int? WagePermille = null, long? InitialBudgetFund = null,
    double? BudgetExponent = null, int? DefaultContractMatches = null);

/// <summary>
/// Kinh tế (C14/C19/C20, PO chọn 01/11): admin đặt trần lương mùa; chốt vòng trừ lương + thuế xa xỉ vào sổ;
/// giá cầu thủ theo mô hình A, tính lại từ snapshot chỉ số mùa và chỉnh theo phong độ sau mỗi vòng.
/// Đợt 2: lương hợp đồng nhập = tỷ lệ × giá; ngân sách đầu mùa bù ngược theo giá trị squad; hết hợp đồng → về CLB gốc / tự do / tự gia hạn.
/// Thứ tự khởi động mùa: ImportPatch → RecalculatePrices → SetImportedWages → AllocateInitialBudgets.
/// </summary>
public sealed class EconomyService(ClubsDbContext db, TimeProvider clock, PriceModelOptions? priceOptions = null, SquadPolicyOptions? squadOptions = null)
{
    private readonly PriceModel _price = new(priceOptions);
    private readonly SquadPolicy<Guid> _policy = new(squadOptions);

    public Task SetSoftWageCapAsync(Guid seasonId, long softCap, CancellationToken ct = default) =>
        SetParametersAsync(seasonId, new(SoftWageCap: softCap), ct);

    public async Task SetParametersAsync(Guid seasonId, SeasonEconomyChange change, CancellationToken ct = default)
    {
        if (change.SoftWageCap < 0 || change.WagePermille < 0 || change.InitialBudgetFund < 0 || change.BudgetExponent < -1)
            throw new ArgumentOutOfRangeException(nameof(change), "Tham số kinh tế không được âm");
        if (change.DefaultContractMatches is { } m && !ContractRules.IsValidLength(m))
            throw new ArgumentOutOfRangeException(nameof(change), "DefaultContractMatches ngoài 10–60");
        var e = await GetOrCreateAsync(seasonId, ct);
        e.SoftWageCap = change.SoftWageCap ?? e.SoftWageCap;
        e.WagePermille = change.WagePermille ?? e.WagePermille;
        e.InitialBudgetFund = change.InitialBudgetFund ?? e.InitialBudgetFund;
        e.BudgetExponent = change.BudgetExponent ?? e.BudgetExponent;
        e.DefaultContractMatches = change.DefaultContractMatches ?? e.DefaultContractMatches;
        e.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Hợp đồng Active chưa có lương (nhập từ patch) → lương = WagePermille × giá. Cầu thủ chưa có giá: bỏ qua. Trả số hợp đồng đã đặt.</summary>
    public async Task<int> SetImportedWagesAsync(Guid seasonId, CancellationToken ct = default)
    {
        var permille = (await GetOrCreateAsync(seasonId, ct)).WagePermille;
        var rows = await (from c in db.Contracts
                          join p in db.PlayerPrices on new { c.PlayerId, SeasonId = seasonId } equals new { p.PlayerId, p.SeasonId }
                          where c.Status == ContractStatus.Active && c.Wage == null
                          select new { c, p.Price }).ToListAsync(ct);
        foreach (var r in rows) r.c.Wage = ContractRules.Wage(r.Price, permille);
        await db.SaveChangesAsync(ct);
        return rows.Count;
    }

    /// <summary>
    /// Ngân sách đầu mùa: chia InitialBudgetFund cho các CLB Active theo (1 / giá trị squad)^BudgetExponent, ghi sổ InitialBudget.
    /// Mỗi mùa chỉ chia 1 lần (sổ đã có InitialBudget của mùa → trả rỗng).
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, long>> AllocateInitialBudgetsAsync(Guid seasonId, CancellationToken ct = default)
    {
        if (await db.Ledger.AnyAsync(e => e.Reason == LedgerReason.InitialBudget && e.RefType == nameof(SeasonEconomy) && e.RefId == seasonId, ct))
            return new Dictionary<Guid, long>();
        var budgets = await ComputeBudgetsAsync(seasonId, ct);
        var now = clock.GetUtcNow();
        foreach (var (clubId, amount) in budgets)
            db.Ledger.Add(new ClubLedgerEntry { ClubId = clubId, Amount = amount, Reason = LedgerReason.InitialBudget, RefType = nameof(SeasonEconomy), RefId = seasonId, At = now });
        await db.SaveChangesAsync(ct);
        return budgets;
    }

    /// <summary>Lý do (RefType) của dòng Adjustment bù ngân sách đầu mùa.</summary>
    public const string InitialBudgetTopUp = "InitialBudgetTopUp";

    /// <summary>
    /// Bù ngân sách đầu mùa khi mùa đã chia lúc quỹ chưa đặt (vd Alpha 1 chia 0): chia lại InitialBudgetFund hiện tại như
    /// <see cref="AllocateInitialBudgetsAsync"/>, ghi phần chênh so với InitialBudget đã có bằng dòng Adjustment (RefType = InitialBudgetTopUp),
    /// giữ nguyên dòng cũ. Mùa chưa chia hoặc đã bù rồi → trả rỗng.
    /// </summary>
    /// <param name="onlyAdd">Chỉ ghi phần cộng (CLB lẽ ra bị bớt giữ nguyên, phần chênh trả về 0) — áp giá thật giữa mùa 1 (PO 22/11).</param>
    public async Task<IReadOnlyDictionary<Guid, long>> TopUpInitialBudgetsAsync(Guid seasonId, CancellationToken ct = default, bool onlyAdd = false)
    {
        var granted = await db.Ledger.Where(e => e.Reason == LedgerReason.InitialBudget && e.RefType == nameof(SeasonEconomy) && e.RefId == seasonId)
            .GroupBy(e => e.ClubId).Select(g => new { g.Key, Sum = g.Sum(e => e.Amount) }).ToDictionaryAsync(x => x.Key, x => x.Sum, ct);
        if (granted.Count == 0 || await db.Ledger.AnyAsync(e => e.Reason == LedgerReason.Adjustment && e.RefType == InitialBudgetTopUp && e.RefId == seasonId, ct))
            return new Dictionary<Guid, long>();
        var diff = (await ComputeBudgetsAsync(seasonId, ct)).ToDictionary(b => b.Key, b => b.Value - granted.GetValueOrDefault(b.Key));
        if (onlyAdd) foreach (var id in diff.Where(d => d.Value < 0).Select(d => d.Key).ToList()) diff[id] = 0;
        var now = clock.GetUtcNow();
        foreach (var (clubId, amount) in diff.Where(d => d.Value != 0))
            db.Ledger.Add(new ClubLedgerEntry { ClubId = clubId, Amount = amount, Reason = LedgerReason.Adjustment, RefType = InitialBudgetTopUp, RefId = seasonId, At = now });
        await db.SaveChangesAsync(ct);
        return diff;
    }

    /// <summary>
    /// CLB được chia ngân sách đầu mùa: mùa có giải (Editions) → chỉ CLB dự giải mùa đó (PES17: 96 CLB 5 giải, PO chọn 18/11/2026;
    /// CLB ngoài giải không có tiền, chỉ là nơi bán cầu thủ). Mùa chưa có giải → null = mọi CLB Active (như PES6).
    /// </summary>
    public static async Task<List<Guid>?> BudgetClubsAsync(ClubsDbContext db, Guid seasonId, CancellationToken ct = default)
    {
        var ids = await (from ec in db.EditionClubs join e in db.Editions on ec.EditionId equals e.Id
                         where e.SeasonId == seasonId select ec.ClubId).Distinct().ToListAsync(ct);
        return ids.Count > 0 ? ids : null;
    }

    private async Task<IReadOnlyDictionary<Guid, long>> ComputeBudgetsAsync(Guid seasonId, CancellationToken ct)
    {
        var eco = await GetOrCreateAsync(seasonId, ct);
        var only = await BudgetClubsAsync(db, seasonId, ct);
        var values = await (from club in db.Clubs
                            where club.Status == ClubStatus.Active && (only == null || only.Contains(club.Id))
                            select new
                            {
                                club.Id,
                                Value = (from m in db.SquadMemberships
                                         join p in db.PlayerPrices on new { m.PlayerId, SeasonId = seasonId } equals new { p.PlayerId, p.SeasonId }
                                         where m.ClubId == club.Id && m.LeftAt == null
                                         select (long?)p.Price).Sum() ?? 0,
                            }).OrderBy(x => x.Id).ToListAsync(ct);
        return BudgetAllocation.Allocate(values.Select(v => (v.Id, v.Value)).ToList(), eco.InitialBudgetFund, eco.BudgetExponent);
    }

    /// <summary>
    /// Chốt vòng: mỗi CLB Active trừ lương (tổng Wage hợp đồng Active) và thuế xa xỉ trên phần vượt trần mềm; hợp đồng Active của CLB có trận ở khung này +1 trận.
    /// Hợp đồng còn 3 trận → nhắc gia hạn; hết hạn → xử lý ngay (xem <see cref="ExpiryOutcome"/>), có hiệu lực từ OF vòng kế.
    /// Mỗi (mùa, vòng) chỉ chạy 1 lần (RoundSettlement unique). Gọi trong cùng bước chốt vòng, TRƯỚC khi sinh OF.
    /// </summary>
    public async Task<RoundSettlementResult> SettleRoundAsync(Guid seasonId, Guid matchdayId, CancellationToken ct = default)
    {
        if (await db.RoundSettlements.AnyAsync(r => r.SeasonId == seasonId && r.MatchdayId == matchdayId, ct))
            return new(true, [], [], []);
        var now = clock.GetUtcNow();
        var eco = await GetOrCreateAsync(seasonId, ct);
        var softCap = eco.SoftWageCap;
        var settlement = new RoundSettlement { SeasonId = seasonId, MatchdayId = matchdayId, SettledAt = now };
        db.RoundSettlements.Add(settlement);

        var contracts = await (from c in db.Contracts
                               join club in db.Clubs on c.ClubId equals club.Id
                               where c.Status == ContractStatus.Active && club.Status == ClubStatus.Active
                               select c).ToListAsync(ct);
        var clubs = await db.Clubs.Where(c => c.Status == ClubStatus.Active).Select(c => c.Id).ToListAsync(ct);
        var results = new List<ClubSettlement>();
        foreach (var clubId in clubs)
        {
            var payroll = contracts.Where(c => c.ClubId == clubId).Sum(c => c.Wage ?? 0);
            var tax = LuxuryTax.Compute(payroll, softCap);
            if (payroll > 0) db.Ledger.Add(Entry(clubId, -payroll, LedgerReason.Wage, settlement.Id, now));
            if (tax > 0) db.Ledger.Add(Entry(clubId, -tax, LedgerReason.LuxuryTax, settlement.Id, now));
            results.Add(new(clubId, payroll, tax));
        }
        // Trả góp phí chuyển nhượng (FM đợt 3): mỗi lần chốt vòng 1 kỳ.
        foreach (var t in await db.Transfers.Where(t => t.Status == TransferStatus.Completed && t.InstalmentsPaid < t.Instalments).ToListAsync(ct))
        {
            var part = t.Fee / t.Instalments;
            db.Ledger.Add(new ClubLedgerEntry { ClubId = t.ToClubId!.Value, Amount = -part, Reason = LedgerReason.TransferIn, RefType = nameof(Transfer), RefId = t.Id, At = now });
            if (t.FromClubId is { } seller)
                db.Ledger.Add(new ClubLedgerEntry { ClubId = seller, Amount = part, Reason = LedgerReason.TransferOut, RefType = nameof(Transfer), RefId = t.Id, At = now });
            t.InstalmentsPaid++;
        }
        // PO 19/11: hợp đồng chỉ trừ khi CLB có trận ở khung này (lịch PES17 có khung nghỉ). Khung không có trận nào (vòng admin/test) → trừ mọi hợp đồng như cũ.
        var playing = (await db.Fixtures.Where(f => f.MatchdayId == matchdayId).Select(f => new { f.HomeClubId, f.AwayClubId }).ToListAsync(ct))
            .SelectMany(f => new[] { f.HomeClubId, f.AwayClubId }).ToHashSet();
        var reminders = new List<RenewalReminder>();
        var expiring = new List<PlayerContract>();
        foreach (var c in contracts)
        {
            if (playing.Count > 0 && !playing.Contains(c.ClubId)) continue;
            c.MatchesPlayed++;
            if (c.MatchesTotal is not { } total) continue;
            if (total - c.MatchesPlayed == ContractRules.RenewalReminderMatches) reminders.Add(new(c.Id, c.PlayerId, c.ClubId, ContractRules.RenewalReminderMatches));
            if (c.MatchesPlayed >= total) expiring.Add(c);
        }
        var expired = await ExpireAsync(seasonId, eco, expiring.OrderBy(c => c.StartedAt).ThenBy(c => c.PlayerId).ToList(), now, ct);
        await db.SaveChangesAsync(ct);
        return new(false, results, expired, reminders);
    }

    /// <summary>
    /// Hết hợp đồng (Kinh tế đợt 2), xét theo thứ tự: đang ở CLB gốc → hợp đồng mới; rời đi vi phạm luật squad → tự gia hạn 10 trận;
    /// CLB gốc Active và chưa đủ 32 → về CLB gốc; còn lại → tự do. Squad giữ trong bộ nhớ để nhiều ca cùng CLB tính đúng.
    /// Đề nghị gia hạn đang chờ của hợp đồng hết hạn bị huỷ.
    /// </summary>
    private async Task<List<ExpiredContract>> ExpireAsync(Guid seasonId, SeasonEconomy eco, List<PlayerContract> expiring, DateTimeOffset now, CancellationToken ct)
    {
        var result = new List<ExpiredContract>();
        if (expiring.Count == 0) return result;
        var ids = expiring.Select(c => c.Id).ToList();
        foreach (var r in await db.ContractRenewals.Where(r => ids.Contains(r.ContractId) && r.Status == RenewalStatus.Pending).ToListAsync(ct))
        {
            r.Status = RenewalStatus.Cancelled;
            r.RejectReason = "ContractExpired";
            r.DecidedAt = now;
        }
        var squads = new Dictionary<Guid, Squad>();
        async Task<Squad> SquadOf(Guid clubId) => squads.TryGetValue(clubId, out var s) ? s : squads[clubId] = await SquadStore.LoadAsync(db, clubId, ct);

        foreach (var c in expiring)
        {
            var player = (await db.Players.FindAsync([c.PlayerId], ct))!;
            var club = (await db.Clubs.FindAsync([c.ClubId], ct))!;
            var price = (await db.PlayerPrices.FindAsync([c.PlayerId, seasonId], ct))?.Price;
            long? WageByPrice() => price is { } p ? ContractRules.Wage(p, eco.WagePermille) : c.Wage;

            if (c.LoanFromClubId is { } homeId)
            {
                var home = await db.Clubs.FindAsync([homeId], ct);
                var on = await SquadOf(club.Id);
                var left = _policy.Release(on.Entries, c.PlayerId);
                var back = home is { Status: ClubStatus.Active } ? _policy.Add((await SquadOf(home.Id)).Entries, c.PlayerId, player.Position) : null;
                if (left.Ok && back is { Ok: true })
                {
                    squads[club.Id] = SquadStore.Apply(db, club, on, left, now);
                    squads[home!.Id] = SquadStore.Apply(db, home, squads[home.Id], back, now);
                    End(c, ContractEndReason.LoanReturned, now);
                    db.Contracts.Add(new PlayerContract
                    {
                        PlayerId = c.PlayerId, ClubId = home.Id, StartedAt = now, Wage = c.LoanParentWage,
                        MatchesTotal = Math.Clamp(c.LoanParentMatchesLeft ?? eco.DefaultContractMatches, ContractRules.MinMatches, ContractRules.MaxMatches),
                    });
                    result.Add(new(c.Id, c.PlayerId, club.Id, ExpiryOutcome.LoanReturned, home.Id, left.NeedsLineupReview));
                }
                else
                {
                    // Không về được (luật squad một trong hai bên) → gia hạn mượn 10 trận.
                    End(c, ContractEndReason.Renewed, now);
                    db.Contracts.Add(new PlayerContract
                    {
                        PlayerId = c.PlayerId, ClubId = c.ClubId, MatchesTotal = ContractRules.AutoRenewMatches, Wage = c.Wage, StartedAt = now,
                        LoanFromClubId = c.LoanFromClubId, LoanParentMatchesLeft = c.LoanParentMatchesLeft, LoanParentWage = c.LoanParentWage,
                    });
                    result.Add(new(c.Id, c.PlayerId, club.Id, ExpiryOutcome.AutoRenewed, club.Id, false));
                }
                continue;
            }

            if (player.OriginClubId == club.Id)
            {
                Renew(c, eco.DefaultContractMatches, WageByPrice(), now);
                result.Add(new(c.Id, c.PlayerId, club.Id, ExpiryOutcome.RenewedAtOrigin, club.Id, false));
                continue;
            }
            var from = await SquadOf(club.Id);
            var released = _policy.Release(from.Entries, c.PlayerId);
            if (!released.Ok)
            {
                Renew(c, ContractRules.AutoRenewMatches, c.Wage, now);
                result.Add(new(c.Id, c.PlayerId, club.Id, ExpiryOutcome.AutoRenewed, club.Id, false));
                continue;
            }
            Club? origin = player.OriginClubId is { } oid ? await db.Clubs.FindAsync([oid], ct) : null;
            SquadChange<Guid>? added = null;
            if (origin is { Status: ClubStatus.Active })
            {
                added = _policy.Add((await SquadOf(origin.Id)).Entries, c.PlayerId, player.Position);
                if (!added.Ok) added = null;   // đủ 32 → tự do
            }
            squads[club.Id] = SquadStore.Apply(db, club, from, released, now);
            End(c, added is null ? ContractEndReason.BecameFree : ContractEndReason.ReturnedToOrigin, now);
            if (added is not null)
            {
                squads[origin!.Id] = SquadStore.Apply(db, origin, squads[origin.Id], added, now);
                db.Contracts.Add(new PlayerContract { PlayerId = c.PlayerId, ClubId = origin.Id, MatchesTotal = eco.DefaultContractMatches, Wage = WageByPrice(), StartedAt = now });
            }
            result.Add(new(c.Id, c.PlayerId, club.Id, added is null ? ExpiryOutcome.BecameFree : ExpiryOutcome.ReturnedToOrigin, origin?.Id is { } nid && added is not null ? nid : null, released.NeedsLineupReview));
        }
        return result;
    }

    private void Renew(PlayerContract old, int matches, long? wage, DateTimeOffset now)
    {
        End(old, ContractEndReason.Renewed, now);
        db.Contracts.Add(new PlayerContract { PlayerId = old.PlayerId, ClubId = old.ClubId, MatchesTotal = matches, Wage = wage, StartedAt = now });
    }

    private static void End(PlayerContract c, ContractEndReason reason, DateTimeOffset now)
    {
        c.Status = ContractStatus.Ended;
        c.EndedAt = now;
        c.EndReason = reason;
    }

    private async Task<SeasonEconomy> GetOrCreateAsync(Guid seasonId, CancellationToken ct)
    {
        var e = await db.SeasonEconomies.FindAsync([seasonId], ct);
        if (e is null) db.SeasonEconomies.Add(e = new SeasonEconomy { SeasonId = seasonId, UpdatedAt = clock.GetUtcNow() });
        return e;
    }

    /// <summary>Tính lại chỉ số tổng + giá gốc từ chỉ số hiện tại của mùa (snapshot + tiến hoá), giữ hệ số phong độ hiện có.
    /// <paramref name="playerIds"/> null = cả mùa (đầu mùa/khi đổi tham số).</summary>
    public async Task<int> RecalculatePricesAsync(Guid seasonId, CancellationToken ct = default, IReadOnlyCollection<Guid>? playerIds = null)
    {
        var now = clock.GetUtcNow();
        var model = await ModelForSeasonAsync(db, seasonId, priceOptions, ct);
        var rows = await (from a in db.PlayerAttributes
                          join p in db.Players on a.PlayerId equals p.Id
                          where a.SeasonId == seasonId && (playerIds == null || playerIds.Contains(a.PlayerId))
                          select new { a.PlayerId, p.Position, p.RealValue, p.RealValueAge, p.RealValueOverall }).ToListAsync(ct);
        var current = await CurrentAttributesAsync(seasonId, playerIds, ct);
        var prices = await db.PlayerPrices.Where(x => x.SeasonId == seasonId && (playerIds == null || playerIds.Contains(x.PlayerId)))
            .ToDictionaryAsync(x => x.PlayerId, ct);
        foreach (var r in rows)
        {
            var values = current[r.PlayerId];
            if (!prices.TryGetValue(r.PlayerId, out var pp))
                db.PlayerPrices.Add(pp = new PlayerPrice { PlayerId = r.PlayerId, SeasonId = seasonId });
            pp.Overall = model.Overall(r.Position, values);
            pp.BasePrice = model.BasePrice(pp.Overall, values.GetValueOrDefault("AGE", 27), RealValue.Of(r.RealValue, r.RealValueAge, r.RealValueOverall));
            pp.Price = model.Price(pp.BasePrice, pp.FormMultiplier);
            pp.UpdatedAt = now;
        }
        await db.SaveChangesAsync(ct);
        return rows.Count;
    }

    /// <summary>Mô hình giá của mùa: tham số truyền vào (test/admin) hoặc theo số mùa (<see cref="PriceModelOptions.ForSeason"/>).</summary>
    internal static async Task<PriceModel> ModelForSeasonAsync(ClubsDbContext db, Guid seasonId, PriceModelOptions? options, CancellationToken ct)
    {
        if (options is not null) return new PriceModel(options);
        var number = await db.Seasons.Where(s => s.Id == seasonId).Select(s => (int?)s.Number).SingleOrDefaultAsync(ct) ?? 1;
        return new PriceModel(PriceModelOptions.ForSeason(number));
    }

    /// <summary>Sau mỗi vòng: chỉnh giá theo phong độ (form ∈ [-1, 1] mỗi cầu thủ; nguồn form chờ module trận).</summary>
    public async Task ApplyFormAsync(Guid seasonId, IReadOnlyDictionary<Guid, double> form, CancellationToken ct = default)
    {
        var ids = form.Keys.ToList();
        var now = clock.GetUtcNow();
        foreach (var pp in await db.PlayerPrices.Where(x => x.SeasonId == seasonId && ids.Contains(x.PlayerId)).ToListAsync(ct))
        {
            pp.FormMultiplier = _price.NextFormMultiplier(pp.FormMultiplier, form[pp.PlayerId]);
            pp.Price = _price.Price(pp.BasePrice, pp.FormMultiplier);
            pp.UpdatedAt = now;
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Chỉ số hiện tại = snapshot đầu mùa + tổng thay đổi của mùa (theo trận + admin sửa). <paramref name="playerIds"/> null = cả mùa.</summary>
    public async Task<Dictionary<Guid, Dictionary<string, int>>> CurrentAttributesAsync(Guid seasonId, IReadOnlyCollection<Guid>? playerIds = null,
        CancellationToken ct = default)
    {
        var current = await db.PlayerAttributes.Where(a => a.SeasonId == seasonId && (playerIds == null || playerIds.Contains(a.PlayerId)))
            .ToDictionaryAsync(a => a.PlayerId, a => new Dictionary<string, int>(a.Values), ct);
        var changes = await db.AttributeChanges.Where(c => c.SeasonId == seasonId && (playerIds == null || playerIds.Contains(c.PlayerId)))
            .GroupBy(c => new { c.PlayerId, c.Field }).Select(g => new { g.Key.PlayerId, g.Key.Field, Delta = g.Sum(c => c.Delta) }).ToListAsync(ct);
        foreach (var c in changes)
            if (current.TryGetValue(c.PlayerId, out var v) && v.TryGetValue(c.Field, out var x)) v[c.Field] = x + c.Delta;
        return current;
    }

    /// <summary>
    /// C19 theo trận (PO chọn 04/11): điểm ≥ 7,5 → +1 (chỉ khi chỉ số tổng còn dưới tiềm năng — PO 19/11), ≤ 5,0 → −1 mỗi chỉ số hợp vai trò;
    /// tổng thay đổi mùa mỗi chỉ số trong ±3, giá trị trong 1–99.
    /// Ghi AttributeChange (chỉ phần thực đổi) rồi tính lại giá gốc cầu thủ bị đổi. Trả về số cầu thủ bị đổi chỉ số.
    /// </summary>
    public async Task<int> EvolveAttributesAsync(Guid seasonId, IReadOnlyCollection<MatchRating> ratings, CancellationToken ct = default)
    {
        var season = await db.Seasons.SingleAsync(s => s.Id == seasonId, ct);
        if (season.Number < AttributeEvolution.FirstSeasonNumber) return 0;
        var moves = ratings.Where(r => r.Rating is not null && AttributeEvolution.MatchDelta(r.Rating.Value) != 0).ToList();
        if (moves.Count == 0) return 0;
        var ids = moves.Select(r => r.PlayerId).Distinct().ToList();
        var positions = await db.Players.Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Position, ct);
        var start = await db.PlayerAttributes.Where(a => a.SeasonId == seasonId && ids.Contains(a.PlayerId))
            .ToDictionaryAsync(a => a.PlayerId, a => new Dictionary<string, int>(a.Values), ct);
        // Trần ±3/mùa chỉ tính tiến hoá theo trận: mốc = snapshot + phần admin sửa (bước 3c).
        var admin = await db.AttributeChanges.Where(c => c.SeasonId == seasonId && ids.Contains(c.PlayerId) && c.Source == AttributeChangeSource.Admin)
            .GroupBy(c => new { c.PlayerId, c.Field }).Select(g => new { g.Key.PlayerId, g.Key.Field, Delta = g.Sum(c => c.Delta) }).ToListAsync(ct);
        foreach (var c in admin)
            if (start.TryGetValue(c.PlayerId, out var v) && v.TryGetValue(c.Field, out var x)) v[c.Field] = x + c.Delta;
        var current = await CurrentAttributesAsync(seasonId, ids, ct);
        // PO 19/11 mục 5: chỉ tăng khi còn dưới tiềm năng (cố định từ mùa đầu) — ngôi sao đá hay không vượt trần mãi.
        var potentials = await TrainingService.PotentialsAsync(db, _price, seasonId, positions.Select(p => (p.Key, p.Value)).ToList(), ct);
        var at = clock.GetUtcNow();
        var changed = new HashSet<Guid>();
        foreach (var r in moves)
        {
            if (!start.TryGetValue(r.PlayerId, out var s0)) continue;
            var delta = AttributeEvolution.MatchDelta(r.Rating!.Value);
            if (delta > 0 && (!potentials.TryGetValue(r.PlayerId, out var pot) || _price.Overall(positions[r.PlayerId], current[r.PlayerId]) >= pot)) continue;
            foreach (var field in AttributeEvolution.RoleFields(positions[r.PlayerId], priceOptions))
            {
                if (!s0.TryGetValue(field, out var baseValue)) continue;
                var now = current[r.PlayerId][field];
                var next = Math.Clamp(AttributeEvolution.Clamp(baseValue, now + delta), 1, 99);
                if (next == now) continue;
                db.AttributeChanges.Add(new AttributeChange
                {
                    SeasonId = seasonId, PlayerId = r.PlayerId, FixtureId = r.FixtureId, Source = AttributeChangeSource.Match, Field = field, Delta = next - now, At = at,
                });
                current[r.PlayerId][field] = next;
                changed.Add(r.PlayerId);
            }
        }
        await db.SaveChangesAsync(ct);
        if (changed.Count > 0) await RecalculatePricesAsync(seasonId, ct, changed);
        return changed.Count;
    }

    private static ClubLedgerEntry Entry(Guid clubId, long amount, LedgerReason reason, Guid settlementId, DateTimeOffset at) =>
        new() { ClubId = clubId, Amount = amount, Reason = reason, RefType = nameof(RoundSettlement), RefId = settlementId, At = at };
}
