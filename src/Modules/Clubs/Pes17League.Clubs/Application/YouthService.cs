using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Infrastructure;

namespace Pes17League.Clubs.Application;

public sealed record YouthStatus(int UsedThisSeason, int PerSeason, int FreeSlots, IReadOnlyList<(Guid PlayerId, string Name, Position Position)> Players);

public sealed record ScoutRow(Guid PlayerId, string Name, Guid? ClubId, int RoundsLeft);

/// <summary>Học viện trẻ + trinh sát (FM đợt 8). Luật ở <see cref="YouthRules"/>, <see cref="ScoutRules"/>.</summary>
public sealed class YouthService(ClubsDbContext db, TimeProvider clock)
{
    public async Task<YouthStatus> StatusAsync(Guid seasonId, Guid clubId, CancellationToken ct = default)
    {
        var used = await (from y in db.YouthIntakes join p in db.Players on y.PlayerId equals p.Id
                          where y.ClubId == clubId && y.SeasonId == seasonId select new { p.Id, p.Name, p.Position }).ToListAsync(ct);
        var free = await new PlayerAdminService(db, clock).FreeSlotsAsync(ct);
        return new(used.Count, YouthRules.PerSeason, free, used.Select(u => (u.Id, u.Name, u.Position)).ToList());
    }

    /// <summary>Đôn 1 cầu thủ trẻ: tạo ở ô OF trống (vào game từ OF vòng sau), ký HĐ 60 trận, vào cuối squad. Trả về lỗi hoặc null.</summary>
    public async Task<string?> PromoteAsync(Guid seasonId, Guid clubId, Position position, CancellationToken ct = default)
    {
        var status = await StatusAsync(seasonId, clubId, ct);
        if (status.UsedThisSeason >= YouthRules.PerSeason) return $"Mỗi mùa tối đa {YouthRules.PerSeason} cầu thủ trẻ.";
        if (status.FreeSlots <= YouthRules.ReserveSlots) return "Hết ô tạo cầu thủ trong file game (giữ lại cho admin).";
        var club = await db.Clubs.SingleAsync(c => c.Id == clubId, ct);
        var squad = await SquadStore.LoadAsync(db, clubId, ct);
        var policy = new SquadPolicy<Guid>();
        if (squad.Entries.Count >= policy.Options.MaxSquadSize) return $"Squad đã đủ {policy.Options.MaxSquadSize} người.";

        var admin = new PlayerAdminService(db, clock);
        var template = await admin.TemplateAsync(position, ct);
        var seed = HashCode.Combine(clubId, seasonId, status.UsedThisSeason, 31);
        var age = YouthRules.Age(seed);
        var main = Pes17League.OptionFile.PlayerField.Verified.Take(26).Select(f => f.Name).ToHashSet();
        var values = template.ToDictionary(kv => kv.Key, kv => kv.Key == "AGE" ? age
            : main.Contains(kv.Key) && kv.Value > 8 ? YouthRules.Attribute(kv.Value, HashCode.Combine(seed, kv.Key)) : kv.Value);
        var year = clock.GetUtcNow().Year;
        Guid playerId;
        try { playerId = await admin.CreateAsync(new PlayerInput(YouthRules.Name(seed), position, year - age, null), values, "Học viện " + club.Name, ct); }
        catch (AdminRuleException e) { return e.Message; }

        var now = clock.GetUtcNow();
        var after = policy.Add(squad.Entries, playerId, position);
        if (after.Error is not null) return "Không thêm được vào squad: " + after.Error;
        SquadStore.Apply(db, club, squad, after, now);
        db.Contracts.Add(new PlayerContract { PlayerId = playerId, ClubId = clubId, MatchesTotal = YouthRules.ContractMatches, Wage = YouthRules.Wage, StartedAt = now });
        db.YouthIntakes.Add(new YouthIntake { SeasonId = seasonId, ClubId = clubId, PlayerId = playerId, CreatedAt = now });
        await db.SaveChangesAsync(ct);
        return null;
    }

