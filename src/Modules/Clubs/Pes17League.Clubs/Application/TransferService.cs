using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Infrastructure;

namespace Pes17League.Clubs.Application;

public enum TransferRejectReason
{
    WindowClosed,
    PlayerNotFound,
    ClubNotFound,
    SameClub,
    InsufficientBudget,
    NegativeFee,
    InvalidContractLength,
    NegativeWage,
    HardCapExceeded,
    PriceMissing,
    FeeOutOfRange,
    /// <summary>Cầu thủ đang được mượn: không bán/thanh lý được.</summary>
    OnLoan,
    // Lỗi luật squad, cùng tên với SquadError
    PlayerNotInSquad, PlayerInOtherClub, AlreadyInSquad, SquadFull, BelowMinSquadSize, BelowMinGoalkeepers,
    InvalidShirtNumber, ShirtNumberTaken, NoFreeShirtNumber,
    /// <summary>Dính đội huyền thoại "… Classics" (PO 20/11): không vào thị trường chuyển nhượng.</summary>
    LegendClub,
}

/// <summary>Điều khoản thêm khi thực hiện đề nghị giữa 2 CLB người (FM đợt 3). Phí đã kiểm ở TransferOfferService.</summary>
public sealed record TransferTerms(TransferKind Kind, int Instalments = 1, int SellOnPercent = 0, Guid? OfferId = null);

public sealed record TransferResult(Guid? TransferId, TransferRejectReason? Error = null, bool NeedsLineupReview = false)
{
    public bool Ok => Error is null;
}

/// <summary>
/// Chuyển nhượng (luồng 2) và đổi số áo (luồng 3). Mỗi lệnh = 1 SaveChanges = 1 transaction:
/// squad cả hai CLB + hợp đồng + sổ tiền + bản ghi Transfer. Club.SquadVersion chặn 2 lệnh đồng thời cùng CLB
/// (lệnh sau ném DbUpdateConcurrencyException, gọi lại là được).
/// DB cập nhật ngay; OF chỉ sinh lại khi chốt vòng (PO duyệt 01/11).
/// </summary>
public sealed class TransferService(ClubsDbContext db, TimeProvider clock, SquadPolicyOptions? options = null)
{
    private readonly SquadPolicy<Guid> _policy = new(options);

    /// <summary>
    /// Mua: từ CLB khác (nếu cầu thủ đang có CLB) hoặc cầu thủ tự do. Fee theo giá PlayerPrice của mùa (Kinh tế đợt 2):
    /// tự do hoặc CLB bán là BOT → đúng giá; CLB bán có chủ → 80–200% giá (đã thương lượng xong trước khi gọi).
    /// Hợp đồng mới <paramref name="contractMatches"/> trận (C16), lương <paramref name="wage"/> mỗi vòng;
    /// chặn nếu quỹ lương CLB mua vượt trần cứng (C14) — mùa chưa đặt trần thì không chặn.
    /// </summary>
    public Task<TransferResult> BuyAsync(Guid windowId, Guid playerId, Guid toClubId, long fee, int contractMatches, long wage, CancellationToken ct = default) =>
        ExecuteAsync(windowId, playerId, toClubId, fee, contractMatches, wage, ct);

    /// <summary>Thực hiện đề nghị đã thoả thuận (mua đứt / trao đổi / mượn): bỏ kiểm khung phí, còn lại như <see cref="BuyAsync"/>.
    /// Trả góp: kỳ 1 (gồm phần dư) trả ngay, các kỳ sau trừ khi chốt vòng. Mượn: hợp đồng mượn nhớ CLB chủ quản.</summary>
    public Task<TransferResult> ExecuteDealAsync(Guid windowId, Guid playerId, Guid toClubId, long fee, int contractMatches, long wage, TransferTerms terms, CancellationToken ct = default) =>
        ExecuteAsync(windowId, playerId, toClubId, fee, contractMatches, wage, ct, terms);

    /// <summary>Thanh lý: cầu thủ thành tự do, không có tiền.</summary>
    public Task<TransferResult> ReleaseAsync(Guid windowId, Guid playerId, CancellationToken ct = default) =>
        ExecuteAsync(windowId, playerId, null, 0, null, null, ct);

