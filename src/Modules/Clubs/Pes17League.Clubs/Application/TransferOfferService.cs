using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Infrastructure;

namespace Pes17League.Clubs.Application;

/// <summary>Luật đề nghị chuyển nhượng (FM đợt 3, PO duyệt mặc định 12/11).</summary>
public static class OfferRules
{
    public const int MaxSellOnPercent = 30, MaxInstalments = 4, MaxLoanFeePercent = 50;
    /// <summary>Phí giải phóng 100–200% giá mô hình.</summary>
    public const int MinClausePercent = 100, MaxClausePercent = 200;
    /// <summary>Cầu thủ từ chối nếu lương đề nghị thấp hơn 90% lương hiện tại.</summary>
    public const int MinWagePercent = 90;

    public static bool IsValidClause(long clause, long price) => clause * 100 >= price * MinClausePercent && clause * 100 <= price * MaxClausePercent;
    public static bool IsValidLoanFee(long fee, long price) => fee >= 0 && fee * 100 <= price * MaxLoanFeePercent;
    public static bool PlayerAccepts(long offeredWage, long? currentWage) => currentWage is not { } w || offeredWage * 100 >= w * MinWagePercent;

    public static string Text(TransferRejectReason r) => r switch
    {
        TransferRejectReason.WindowClosed => "kỳ chuyển nhượng đã đóng",
        TransferRejectReason.InsufficientBudget => "không đủ tiền",
        TransferRejectReason.HardCapExceeded => "vượt trần lương cứng",
        TransferRejectReason.FeeOutOfRange => "phí ngoài khung cho phép",
        TransferRejectReason.InvalidContractLength => "hợp đồng phải 10–60 trận",
        TransferRejectReason.OnLoan => "cầu thủ đang được mượn",
        TransferRejectReason.SquadFull => "đội đã đủ 32 người",
        TransferRejectReason.BelowMinSquadSize => "đội bán sẽ dưới số người tối thiểu",
        TransferRejectReason.BelowMinGoalkeepers => "đội bán sẽ thiếu thủ môn",
        TransferRejectReason.PriceMissing => "cầu thủ chưa có giá mùa này",
        _ => r.ToString(),
    };
}

public enum OfferReply { Accept, Reject, Counter }

public sealed record OfferDraft(Guid PlayerId, Guid BuyerClubId, OfferKind Kind, long Fee, int ContractMatches, long Wage,
    Guid? SwapPlayerId = null, int SellOnPercent = 0, int Instalments = 1, string? Note = null);

public sealed record OfferResult(Guid? OfferId, string? Error = null, OfferStatus? Status = null)
{
    public bool Ok => Error is null;
}

public sealed record OfferRow(Guid Id, OfferKind Kind, OfferStatus Status, Guid PlayerId, string Player, Guid BuyerClubId, string Buyer,
    Guid SellerClubId, string Seller, long Fee, long? CounterFee, string? SwapPlayer, int SellOnPercent, int Instalments,
    int ContractMatches, long Wage, string? Note, string? FailReason, DateTimeOffset CreatedAt, long? Price);