    /// <summary>CLB biết chính xác chỉ số: cầu thủ của mình, tự do, hoặc đã có báo cáo xong.</summary>
    public async Task<bool> KnowsAsync(Guid clubId, Guid playerId, CancellationToken ct = default) =>
        clubId == Guid.Empty
        || !await db.SquadMemberships.AnyAsync(m => m.PlayerId == playerId && m.LeftAt == null && m.ClubId != clubId, ct)
        || await db.ScoutReports.AnyAsync(r => r.ClubId == clubId && r.PlayerId == playerId && r.RoundsLeft == 0, ct);

    /// <summary>Cầu thủ CLB khác mà <paramref name="clubId"/> chưa trinh sát xong → danh sách hiện OVR dạng khoảng (<see cref="ScoutRules.Mask"/>).</summary>
    public async Task<HashSet<Guid>> HiddenAsync(Guid clubId, CancellationToken ct = default)
    {
        if (clubId == Guid.Empty) return [];
        var done = db.ScoutReports.Where(r => r.ClubId == clubId && r.RoundsLeft == 0).Select(r => r.PlayerId);
        return (await db.SquadMemberships.Where(m => m.LeftAt == null && m.ClubId != clubId && !done.Contains(m.PlayerId))
            .Select(m => m.PlayerId).ToListAsync(ct)).ToHashSet();
    }

    public async Task<ScoutReport?> ReportAsync(Guid clubId, Guid playerId, CancellationToken ct = default) =>
        await db.ScoutReports.AsNoTracking().SingleOrDefaultAsync(r => r.ClubId == clubId && r.PlayerId == playerId, ct);

    public async Task<string?> ScoutAsync(Guid clubId, Guid playerId, CancellationToken ct = default)
    {
        if (await db.ScoutReports.AnyAsync(r => r.ClubId == clubId && r.PlayerId == playerId, ct)) return "Đã giao trinh sát cầu thủ này.";
        var level = (await db.ClubStaff.FindAsync([clubId, StaffRole.Scout], ct))?.Level ?? 1;
        if (await db.ScoutReports.CountAsync(r => r.ClubId == clubId && r.RoundsLeft > 0, ct) >= ScoutRules.MaxPending(level))
            return $"Trinh sát đang bận ({ScoutRules.MaxPending(level)} việc). Nâng cấp trinh sát để làm song song nhiều hơn.";
        if (await SquadStore.BalanceAsync(db, clubId, ct) < ScoutRules.Fee) return "Không đủ tiền.";
        var now = clock.GetUtcNow();
        db.ScoutReports.Add(new ScoutReport { ClubId = clubId, PlayerId = playerId, RoundsLeft = ScoutRules.Rounds(level), RequestedAt = now });
        db.Ledger.Add(new ClubLedgerEntry { ClubId = clubId, Amount = -ScoutRules.Fee, Reason = LedgerReason.Staff, RefType = nameof(ScoutReport), RefId = playerId, At = now });
        await db.SaveChangesAsync(ct);
        return null;
    }

    public async Task<List<ScoutRow>> ScoutListAsync(Guid clubId, CancellationToken ct = default) =>
        await (from r in db.ScoutReports join p in db.Players on r.PlayerId equals p.Id
               where r.ClubId == clubId
               orderby r.RequestedAt descending
               select new ScoutRow(p.Id, p.Name, db.SquadMemberships.Where(m => m.PlayerId == p.Id && m.LeftAt == null).Select(m => (Guid?)m.ClubId).FirstOrDefault(), r.RoundsLeft))
            .ToListAsync(ct);

    /// <summary>Chốt vòng: báo cáo đang chờ −1 vòng (mỗi vòng 1 lần).</summary>
    public async Task ApplyRoundAsync(Guid matchdayId, CancellationToken ct = default)
    {
        foreach (var r in await db.ScoutReports.Where(r => r.RoundsLeft > 0 && r.LastMatchdayId != matchdayId).ToListAsync(ct))
        {
            r.RoundsLeft--;
            r.LastMatchdayId = matchdayId;
        }
        await db.SaveChangesAsync(ct);
    }
}