    private async Task<TransferResult> ExecuteAsync(Guid windowId, Guid playerId, Guid? toClubId, long fee, int? contractMatches, long? wage, CancellationToken ct, TransferTerms? terms = null)
    {
        var now = clock.GetUtcNow();
        var player = await db.Players.FindAsync([playerId], ct);
        var window = await db.TransferWindows.FindAsync([windowId], ct);
        var current = await db.SquadMemberships.SingleOrDefaultAsync(m => m.PlayerId == playerId && m.LeftAt == null, ct);
        var fromClubId = current?.ClubId;

        var transfer = new Transfer
        {
            TransferWindowId = windowId, PlayerId = playerId, FromClubId = fromClubId, ToClubId = toClubId, Fee = fee,
            ContractMatches = contractMatches, Wage = wage,
            Kind = terms?.Kind ?? (toClubId is null ? TransferKind.Release : TransferKind.Buy), RequestedAt = now,
            Instalments = Math.Max(1, terms?.Instalments ?? 1), SellOnPercent = terms?.SellOnPercent ?? 0, OfferId = terms?.OfferId,
        };

        if (window is null || !window.IsOpenAt(now)) return await RejectAsync(transfer, TransferRejectReason.WindowClosed, window is not null && player is not null, ct);
        if (player is null) return await RejectAsync(transfer, TransferRejectReason.PlayerNotFound, false, ct);
        if (fee < 0) return await RejectAsync(transfer, TransferRejectReason.NegativeFee, true, ct);
        if (contractMatches is { } cm && !ContractRules.IsValidLength(cm)) return await RejectAsync(transfer, TransferRejectReason.InvalidContractLength, true, ct);
        if (wage < 0) return await RejectAsync(transfer, TransferRejectReason.NegativeWage, true, ct);
        if (toClubId is null && fromClubId is null) return await RejectAsync(transfer, TransferRejectReason.PlayerNotInSquad, true, ct);
        if (toClubId is not null && toClubId == fromClubId) return await RejectAsync(transfer, TransferRejectReason.SameClub, true, ct);

        Club? toClub = null, fromClub = null;
        if (toClubId is { } tid && (toClub = await db.Clubs.FindAsync([tid], ct)) is null)
            return await RejectAsync(transfer, TransferRejectReason.ClubNotFound, false, ct);
        if (fromClubId is { } fid) fromClub = await db.Clubs.FindAsync([fid], ct);
        if (LegendClubs.IsName(fromClub?.Name) || LegendClubs.IsName(toClub?.Name))
            return await RejectAsync(transfer, TransferRejectReason.LegendClub, true, ct);

        if (toClub is not null)
        {
            if (await db.PlayerPrices.FindAsync([playerId, window.SeasonId], ct) is not { } price)
                return await RejectAsync(transfer, TransferRejectReason.PriceMissing, true, ct);
            var negotiated = fromClub is not null && await SquadStore.IsHumanAsync(db, fromClub.Id, ct);
            if (terms is null && (negotiated ? !FeeRules.IsValidNegotiated(fee, price.Price) : fee != price.Price))
                return await RejectAsync(transfer, TransferRejectReason.FeeOutOfRange, true, ct);
            // HLV career (PO 19/11): năng lực Đàm phán đổi phí mua thẳng từ CLB BOT / chợ tự do (−1%/điểm trên 10).
            if (terms is null && !negotiated && await CoachHooks.BuyDiscountPctAsync(db, toClub.Id, ct) is var disc and not 0)
                transfer.Fee = fee = CoachRules.Discounted(price.Price, disc);
        }
        var contract = await db.Contracts.SingleOrDefaultAsync(c => c.PlayerId == playerId && c.Status == ContractStatus.Active, ct);
        if (contract?.LoanFromClubId is not null) return await RejectAsync(transfer, TransferRejectReason.OnLoan, true, ct);
        var upfront = fee - fee / transfer.Instalments * (transfer.Instalments - 1);

        // Kiểm luật cả hai bên trước khi ghi gì.
        Squad? from = null, to = null;
        SquadChange<Guid>? released = null, added = null;
        if (fromClub is not null)
        {
            from = await SquadStore.LoadAsync(db, fromClub.Id, ct);
            released = _policy.Release(from.Entries, playerId);
            if (!released.Ok) return await RejectAsync(transfer, Map(released.Error!.Value), true, ct);
        }
        if (toClub is not null)
        {
            to = await SquadStore.LoadAsync(db, toClub.Id, ct);
            added = _policy.Add(to.Entries, playerId, player.Position);
            if (!added.Ok) return await RejectAsync(transfer, Map(added.Error!.Value), true, ct);
            if (upfront > 0 && await BalanceAsync(toClub.Id, ct) < upfront)
                return await RejectAsync(transfer, TransferRejectReason.InsufficientBudget, true, ct);
            if (await db.SeasonEconomies.FindAsync([window.SeasonId], ct) is { SoftWageCap: > 0 } eco
                && await PayrollAsync(toClub.Id, ct) + (wage ?? 0) > LuxuryTax.HardCap(eco.SoftWageCap))
                return await RejectAsync(transfer, TransferRejectReason.HardCapExceeded, true, ct);
        }

        if (from is not null) SquadStore.Apply(db, fromClub!, from, released!, now);
        if (to is not null) SquadStore.Apply(db, toClub!, to, added!, now);

        if (contract is not null)
        {
            contract.Status = ContractStatus.Ended;
            contract.EndedAt = now;
            contract.EndReason = toClub is null ? ContractEndReason.Released : ContractEndReason.Transferred;
        }
        if (toClub is not null)
        {
            var loan = transfer.Kind == TransferKind.Loan && fromClub is not null;
            db.Contracts.Add(new PlayerContract
            {
                PlayerId = playerId, ClubId = toClub.Id, MatchesTotal = contractMatches, Wage = wage, StartedAt = now,
                LoanFromClubId = loan ? fromClub!.Id : null,
                LoanParentMatchesLeft = loan && contract?.MatchesTotal is { } mt ? mt - contract.MatchesPlayed : null,
                LoanParentWage = loan ? contract?.Wage : null,
            });
        }

        transfer.Status = TransferStatus.Completed;
        transfer.CompletedAt = now;
        transfer.ResultingLineupReview = released?.NeedsLineupReview ?? false;
        db.Transfers.Add(transfer);

        if (fee > 0)
        {
            db.Ledger.Add(new ClubLedgerEntry { ClubId = toClub!.Id, Amount = -upfront, Reason = LedgerReason.TransferIn, RefType = nameof(Transfer), RefId = transfer.Id, At = now });
            if (fromClub is not null)
                db.Ledger.Add(new ClubLedgerEntry { ClubId = fromClub.Id, Amount = upfront, Reason = LedgerReason.TransferOut, RefType = nameof(Transfer), RefId = transfer.Id, At = now });
            // % bán lại: CLB đã bán cầu thủ cho CLB đang bán (lần mua đứt gần nhất) nhận phần trăm phí.
            if (fromClub is not null && transfer.Kind != TransferKind.Loan
                && await db.Transfers.Where(x => x.PlayerId == playerId && x.ToClubId == fromClub.Id && x.Status == TransferStatus.Completed && x.Kind != TransferKind.Loan)
                    .OrderByDescending(x => x.CompletedAt).FirstOrDefaultAsync(ct) is { SellOnPercent: > 0, FromClubId: { } beneficiary } prior)
            {
                var cut = fee * prior.SellOnPercent / 100;
                db.Ledger.Add(new ClubLedgerEntry { ClubId = fromClub.Id, Amount = -cut, Reason = LedgerReason.SellOn, RefType = nameof(Transfer), RefId = transfer.Id, At = now });
                db.Ledger.Add(new ClubLedgerEntry { ClubId = beneficiary, Amount = cut, Reason = LedgerReason.SellOn, RefType = nameof(Transfer), RefId = transfer.Id, At = now });
            }
        }

        await db.SaveChangesAsync(ct);
        return new(transfer.Id, NeedsLineupReview: transfer.ResultingLineupReview);
    }

