using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Infrastructure;

namespace Pes17League.Clubs.Application;

/// <summary>Áp tác động (<see cref="CoachFx"/>) của sự kiện / hồi âm lên HLV, BLĐ, CĐV, tinh thần cầu thủ. Không lưu.</summary>
internal sealed class CoachEffects(ClubsDbContext db, TimeProvider clock)
{
    /// <summary>Dòng tin cậy BLĐ của CLB trong mùa (tạo nếu chưa có, hạng kỳ vọng theo <see cref="BoardService.StatusAsync"/>).</summary>
    public async Task<BoardConfidence?> BoardAsync(Guid seasonId, Guid clubId, CancellationToken ct)
    {
        var b = db.BoardConfidences.Local.FirstOrDefault(x => x.ClubId == clubId && x.SeasonId == seasonId)
            ?? await db.BoardConfidences.SingleOrDefaultAsync(x => x.ClubId == clubId && x.SeasonId == seasonId, ct);
        if (b is not null) return b;
        if (await new BoardService(db, clock).StatusAsync(seasonId, clubId, ct) is not { } s) return null;
        db.BoardConfidences.Add(b = new BoardConfidence { ClubId = clubId, SeasonId = seasonId, ExpectedPosition = s.ExpectedPosition, UpdatedAt = clock.GetUtcNow() });
        return b;
    }

    public static Task<Guid> ActiveSeasonAsync(ClubsDbContext db, CancellationToken ct) =>
        db.Seasons.Where(s => s.Status == SeasonStatus.Active).OrderBy(s => s.Number).Select(s => s.Id).FirstOrDefaultAsync(ct);

    public async Task<string> ApplyAsync(Coach c, CoachFx fx, Guid? clubId, Guid? playerId, int tick, string note, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        CoachService.Money(db, c, fx.Money, fx.Money > 0 ? CoachMoney.Sponsor : CoachMoney.Event, note, tick, now);
        if (fx.Invest > 0)
        {
            db.CoachAssets.Add(new CoachAsset
            {
                CoachId = c.Id, Key = "dau-tu", Price = fx.Invest, MaturesTick = tick + CoachEvents.InvestRounds,
                ReturnPermille = CoachEvents.InvestReturn(CoachRules.Roll(c.Id, tick * 7 + 3)), Tick = tick, At = now,
            });
            CoachService.Money(db, c, -fx.Invest, CoachMoney.Investment, note, tick, now);
        }
        c.Reputation = Math.Clamp(c.Reputation + fx.Rep, 0, 100);
        c.Stress = Math.Clamp(c.Stress + fx.Stress, 0, CoachRules.StressMax);
        c.Xp += fx.Xp;
        c.UpdatedAt = now;

        var seasonId = await ActiveSeasonAsync(db, ct);
        if (clubId is { } club && seasonId != Guid.Empty)
        {
            if (fx.Board != 0 && await BoardAsync(seasonId, club, ct) is { } b)
            {
                b.Confidence = Math.Clamp(b.Confidence + fx.Board, 0, 100);
                b.UpdatedAt = now;
            }
            if (fx.Fans != 0 && (await new FinanceService(db, clock).EnsureAsync(seasonId, ct)).GetValueOrDefault(club) is { } fin)
                fin.Fans = Math.Clamp(fin.Fans + fin.Fans * fx.Fans / 1000, 1000, fin.Capacity * 2);
            if (fx.TeamMorale != 0) await MoraleAsync(club, null, fx.TeamMorale, now, ct);
        }
        if (playerId is { } p && fx.PlayerMorale != 0) await MoraleAsync(null, p, fx.PlayerMorale, now, ct);
        return CoachEvents.Hint(fx) is var h && h != "Không ảnh hưởng" ? "Đã áp dụng: " + h + "." : "Đã ghi nhận.";
    }

    /// <summary>Đổi tinh thần cả CLB (<paramref name="clubId"/>) hoặc 1 cầu thủ.</summary>
    public async Task MoraleAsync(Guid? clubId, Guid? playerId, int delta, DateTimeOffset now, CancellationToken ct)
    {
        var ids = playerId is { } one ? [one]
            : await db.SquadMemberships.Where(m => m.ClubId == clubId && m.LeftAt == null).Select(m => m.PlayerId).ToListAsync(ct);
        var cond = await db.PlayerConditions.Where(c => ids.Contains(c.PlayerId)).ToDictionaryAsync(c => c.PlayerId, ct);
        foreach (var id in ids)
        {
            if (!cond.TryGetValue(id, out var c))
                c = db.PlayerConditions.Local.FirstOrDefault(x => x.PlayerId == id) ?? db.PlayerConditions.Add(new PlayerCondition { PlayerId = id }).Entity;
            c.Morale = Math.Clamp(c.Morale + delta, 0, 100);
            c.UpdatedAt = now;
        }
    }
}
