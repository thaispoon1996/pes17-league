using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Infrastructure;

namespace Pes17League.Clubs.Application;

public enum RenewalRejectReason
{
    NoActiveContract,
    OnLoan,
    InvalidContractLength,
    PriceMissing,
    FeeOutOfRange,
    AlreadyPending,
    InsufficientBudget,
    HardCapExceeded,
    NotPending,
    ContractChanged,
}

public sealed record RenewalResult(Guid? RenewalId, RenewalStatus? Status, RenewalRejectReason? Error = null)
{
    public bool Ok => Error is null;
}

/// <summary>
/// Gia hạn hợp đồng (Kinh tế đợt 2, PO chọn 01/11). CLB hiện tại đề nghị số trận (10–60) + phí trả CLB gốc (0–100% giá);
/// lương mới = WagePermille × giá hiện tại. Cần chủ CLB gốc đồng ý (<see cref="DecideAsync"/>); tự đồng ý khi CLB gốc là BOT,
/// không còn tham gia giải, cầu thủ gốc tự do, hoặc đang ở chính CLB gốc — 2 trường hợp sau không có người nhận nên phí phải 0.
/// Đề nghị lúc nào cũng được (không cần kỳ chuyển nhượng — chưa chốt). Chưa có xác thực: lớp API kiểm ai được duyệt.
/// </summary>
public sealed class ContractService(ClubsDbContext db, TimeProvider clock)
{
    public async Task<RenewalResult> RequestRenewalAsync(Guid seasonId, Guid playerId, int matches, long fee, CancellationToken ct = default)
    {
        var contract = await db.Contracts.SingleOrDefaultAsync(c => c.PlayerId == playerId && c.Status == ContractStatus.Active, ct);
        if (contract is null) return new(null, null, RenewalRejectReason.NoActiveContract);
        if (contract.LoanFromClubId is not null) return new(null, null, RenewalRejectReason.OnLoan);
        if (!ContractRules.IsValidLength(matches)) return new(null, null, RenewalRejectReason.InvalidContractLength);
        if (await db.PlayerPrices.FindAsync([playerId, seasonId], ct) is not { } price) return new(null, null, RenewalRejectReason.PriceMissing);
        if (await db.ContractRenewals.AnyAsync(r => r.ContractId == contract.Id && r.Status == RenewalStatus.Pending, ct))
            return new(null, null, RenewalRejectReason.AlreadyPending);

        var player = (await db.Players.FindAsync([playerId], ct))!;
        var origin = player.OriginClubId is { } oid && oid != contract.ClubId ? await db.Clubs.FindAsync([oid], ct) : null;
        var recipient = origin is { Status: ClubStatus.Active } ? origin.Id : (Guid?)null;
        if (!FeeRules.IsValidRenewal(fee, price.Price) || (recipient is null && fee != 0))
            return new(null, null, RenewalRejectReason.FeeOutOfRange);

        var eco = await db.SeasonEconomies.FindAsync([seasonId], ct) ?? new SeasonEconomy { SeasonId = seasonId };
        var renewal = new ContractRenewal
        {
            SeasonId = seasonId, ContractId = contract.Id, PlayerId = playerId, ClubId = contract.ClubId, OriginClubId = recipient,
            Matches = matches, Fee = fee, Wage = ContractRules.Wage(price.Price, eco.WagePermille),
            Status = RenewalStatus.Pending, RequestedAt = clock.GetUtcNow(),
        };
        if (await CheckAsync(renewal, contract, eco, ct) is { } error) return new(null, null, error);
        db.ContractRenewals.Add(renewal);
        if (recipient is null || !await SquadStore.IsHumanAsync(db, recipient.Value, ct))
        {
            renewal.AutoApproved = true;
            Complete(renewal, contract);
        }
        await db.SaveChangesAsync(ct);
        return new(renewal.Id, renewal.Status);
    }

    /// <summary>Chủ CLB gốc đồng ý / từ chối. Đồng ý → kiểm lại số dư + trần cứng rồi gia hạn ngay.</summary>
    public async Task<RenewalResult> DecideAsync(Guid renewalId, bool approve, CancellationToken ct = default)
    {
        var renewal = await db.ContractRenewals.FindAsync([renewalId], ct);
        if (renewal is not { Status: RenewalStatus.Pending }) return new(renewalId, renewal?.Status, RenewalRejectReason.NotPending);
        var contract = await db.Contracts.FindAsync([renewal.ContractId], ct);
        if (contract is not { Status: ContractStatus.Active }) return new(renewalId, renewal.Status, RenewalRejectReason.ContractChanged);
        if (!approve)
        {
            renewal.Status = RenewalStatus.Rejected;
            renewal.RejectReason = "OriginDeclined";
            renewal.DecidedAt = clock.GetUtcNow();
        }
        else
        {
            var eco = await db.SeasonEconomies.FindAsync([renewal.SeasonId], ct) ?? new SeasonEconomy { SeasonId = renewal.SeasonId };
            if (await CheckAsync(renewal, contract, eco, ct) is { } error) return new(renewalId, renewal.Status, error);
            Complete(renewal, contract);
        }
        await db.SaveChangesAsync(ct);
        return new(renewalId, renewal.Status);
    }

    private async Task<RenewalRejectReason?> CheckAsync(ContractRenewal r, PlayerContract current, SeasonEconomy eco, CancellationToken ct)
    {
        if (r.Fee > 0 && await SquadStore.BalanceAsync(db, r.ClubId, ct) < r.Fee) return RenewalRejectReason.InsufficientBudget;
        if (eco.SoftWageCap > 0 && await SquadStore.PayrollAsync(db, r.ClubId, ct) - (current.Wage ?? 0) + r.Wage > LuxuryTax.HardCap(eco.SoftWageCap))
            return RenewalRejectReason.HardCapExceeded;
        return null;
    }

    private void Complete(ContractRenewal r, PlayerContract old)
    {
        var now = clock.GetUtcNow();
        old.Status = ContractStatus.Ended;
        old.EndedAt = now;
        old.EndReason = ContractEndReason.Renewed;
        db.Contracts.Add(new PlayerContract { PlayerId = r.PlayerId, ClubId = r.ClubId, MatchesTotal = r.Matches, Wage = r.Wage, StartedAt = now });
        if (r.Fee > 0)
        {
            db.Ledger.Add(new ClubLedgerEntry { ClubId = r.ClubId, Amount = -r.Fee, Reason = LedgerReason.RenewalFee, RefType = nameof(ContractRenewal), RefId = r.Id, At = now });
            db.Ledger.Add(new ClubLedgerEntry { ClubId = r.OriginClubId!.Value, Amount = r.Fee, Reason = LedgerReason.RenewalFee, RefType = nameof(ContractRenewal), RefId = r.Id, At = now });
        }
        r.Status = RenewalStatus.Completed;
        r.DecidedAt = now;
    }
}