    public async Task<TransferRejectReason?> ChangeShirtNumberAsync(Guid clubId, Guid playerId, int number, CancellationToken ct = default)
    {
        var club = await db.Clubs.FindAsync([clubId], ct);
        if (club is null) return TransferRejectReason.ClubNotFound;
        var squad = await SquadStore.LoadAsync(db, clubId, ct);
        var r = _policy.SetShirtNumber(squad.Entries, playerId, number);
        if (!r.Ok) return Map(r.Error!.Value);
        SquadStore.Apply(db, club, squad, r, clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return null;
    }

    /// <summary>Quỹ lương mỗi vòng = tổng Wage hợp đồng Active (null tính 0).</summary>
    public Task<long> PayrollAsync(Guid clubId, CancellationToken ct = default) => SquadStore.PayrollAsync(db, clubId, ct);

    public Task<long> BalanceAsync(Guid clubId, CancellationToken ct = default) => SquadStore.BalanceAsync(db, clubId, ct);

    private async Task<TransferResult> RejectAsync(Transfer t, TransferRejectReason reason, bool persist, CancellationToken ct)
    {
        // Lưu lệnh bị từ chối để có lịch sử; không lưu khi khoá ngoại không tồn tại (window/CLB/cầu thủ sai).
        if (!persist) return new(null, reason);
        t.Status = TransferStatus.Rejected;
        t.RejectReason = reason.ToString();
        db.Transfers.Add(t);
        await db.SaveChangesAsync(ct);
        return new(t.Id, reason);
    }

    private static TransferRejectReason Map(SquadError e) => Enum.Parse<TransferRejectReason>(e.ToString());
}
