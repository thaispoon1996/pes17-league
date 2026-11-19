using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Infrastructure;

namespace Pes17League.Clubs.Application;

/// <param name="Ran">Chợ đang mở và khung này chưa chạy.</param>
/// <param name="Deals">Vụ CLB BOT mua (từ BOT khác hoặc tự do).</param>
/// <param name="Released">Cầu thủ CLB BOT thanh lý để giữ squad gọn.</param>
/// <param name="Offers">Đề nghị BOT gửi CLB người chơi.</param>
public sealed record BotMarketResult(bool Ran, int Deals, int Released, int Offers);

/// <summary>
/// Chợ chuyển nhượng BOT career (PO 19/11 mục 4) — luật ở <see cref="BotMarketRules"/>. Chỉ CLB đang dự giải mùa này và cầu thủ tự do. Chạy đầu mỗi khung khi chợ PES17 đang mở,
/// mỗi khung đúng 1 lần (AdminAudit "BotMarket" theo khung). Mọi vụ đi qua <see cref="TransferService"/> / <see cref="TransferOfferService"/>
/// nên vẫn đúng luật squad, tiền, trần lương; vụ bị từ chối thì bỏ qua.
/// </summary>
public sealed class BotMarketService(ClubsDbContext db, TimeProvider clock)
{
    public const string AuditAction = "BotMarket";
    /// <summary>BOT chỉ hỏi mua cầu thủ người chơi có giá từ mức này, chọn trong nhóm đắt nhất.</summary>
    public const long OfferMinPrice = 100_000;
    public const int OfferTopPlayers = 8;

    sealed class ClubState(Guid id)
    {
        public Guid Id { get; } = id;
        public List<MarketPlayer> Squad { get; } = [];
        public int Count;
        public long Balance;
        SquadNeed? _need;
        HashSet<Guid>? _starters;
        public SquadNeed Need => _need ??= BotMarketRules.Need(Squad.Select(p => (p.Position.Group(), p.Ovr)));
        public HashSet<Guid> Starters => _starters ??= BotMarketRules.Starters(Squad);
        public void Changed() { _need = null; _starters = null; }
    }