/// <summary>
/// Đề nghị chuyển nhượng giữa 2 CLB có HLV người (FM đợt 3). CLB bán là BOT hoặc cầu thủ tự do thì vẫn mua thẳng qua <see cref="TransferService.BuyAsync"/>.
/// Hoàn tất = 1 transaction (trao đổi: 2 vụ chuyển nhượng); lỗi luật squad/tiền → đề nghị Failed, không đổi gì.
/// </summary>
public sealed class TransferOfferService(ClubsDbContext db, TimeProvider clock)
{
    public async Task<OfferResult> MakeAsync(Guid windowId, OfferDraft d, CancellationToken ct = default)
    {
        await ExpireStaleAsync(ct);
        var now = clock.GetUtcNow();
        var window = await db.TransferWindows.FindAsync([windowId], ct);
        if (window is null || !window.IsOpenAt(now)) return new(null, "Kỳ chuyển nhượng đang đóng.");
        var seller = await db.SquadMemberships.Where(m => m.PlayerId == d.PlayerId && m.LeftAt == null).Select(m => (Guid?)m.ClubId).SingleOrDefaultAsync(ct);
        if (seller is not { } sellerId) return new(null, "Cầu thủ tự do: mua thẳng ở thị trường.");
        if (sellerId == d.BuyerClubId) return new(null, "Cầu thủ đã ở CLB của bạn.");
        if (!await SquadStore.IsHumanAsync(db, sellerId, ct)) return new(null, "CLB bán là BOT: mua thẳng ở thị trường.");
        if (await db.TransferOffers.AnyAsync(o => o.BuyerClubId == d.BuyerClubId && o.PlayerId == d.PlayerId
                && (o.Status == OfferStatus.Pending || o.Status == OfferStatus.Countered), ct))
            return new(null, "Bạn đã có đề nghị đang chờ cho cầu thủ này.");

        var offer = new TransferOffer
        {
            TransferWindowId = windowId, PlayerId = d.PlayerId, BuyerClubId = d.BuyerClubId, SellerClubId = sellerId, Kind = d.Kind,
            Fee = d.Fee, SwapPlayerId = d.SwapPlayerId, SellOnPercent = d.SellOnPercent, Instalments = d.Instalments,
            ContractMatches = d.ContractMatches, Wage = d.Wage, Note = d.Note?.Trim() is { Length: > 0 } n ? n[..Math.Min(n.Length, 300)] : null,
            Status = OfferStatus.Pending, CreatedAt = now,
        };
        if (await ValidateAsync(offer, offer.Fee, ct) is { } error) return new(null, error);

        db.TransferOffers.Add(offer);
        var contract = await ContractAsync(d.PlayerId, ct);
        if (offer.Kind == OfferKind.Permanent && offer.SwapPlayerId is null && contract?.ReleaseClause is { } clause && offer.Fee >= clause)
        {
            offer.Note = ((offer.Note ?? "") + " [kích hoạt phí giải phóng]").Trim();
            await db.SaveChangesAsync(ct);
            return await CompleteAndMailAsync(offer, ct);
        }
        await db.SaveChangesAsync(ct);
        await MailAsync(offer, toBuyer: false, "Đề nghị chuyển nhượng mới", "CLB {buyer} gửi đề nghị mua {player} với giá {fee}. Vào mục Đề nghị chuyển nhượng để trả lời.", ct);
        return new(offer.Id, Status: offer.Status);
    }

