using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Infrastructure;

namespace Pes17League.Clubs.Application;

public sealed record FinanceRound(int Matchday, IReadOnlyDictionary<LedgerReason, long> ByReason)
{
    public long Net => ByReason.Values.Sum();
}

public sealed record FinanceReport(ClubFinance Finance, long Balance, IReadOnlyDictionary<LedgerReason, long> Totals, IReadOnlyList<FinanceRound> Rounds,
    int RoundsLeft, long Forecast);

/// <summary>Thu chi theo vòng (FM đợt 6). Luật/số liệu ở <see cref="FinanceRules"/>.</summary>
public sealed class FinanceService(ClubsDbContext db, TimeProvider clock)
{
    /// <summary>Gọi khi chốt vòng, sau <see cref="EconomyService.SettleRoundAsync"/> (cần RoundSettlement). Mỗi vòng chỉ ghi 1 lần.</summary>
    public async Task ApplyRoundAsync(Guid seasonId, Guid matchdayId, CancellationToken ct = default)
    {
        var settlement = await db.RoundSettlements.SingleOrDefaultAsync(r => r.SeasonId == seasonId && r.MatchdayId == matchdayId, ct)
            ?? db.RoundSettlements.Local.SingleOrDefault(r => r.SeasonId == seasonId && r.MatchdayId == matchdayId);
        if (settlement is null) return;
        if (await db.Ledger.AnyAsync(l => l.RefId == settlement.Id && l.Reason == LedgerReason.TvRights, ct)) return;
        var finances = await EnsureAsync(seasonId, ct);
        var now = clock.GetUtcNow();
        void Add(Guid club, long amount, LedgerReason reason)
        {
            if (amount != 0) db.Ledger.Add(new ClubLedgerEntry { ClubId = club, Amount = amount, Reason = reason, RefType = nameof(RoundSettlement), RefId = settlement.Id, At = now });
        }
        var fixtures = await db.Fixtures.Where(x => x.MatchdayId == matchdayId && x.HomeGoals != null && x.AwayGoals != null).ToListAsync(ct);
        // PO 19/11 mục 5: TV, tài trợ, bảo trì sân, lương ban huấn luyện tính theo TRẬN — chỉ CLB có trận ở khung này
        // (lịch PES17 69 khung, mỗi CLB đá ~40–55; trước đây cộng cả khung nghỉ nên tiền phình). Lương cầu thủ vẫn theo khung (EconomyService).
        var playing = fixtures.SelectMany(x => new[] { x.HomeClubId, x.AwayClubId }).ToHashSet();
        var staff = await db.ClubStaff.Where(s => s.Level > 1).ToListAsync(ct);
        foreach (var s in staff.Where(s => finances.ContainsKey(s.ClubId) && playing.Contains(s.ClubId))) Add(s.ClubId, -StaffRules.Wage(s.Level), LedgerReason.Staff);   // FM đợt 7
        foreach (var f in finances.Values.Where(f => playing.Contains(f.ClubId)))
        {
            Add(f.ClubId, FinanceRules.TvPerRound, LedgerReason.TvRights);
            Add(f.ClubId, f.SponsorPerRound, LedgerReason.Sponsor);
            Add(f.ClubId, -f.Capacity * FinanceRules.UpkeepPerSeat, LedgerReason.Facility);
        }
        foreach (var x in fixtures)
        {
            var diff = x.HomeGoals!.Value - x.AwayGoals!.Value;
            if (finances.TryGetValue(x.HomeClubId, out var home))
            {
                Add(home.ClubId, FinanceRules.Attendance(home.Capacity, home.Fans, home.TicketPrice) * home.TicketPrice, LedgerReason.Ticket);
                home.Fans = FinanceRules.FansAfter(home.Fans, home.Capacity, diff);
            }
            if (finances.TryGetValue(x.AwayClubId, out var away)) away.Fans = FinanceRules.FansAfter(away.Fans, away.Capacity, -diff);
            Add(x.HomeClubId, diff > 0 ? FinanceRules.WinPrize : diff == 0 ? FinanceRules.DrawPrize : 0, LedgerReason.Prize);
            Add(x.AwayClubId, diff < 0 ? FinanceRules.WinPrize : diff == 0 ? FinanceRules.DrawPrize : 0, LedgerReason.Prize);
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Tạo ClubFinance còn thiếu cho CLB Active: sức chứa + tài trợ theo hạng giá trị squad.
    /// Đủ hết (thường gặp) → không xếp hạng giá trị (~0,1 s mỗi lần, trang career gọi nhiều lần).</summary>
    public async Task<Dictionary<Guid, ClubFinance>> EnsureAsync(Guid seasonId, CancellationToken ct = default)
    {
        var existing = await db.ClubFinances.ToDictionaryAsync(f => f.ClubId, ct);
        var activeIds = await db.Clubs.Where(c => c.Status == ClubStatus.Active).Select(c => c.Id).ToListAsync(ct);
        if (activeIds.All(existing.ContainsKey)) return activeIds.ToDictionary(id => id, id => existing[id]);
        var clubs = await db.Clubs.Where(c => c.Status == ClubStatus.Active).Select(c => new
        {
            c.Id,
            Value = (from m in db.SquadMemberships
                     join p in db.PlayerPrices on new { m.PlayerId, SeasonId = seasonId } equals new { p.PlayerId, p.SeasonId }
                     where m.ClubId == c.Id && m.LeftAt == null
                     select (long?)p.Price).Sum() ?? 0,
        }).OrderByDescending(c => c.Value).ThenBy(c => c.Id).ToListAsync(ct);
        var now = clock.GetUtcNow();
        for (var i = 0; i < clubs.Count; i++)
        {
            if (existing.ContainsKey(clubs[i].Id)) continue;
            var rep = FinanceRules.Reputation(i, clubs.Count);
            var cap = FinanceRules.DefaultCapacity(rep);
            var f = new ClubFinance { ClubId = clubs[i].Id, Capacity = cap, Fans = cap * 9 / 10, SponsorPerRound = FinanceRules.Sponsor(rep), UpdatedAt = now };
            db.ClubFinances.Add(f);
            existing[f.ClubId] = f;
        }
        var active = clubs.Select(c => c.Id).ToHashSet();
        return existing.Where(e => active.Contains(e.Key)).ToDictionary();
    }

    /// <summary>Đầu mùa (career): tính lại tài trợ mọi CLB theo uy tín mới (hạng giá trị squad) và mức <see cref="FinanceRules.SponsorBase"/> hiện hành.</summary>
    public async Task<int> RefreshSponsorsAsync(Guid seasonId, CancellationToken ct = default)
    {
        var finances = await EnsureAsync(seasonId, ct);
        var ranked = await db.Clubs.Where(c => c.Status == ClubStatus.Active).Select(c => new
        {
            c.Id,
            Value = (from m in db.SquadMemberships
                     join p in db.PlayerPrices on new { m.PlayerId, SeasonId = seasonId } equals new { p.PlayerId, p.SeasonId }
                     where m.ClubId == c.Id && m.LeftAt == null
                     select (long?)p.Price).Sum() ?? 0,
        }).OrderByDescending(c => c.Value).ThenBy(c => c.Id).Select(c => c.Id).ToListAsync(ct);
        var now = clock.GetUtcNow();
        for (var i = 0; i < ranked.Count; i++)
            if (finances.TryGetValue(ranked[i], out var f))
            {
                f.SponsorPerRound = FinanceRules.Sponsor(FinanceRules.Reputation(i, ranked.Count));
                f.UpdatedAt = now;
            }
        await db.SaveChangesAsync(ct);
        return ranked.Count;
    }

    public async Task<string?> SetTicketPriceAsync(Guid seasonId, Guid clubId, long price, CancellationToken ct = default)
    {
        if (price is < FinanceRules.MinTicketPrice or > FinanceRules.MaxTicketPrice) return $"Giá vé {FinanceRules.MinTicketPrice}–{FinanceRules.MaxTicketPrice}.";
        var f = (await EnsureAsync(seasonId, ct)).GetValueOrDefault(clubId);
        if (f is null) return "CLB không hoạt động.";
        f.TicketPrice = price;
        f.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return null;
    }

    /// <summary>Mở rộng sân +5.000 chỗ, trả ngay <see cref="FinanceRules.ExpandCost"/>.</summary>
    public async Task<string?> ExpandStadiumAsync(Guid seasonId, Guid clubId, CancellationToken ct = default)
    {
        var f = (await EnsureAsync(seasonId, ct)).GetValueOrDefault(clubId);
        if (f is null) return "CLB không hoạt động.";
        if (f.Capacity + FinanceRules.ExpandSeats > FinanceRules.MaxCapacity) return $"Sân tối đa {FinanceRules.MaxCapacity:N0} chỗ.";
        if (await SquadStore.BalanceAsync(db, clubId, ct) < FinanceRules.ExpandCost) return "Không đủ tiền.";
        f.Capacity += FinanceRules.ExpandSeats;
        f.UpdatedAt = clock.GetUtcNow();
        db.Ledger.Add(new ClubLedgerEntry { ClubId = clubId, Amount = -FinanceRules.ExpandCost, Reason = LedgerReason.Upgrade, RefType = nameof(ClubFinance), RefId = clubId, At = f.UpdatedAt });
        await db.SaveChangesAsync(ct);
        return null;
    }

    /// <summary>Báo cáo: tổng theo khoản cả mùa, 10 vòng gần nhất, dự báo số dư cuối mùa = số dư + TB ròng 5 vòng gần × số vòng còn lại.</summary>
    public async Task<FinanceReport> ReportAsync(Guid seasonId, Guid clubId, CancellationToken ct = default)
    {
        var finance = (await EnsureAsync(seasonId, ct)).GetValueOrDefault(clubId) ?? new ClubFinance { ClubId = clubId };
        await db.SaveChangesAsync(ct);
        var settlements = await (from r in db.RoundSettlements
                                 join m in db.Matchdays on r.MatchdayId equals m.Id
                                 where r.SeasonId == seasonId
                                 select new { r.Id, m.Number }).ToDictionaryAsync(x => x.Id, x => x.Number, ct);
        var ids = settlements.Keys.ToList();
        var entries = await db.Ledger.Where(l => l.ClubId == clubId).Select(l => new { l.Amount, l.Reason, l.RefId, l.At }).ToListAsync(ct);
        // Mùa = từ lúc mở kỳ chuyển nhượng đầu tiên của mùa (ngân sách đầu mùa ghi cùng lúc).
        var from = await db.TransferWindows.Where(w => w.SeasonId == seasonId).MinAsync(w => (DateTimeOffset?)w.OpensAt, ct);
        var season = entries.Where(e => (e.RefId is { } id && ids.Contains(id)) || (from is { } f && e.At >= f.AddMinutes(-5))).ToList();
        var totals = season.GroupBy(e => e.Reason).ToDictionary(g => g.Key, g => g.Sum(e => e.Amount));
        var rounds = entries.Where(e => e.RefId is { } id && settlements.ContainsKey(id))
            .GroupBy(e => settlements[e.RefId!.Value]).OrderByDescending(g => g.Key).Take(10)
            .Select(g => new FinanceRound(g.Key, g.GroupBy(e => e.Reason).ToDictionary(x => x.Key, x => x.Sum(e => e.Amount)))).ToList();
        var total = await db.Matchdays.CountAsync(m => m.SeasonId == seasonId, ct);
        var left = Math.Max(0, total - settlements.Count);
        var balance = entries.Sum(e => e.Amount);
        var avg = rounds.Take(5).Select(r => r.Net).DefaultIfEmpty(0).Average();
        return new(finance, balance, totals, rounds, left, balance + (long)(avg * left));
    }
}