    public async Task<BotMarketResult> RunAsync(Guid matchdayId, CancellationToken ct = default)
    {
        var none = new BotMarketResult(false, 0, 0, 0);
        var md = await db.Matchdays.AsNoTracking().SingleOrDefaultAsync(m => m.Id == matchdayId, ct);
        if (md is null) return none;
        var now = clock.GetUtcNow();
        var window = await new FreeMarketService(db, clock).WindowAsync(md.SeasonId, ct);
        if (window is null || !window.IsOpenAt(now)) return none;
        if (await db.AdminAudit.AnyAsync(a => a.Action == AuditAction && a.EntityId == matchdayId, ct)) return none;

        var seasonId = md.SeasonId;
        var permille = (await db.SeasonEconomies.AsNoTracking().SingleOrDefaultAsync(e => e.SeasonId == seasonId, ct))?.WagePermille ?? 0;
        var prices = await db.PlayerPrices.AsNoTracking().Where(p => p.SeasonId == seasonId)
            .ToDictionaryAsync(p => p.PlayerId, p => (p.Overall, p.Price), ct);
        var ages = (await db.PlayerAttributes.AsNoTracking().Where(a => a.SeasonId == seasonId).Select(a => new { a.PlayerId, a.Values }).ToListAsync(ct))
            .ToDictionary(a => a.PlayerId, a => a.Values.GetValueOrDefault("AGE", 99));
        var players = await db.Players.AsNoTracking().Where(p => p.MarketVisibility != false).Select(p => new { p.Id, p.Position }).ToListAsync(ct);
        var members = await db.SquadMemberships.AsNoTracking().Where(m => m.LeftAt == null).ToDictionaryAsync(m => m.PlayerId, m => m.ClubId, ct);
        var contracts = await db.Contracts.AsNoTracking().Where(c => c.Status == ContractStatus.Active)
            .Select(c => new { c.PlayerId, c.Wage, Loan = c.LoanFromClubId != null }).ToListAsync(ct);
        var onLoan = contracts.Where(c => c.Loan).Select(c => c.PlayerId).ToHashSet();
        var wages = contracts.GroupBy(c => c.PlayerId).ToDictionary(g => g.Key, g => g.First().Wage);
        // Người vừa chuyển trong kỳ này không bị bán tiếp (tránh qua tay liên tục).
        var moved = (await db.Transfers.AsNoTracking().Where(t => t.TransferWindowId == window.Id && t.Status == TransferStatus.Completed && t.ToClubId != null)
            .Select(t => t.PlayerId).ToListAsync(ct)).ToHashSet();
        var humans = (await db.TeamAssignments.AsNoTracking().Where(a => a.To == null && a.ControllerType == ControllerType.Human)
            .Select(a => a.ClubId).ToListAsync(ct)).ToHashSet();
        // Chợ gồm CLB đang dự giải mùa này (loại đội huyền thoại / ML ảo / giải không mô phỏng) + cầu thủ tự do; CLB mua = BOT đá giải quốc nội.
        var entries = await (from x in db.EditionClubs
                             join e in db.Editions on x.EditionId equals e.Id
                             join k in db.Competitions on e.CompetitionId equals k.Id
                             where e.SeasonId == seasonId
                             select new { x.ClubId, League = k.Type == CompetitionType.DomesticLeague }).ToListAsync(ct);
        var inSeason = entries.Select(x => x.ClubId).ToHashSet();
        var botIds = (await db.TeamAssignments.AsNoTracking().Where(a => a.To == null && a.ControllerType == ControllerType.Bot)
            .Select(a => a.ClubId).ToListAsync(ct)).ToHashSet();
        var bots = entries.Where(x => x.League && botIds.Contains(x.ClubId)).Select(x => x.ClubId).ToHashSet();
        var balances = await db.Ledger.AsNoTracking().GroupBy(e => e.ClubId).Select(g => new { g.Key, Sum = g.Sum(e => e.Amount) })
            .ToDictionaryAsync(x => x.Key, x => x.Sum, ct);

        var clubs = new Dictionary<Guid, ClubState>();
        ClubState Club(Guid id) => clubs.TryGetValue(id, out var s) ? s : clubs[id] = new ClubState(id) { Balance = balances.GetValueOrDefault(id) };
        foreach (var (pid, cid) in members) Club(cid).Count++;
        var market = new Dictionary<Guid, MarketPlayer>();
        foreach (var p in players)
        {
            if (!prices.TryGetValue(p.Id, out var pr)) continue;
            Guid? at = members.TryGetValue(p.Id, out var cid) ? cid : null;
            var mp = new MarketPlayer(p.Id, p.Position, pr.Overall, ages.GetValueOrDefault(p.Id, 99), pr.Price, at);
            market[p.Id] = mp;
            if (at is { } c) Club(c).Squad.Add(mp);
        }

        var rng = new Random(BitConverter.ToInt32(matchdayId.ToByteArray(), 0));
        var transfers = new TransferService(db, clock);
        int deals = 0, released = 0, offers = 0;

        // (a) BOT ↔ BOT / tự do.
        foreach (var buyerId in bots.OrderBy(id => id).OrderBy(_ => rng.Next()).ToList())
        {
            if (deals >= BotMarketRules.DealsPerSlot) break;
            var buyer = Club(buyerId);
            if (buyer.Count >= BotMarketRules.BuyerMaxSquad || buyer.Squad.Count < 11) continue;
            var need = buyer.Need;
            var pick = BotMarketRules.Best(market.Values.Where(p =>
                p.ClubId != buyerId && !onLoan.Contains(p.Id) && !moved.Contains(p.Id)
                && BotMarketRules.Fits(p, need, buyer.Balance)
                && (p.ClubId is not { } sid || (inSeason.Contains(sid) && !humans.Contains(sid) && Club(sid) is var seller
                    && BotMarketRules.SellerLets(seller.Count, seller.Starters.Contains(p.Id), seller.Need.Level, need.Level)))));
            if (pick is null) continue;
            var r = await transfers.BuyAsync(window.Id, pick.Id, buyerId, pick.Price, ContractRules.MaxMatches, ContractRules.Wage(pick.Price, permille), ct);
            if (!r.Ok) continue;
            deals++;
            moved.Add(pick.Id);
            if (pick.ClubId is { } from)
            {
                var s = Club(from);
                s.Squad.RemoveAll(x => x.Id == pick.Id); s.Count--; s.Balance += pick.Price; s.Changed();
            }
            var bought = pick with { ClubId = buyerId };
            market[pick.Id] = bought;
            buyer.Squad.Add(bought); buyer.Count++; buyer.Balance -= pick.Price; buyer.Changed();

            if (buyer.Count > BotMarketRules.TrimAbove
                && BotMarketRules.TrimPick(buyer.Squad, buyer.Starters, new HashSet<Guid>(onLoan) { pick.Id }) is { } cut
                && (await transfers.ReleaseAsync(window.Id, cut.Id, ct)).Ok)
            {
                released++;
                buyer.Squad.RemoveAll(x => x.Id == cut.Id); buyer.Count--; buyer.Changed();
                market[cut.Id] = cut with { ClubId = null };
            }
        }

        // (b) BOT hỏi mua cầu thủ của CLB người chơi.
        var offerService = new TransferOfferService(db, clock);
        await offerService.ExpireStaleAsync(ct);   // đề nghị kỳ trước (chợ đã đóng) không chiếm suất
        foreach (var humanId in humans.OrderBy(id => id))
        {
            var open = await db.TransferOffers.AsNoTracking().Where(o => o.SellerClubId == humanId
                    && (o.Status == OfferStatus.Pending || o.Status == OfferStatus.Countered) && !humans.Contains(o.BuyerClubId))
                .Select(o => o.PlayerId).ToListAsync(ct);
            if (open.Count >= BotMarketRules.MaxOpenOffersToHuman || rng.Next(100) >= BotMarketRules.OfferChancePercent) continue;
            var targets = Club(humanId).Squad.Where(p => p.Price >= OfferMinPrice && !onLoan.Contains(p.Id) && !open.Contains(p.Id))
                .OrderByDescending(p => p.Price).ThenBy(p => p.Id).Take(OfferTopPlayers).ToList();
            if (targets.Count == 0) continue;
            var target = targets[rng.Next(targets.Count)];
            var fee = BotMarketRules.OfferFee(target.Price, rng.Next(1000));
            var buyers = bots.Select(Club).Where(b => b.Count < BotMarketRules.BuyerMaxSquad && b.Squad.Count >= 11 && b.Balance >= fee
                    && b.Need.Level >= target.Ovr - BotMarketRules.LevelMargin)
                .OrderBy(b => b.Id).ToList();
            if (buyers.Count == 0) continue;
            var buyerId = buyers[rng.Next(buyers.Count)].Id;
            var wage = Math.Max(ContractRules.Wage(target.Price, permille), wages.GetValueOrDefault(target.Id) ?? 0);
            var r = await offerService.MakeAsync(window.Id,
                new OfferDraft(target.Id, buyerId, OfferKind.Permanent, fee, ContractRules.MaxMatches, wage, Note: "CLB BOT hỏi mua"), ct);
            if (r.Ok) offers++;
        }

        db.AdminAudit.Add(new AdminAuditEntry
        {
            At = now, Actor = "bot", EntityType = nameof(Matchday), EntityId = matchdayId, Action = AuditAction,
            After = JsonSerializer.Serialize(new { Deals = deals, Released = released, Offers = offers }),
        });
        await db.SaveChangesAsync(ct);
        return new(true, deals, released, offers);
    }
}