    /// <summary>Thư hệ thống (PO 17/11 hòm thư) tới HLV người của CLB mua/bán; CLB BOT thì bỏ qua. {buyer} {seller} {player} {fee} {counter}.</summary>
    private async Task MailAsync(TransferOffer o, bool toBuyer, string subject, string body, CancellationToken ct)
    {
        var mail = new MailService(db, clock);
        var to = await mail.ManagerOfClubAsync(toBuyer ? o.BuyerClubId : o.SellerClubId, ct);
        if (to is null) return;
        var clubs = await db.Clubs.Where(c => c.Id == o.BuyerClubId || c.Id == o.SellerClubId).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var player = await db.Players.Where(p => p.Id == o.PlayerId).Select(p => p.Name).SingleOrDefaultAsync(ct) ?? "?";
        string F(long v) => v.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("vi-VN"));
        string Fill(string t) => t.Replace("{buyer}", clubs.GetValueOrDefault(o.BuyerClubId, "?")).Replace("{seller}", clubs.GetValueOrDefault(o.SellerClubId, "?"))
            .Replace("{player}", player).Replace("{fee}", F(o.Fee)).Replace("{counter}", o.CounterFee is { } c ? F(c) : "");
        await mail.SystemAsync(to, Fill(subject), Fill(body), o.PlayerId, ct: ct);
    }

    private async Task<OfferResult> CompleteAndMailAsync(TransferOffer o, CancellationToken ct)
    {
        var r = await CompleteAsync(o.Id, ct);
        if (r.Error is null)
            foreach (var buyer in new[] { true, false })
                await MailAsync(o, buyer, "Hoàn tất chuyển nhượng {player}", "{player} chuyển từ {seller} sang {buyer}. Thương vụ đã hoàn tất.", ct);
        return r;
    }

    /// <summary>CLB bán trả lời: chấp nhận (hoàn tất ngay), từ chối, hoặc trả giá <paramref name="counterFee"/>.</summary>
    public async Task<OfferResult> RespondAsync(Guid offerId, Guid sellerClubId, OfferReply reply, long? counterFee = null, CancellationToken ct = default)
    {
        await ExpireStaleAsync(ct);
        var o = await db.TransferOffers.FindAsync([offerId], ct);
        if (o is null || o.SellerClubId != sellerClubId) return new(null, "Không tìm thấy đề nghị.");
        if (o.Status != OfferStatus.Pending) return new(o.Id, "Đề nghị không còn chờ bạn trả lời.");
        o.RespondedAt = clock.GetUtcNow();
        switch (reply)
        {
            case OfferReply.Accept:
                return await CompleteAndMailAsync(o, ct);
            case OfferReply.Reject:
                o.Status = OfferStatus.Rejected;
                break;
            case OfferReply.Counter:
                if (counterFee is not { } cf) return new(o.Id, "Nhập giá trả.");
                if (await ValidateAsync(o, cf, ct) is { } error) return new(o.Id, error);
                o.CounterFee = cf;
                o.Status = OfferStatus.Countered;
                break;
        }
        await db.SaveChangesAsync(ct);
        if (o.Status == OfferStatus.Countered && !await SquadStore.IsHumanAsync(db, o.BuyerClubId, ct)) return await BotAnswerCounterAsync(o, ct);
        if (o.Status == OfferStatus.Rejected) await MailAsync(o, toBuyer: true, "Đề nghị mua {player} bị từ chối", "CLB {seller} đã từ chối đề nghị mua {player} giá {fee}.", ct);
        else await MailAsync(o, toBuyer: true, "{seller} trả giá {player}", "CLB {seller} trả giá {counter} cho {player} (bạn đề nghị {fee}). Vào mục Đề nghị chuyển nhượng để đồng ý hoặc rút.", ct);
        return new(o.Id, Status: o.Status);
    }

    /// <summary>CLB mua là BOT (chợ BOT career, <see cref="BotMarketRules"/>): đồng ý ngay nếu giá trả ≤ mức trần riêng của đề nghị và đủ tiền, không thì rút.</summary>
    private async Task<OfferResult> BotAnswerCounterAsync(TransferOffer o, CancellationToken ct)
    {
        var seasonId = await db.TransferWindows.Where(w => w.Id == o.TransferWindowId).Select(w => w.SeasonId).SingleAsync(ct);
        var price = (await db.PlayerPrices.FindAsync([o.PlayerId, seasonId], ct))?.Price ?? 0;
        var counter = o.CounterFee ?? long.MaxValue;
        if (counter <= BotMarketRules.WalkAway(price, o.Id) && await SquadStore.BalanceAsync(db, o.BuyerClubId, ct) >= counter)
            return await CompleteAndMailAsync(o, ct);
        o.Status = OfferStatus.Withdrawn;
        o.RespondedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await MailAsync(o, toBuyer: false, "{buyer} rút đề nghị mua {player}", "CLB {buyer} không chấp nhận giá {counter} cho {player} và đã rút đề nghị.", ct);
        return new(o.Id, Status: o.Status);
    }

    /// <summary>CLB mua: chấp nhận giá trả (hoàn tất) hoặc rút đề nghị (lúc đang chờ hoặc đã bị trả giá).</summary>
    public async Task<OfferResult> BuyerReplyAsync(Guid offerId, Guid buyerClubId, bool acceptCounter, CancellationToken ct = default)
    {
        await ExpireStaleAsync(ct);
        var o = await db.TransferOffers.FindAsync([offerId], ct);
        if (o is null || o.BuyerClubId != buyerClubId) return new(null, "Không tìm thấy đề nghị.");
        if (!o.IsOpen) return new(o.Id, "Đề nghị đã kết thúc.");
        if (acceptCounter)
        {
            if (o.Status != OfferStatus.Countered) return new(o.Id, "CLB bán chưa trả giá.");
            return await CompleteAndMailAsync(o, ct);
        }
        o.Status = OfferStatus.Withdrawn;
        o.RespondedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await MailAsync(o, toBuyer: false, "{buyer} rút đề nghị mua {player}", "CLB {buyer} đã rút đề nghị mua {player}.", ct);
        return new(o.Id, Status: o.Status);
    }

    /// <summary>Đặt/bỏ phí giải phóng cho cầu thủ của CLB (null = bỏ). Không áp cho cầu thủ đang mượn.</summary>
    public async Task<string?> SetReleaseClauseAsync(Guid clubId, Guid playerId, long? clause, CancellationToken ct = default)
    {
        var c = await ContractAsync(playerId, ct);
        if (c is null || c.ClubId != clubId) return "Cầu thủ không thuộc CLB của bạn.";
        if (c.LoanFromClubId is not null) return "Cầu thủ đang mượn.";
        if (clause is { } v)
        {
            var price = await CurrentPriceAsync(playerId, ct);
            if (price is null) return "Cầu thủ chưa có giá mùa này.";
            if (!OfferRules.IsValidClause(v, price.Value)) return $"Phí giải phóng phải {OfferRules.MinClausePercent}–{OfferRules.MaxClausePercent}% giá.";
        }
        c.ReleaseClause = clause;
        await db.SaveChangesAsync(ct);
        return null;
    }

    /// <summary>Kỳ chuyển nhượng đóng (hạn chót) → đề nghị còn treo hết hạn.</summary>
    public async Task<int> ExpireStaleAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var stale = await (from o in db.TransferOffers
                           join w in db.TransferWindows on o.TransferWindowId equals w.Id
                           where (o.Status == OfferStatus.Pending || o.Status == OfferStatus.Countered)
                                 && (w.Status != TransferWindowStatus.Open || w.ClosesAt <= now || w.OpensAt > now)
                           select o).ToListAsync(ct);
        foreach (var o in stale) { o.Status = OfferStatus.Expired; o.RespondedAt = now; }
        if (stale.Count > 0) await db.SaveChangesAsync(ct);
        return stale.Count;
    }

    public async Task<List<OfferRow>> ListAsync(Guid clubId, CancellationToken ct = default)
    {
        await ExpireStaleAsync(ct);
        var since = clock.GetUtcNow().AddDays(-30);
        var rows = await (from o in db.TransferOffers
                          where (o.BuyerClubId == clubId || o.SellerClubId == clubId) && (o.CreatedAt >= since || o.Status == OfferStatus.Pending || o.Status == OfferStatus.Countered)
                          join p in db.Players on o.PlayerId equals p.Id
                          join b in db.Clubs on o.BuyerClubId equals b.Id
                          join s in db.Clubs on o.SellerClubId equals s.Id
                          join w in db.TransferWindows on o.TransferWindowId equals w.Id
                          orderby o.CreatedAt descending
                          select new
                          {
                              o, Player = p.Name, Buyer = b.Name, Seller = s.Name,
                              Swap = db.Players.Where(x => x.Id == o.SwapPlayerId).Select(x => x.Name).FirstOrDefault(),
                              Price = db.PlayerPrices.Where(x => x.PlayerId == o.PlayerId && x.SeasonId == w.SeasonId).Select(x => (long?)x.Price).FirstOrDefault(),
                          }).Take(100).ToListAsync(ct);
        return rows.Select(r => new OfferRow(r.o.Id, r.o.Kind, r.o.Status, r.o.PlayerId, r.Player, r.o.BuyerClubId, r.Buyer, r.o.SellerClubId, r.Seller,
            r.o.Fee, r.o.CounterFee, r.Swap, r.o.SellOnPercent, r.o.Instalments, r.o.ContractMatches, r.o.Wage, r.o.Note, r.o.FailReason, r.o.CreatedAt, r.Price)).ToList();
    }

    private async Task<string?> ValidateAsync(TransferOffer o, long fee, CancellationToken ct)
    {
        if (fee < 0) return "Phí không được âm.";
        if (o.Wage < 0) return "Lương không được âm.";
        if (!ContractRules.IsValidLength(o.ContractMatches)) return $"Hợp đồng/thời gian mượn phải {ContractRules.MinMatches}–{ContractRules.MaxMatches} trận.";
        var window = await db.TransferWindows.FindAsync([o.TransferWindowId], ct);
        var price = window is null ? null : (await db.PlayerPrices.FindAsync([o.PlayerId, window.SeasonId], ct))?.Price;
        if (price is not { } p) return "Cầu thủ chưa có giá mùa này.";
        var contract = await ContractAsync(o.PlayerId, ct);
        if (contract?.LoanFromClubId is not null) return "Cầu thủ đang được mượn, không chuyển tiếp được.";
        if (o.Kind == OfferKind.Loan)
        {
            if (o.SwapPlayerId is not null || o.SellOnPercent != 0 || o.Instalments != 1) return "Mượn không kèm trao đổi, % bán lại hay trả góp.";
            if (!OfferRules.IsValidLoanFee(fee, p)) return $"Phí mượn tối đa {OfferRules.MaxLoanFeePercent}% giá ({p:N0}).";
            return null;
        }
        if (o.SellOnPercent is < 0 or > OfferRules.MaxSellOnPercent) return $"% bán lại 0–{OfferRules.MaxSellOnPercent}.";
        if (o.Instalments is < 1 or > OfferRules.MaxInstalments) return $"Trả góp 1–{OfferRules.MaxInstalments} kỳ.";
        long swapValue = 0;
        if (o.SwapPlayerId is { } swap)
        {
            var swapContract = await ContractAsync(swap, ct);
            if (swapContract is null || swapContract.ClubId != o.BuyerClubId) return "Cầu thủ trao đổi không thuộc CLB của bạn.";
            if (swapContract.LoanFromClubId is not null) return "Cầu thủ trao đổi đang được mượn.";
            swapValue = (await db.PlayerPrices.FindAsync([swap, window!.SeasonId], ct))?.Price ?? 0;
        }
        if (!FeeRules.IsValidNegotiated(fee + swapValue, p))
            return $"Tổng giá trị (tiền + cầu thủ trao đổi) phải {FeeRules.MinNegotiatedPercent}–{FeeRules.MaxNegotiatedPercent}% giá ({p:N0}).";
        return null;
    }

    private async Task<OfferResult> CompleteAsync(Guid offerId, CancellationToken ct)
    {
        var o = (await db.TransferOffers.FindAsync([offerId], ct))!;
        var now = clock.GetUtcNow();
        var current = await ContractAsync(o.PlayerId, ct);
        string? fail = null;
        if (current is null || current.ClubId != o.SellerClubId) fail = "Cầu thủ không còn ở CLB bán.";
        else if (!OfferRules.PlayerAccepts(o.Wage, current.Wage)) fail = "Cầu thủ từ chối: lương đề nghị thấp hơn 90% lương hiện tại.";
        if (fail is not null) return await FailAsync(o.Id, fail, ct);

        var transfers = new TransferService(db, clock);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        TransferResult r;
        if (o.SwapPlayerId is { } swap)
        {
            var sc = (await ContractAsync(swap, ct))!;
            var left = sc.MatchesTotal is { } mt ? mt - sc.MatchesPlayed : ContractRules.MaxMatches;
            r = await transfers.ExecuteDealAsync(o.TransferWindowId, swap, o.SellerClubId, 0,
                Math.Clamp(left, ContractRules.MinMatches, ContractRules.MaxMatches), sc.Wage ?? 0, new(TransferKind.Swap, OfferId: o.Id), ct);
            if (!r.Ok) { await tx.RollbackAsync(ct); return await FailAsync(o.Id, "Cầu thủ trao đổi: " + OfferRules.Text(r.Error!.Value), ct); }
        }
        var kind = o.Kind == OfferKind.Loan ? TransferKind.Loan : o.SwapPlayerId is null ? TransferKind.Buy : TransferKind.Swap;
        r = await transfers.ExecuteDealAsync(o.TransferWindowId, o.PlayerId, o.BuyerClubId, o.AgreedFee, o.ContractMatches, o.Wage,
            new(kind, o.Instalments, o.SellOnPercent, o.Id), ct);
        if (!r.Ok) { await tx.RollbackAsync(ct); return await FailAsync(o.Id, OfferRules.Text(r.Error!.Value), ct); }
        o.Status = OfferStatus.Completed;
        o.TransferId = r.TransferId;
        o.RespondedAt = now;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new(o.Id, Status: o.Status);
    }

    private async Task<OfferResult> FailAsync(Guid offerId, string reason, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var o = (await db.TransferOffers.FindAsync([offerId], ct))!;
        o.Status = OfferStatus.Failed;
        o.FailReason = reason.Length > 80 ? reason[..80] : reason;
        o.RespondedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return new(o.Id, "Không hoàn tất: " + reason, o.Status);
    }

    private Task<PlayerContract?> ContractAsync(Guid playerId, CancellationToken ct) =>
        db.Contracts.SingleOrDefaultAsync(c => c.PlayerId == playerId && c.Status == ContractStatus.Active, ct);

    private async Task<long?> CurrentPriceAsync(Guid playerId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var season = await db.TransferWindows.Where(w => w.OpensAt <= now).OrderByDescending(w => w.OpensAt).Select(w => (Guid?)w.SeasonId).FirstOrDefaultAsync(ct);
        return season is { } s ? (await db.PlayerPrices.FindAsync([playerId, s], ct))?.Price : null;
    }
}
