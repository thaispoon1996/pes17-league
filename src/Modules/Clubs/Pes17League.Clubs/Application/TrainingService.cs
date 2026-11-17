using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Infrastructure;

namespace Pes17League.Clubs.Application;

public sealed record TrainingPlayerRow(Guid PlayerId, string Name, Position Position, int Age, int Overall, double PotentialStars, int SeasonDelta);

/// <summary>Tập luyện + phát triển cầu thủ (FM đợt 4). Luật ở <see cref="TrainingRules"/>.</summary>
public sealed class TrainingService(ClubsDbContext db, TimeProvider clock, PriceModelOptions? priceOptions = null)
{
    private readonly PriceModel _model = new(priceOptions);

    public async Task<ClubTraining> GetAsync(Guid clubId, CancellationToken ct = default) =>
        await db.ClubTrainings.AsNoTracking().SingleOrDefaultAsync(t => t.ClubId == clubId, ct) ?? new ClubTraining { ClubId = clubId };

    public async Task SetAsync(Guid clubId, TrainingFocus focus, TrainingIntensity intensity, CancellationToken ct = default)
    {
        var t = await db.ClubTrainings.FindAsync([clubId], ct);
        if (t is null) db.ClubTrainings.Add(t = new ClubTraining { ClubId = clubId });
        t.Focus = focus;
        t.Intensity = intensity;
        t.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Gọi khi chốt vòng (sau tiến hoá theo trận). Chỉ chạy ở vòng chia hết cho <see cref="TrainingRules.EveryRounds"/>. Trả về số cầu thủ đổi chỉ số.</summary>
    public async Task<int> ApplyAsync(Guid seasonId, int matchdayNumber, CancellationToken ct = default)
    {
        if (matchdayNumber <= 0 || matchdayNumber % TrainingRules.EveryRounds != 0) return 0;
        var session = matchdayNumber / TrainingRules.EveryRounds;
        var squad = await (from m in db.SquadMemberships
                           join c in db.Clubs on m.ClubId equals c.Id
                           join p in db.Players on m.PlayerId equals p.Id
                           where m.LeftAt == null && c.Status == ClubStatus.Active
                           select new { m.ClubId, p.Id, p.Position }).ToListAsync(ct);
        if (squad.Count == 0) return 0;
        var ids = squad.Select(s => s.Id).ToList();
        var plans = await db.ClubTrainings.ToDictionaryAsync(t => t.ClubId, ct);
        var start = await db.PlayerAttributes.Where(a => a.SeasonId == seasonId && ids.Contains(a.PlayerId))
            .ToDictionaryAsync(a => a.PlayerId, a => new Dictionary<string, int>(a.Values), ct);
        var startOverall = squad.Where(s => start.ContainsKey(s.Id)).ToDictionary(s => s.Id, s => _model.Overall(s.Position, start[s.Id]));
        // Trần ±3/mùa: mốc = snapshot + phần admin sửa (như tiến hoá theo trận).
        var admin = await db.AttributeChanges.Where(c => c.SeasonId == seasonId && ids.Contains(c.PlayerId) && c.Source == AttributeChangeSource.Admin)
            .GroupBy(c => new { c.PlayerId, c.Field }).Select(g => new { g.Key.PlayerId, g.Key.Field, Delta = g.Sum(c => c.Delta) }).ToListAsync(ct);
        foreach (var c in admin)
            if (start.TryGetValue(c.PlayerId, out var v) && v.TryGetValue(c.Field, out var x)) v[c.Field] = x + c.Delta;
        var current = await new EconomyService(db, clock, priceOptions).CurrentAttributesAsync(seasonId, ids, ct);

        var staff = await BoardService.LevelsAsync(db, ct);   // FM đợt 7: HLV tập luyện
        var at = clock.GetUtcNow();
        var changed = new HashSet<Guid>();
        foreach (var s in squad)
        {
            if (!start.TryGetValue(s.Id, out var s0) || !current.TryGetValue(s.Id, out var now)) continue;
            var plan = plans.GetValueOrDefault(s.ClubId) ?? new ClubTraining { ClubId = s.ClubId };
            var age = now.GetValueOrDefault("AGE", 25);
            var potential = TrainingRules.Potential(s.Id, startOverall[s.Id], age);
            var focus = plan.Focus == TrainingFocus.Goalkeeping && s.Position.Group() != PositionGroup.Goalkeeper ? TrainingFocus.Balanced : plan.Focus;
            var fields = TrainingRules.Fields(focus, AttributeEvolution.RoleFields(s.Position, priceOptions));

            void Move(string field, int delta)
            {
                if (!s0.TryGetValue(field, out var baseValue) || !now.TryGetValue(field, out var value)) return;
                var next = Math.Clamp(AttributeEvolution.Clamp(baseValue, value + delta), 1, 99);
                if (next == value) return;
                db.AttributeChanges.Add(new AttributeChange { SeasonId = seasonId, PlayerId = s.Id, Source = AttributeChangeSource.Training, Field = field, Delta = next - value, At = at });
                now[field] = next;
                changed.Add(s.Id);
            }

            static int Growth(int n, int coach) => n > 0 ? n + StaffRules.CoachGrowthBonus(coach) : 0;
            if (_model.Overall(s.Position, now) < potential)
                foreach (var f in TrainingRules.Pick(fields, Growth(TrainingRules.GrowthFields(age, plan.Intensity), staff.GetValueOrDefault((s.ClubId, StaffRole.Coach), 1)), s.Id, session)) Move(f, +1);
            foreach (var f in TrainingRules.Pick(TrainingRules.PhysicalFields, TrainingRules.DeclineFields(age, plan.Focus, plan.Intensity), s.Id, session)) Move(f, -1);
        }
        await db.SaveChangesAsync(ct);
        if (changed.Count > 0) await new EconomyService(db, clock, priceOptions).RecalculatePricesAsync(seasonId, ct, changed);
        return changed.Count;
    }

    /// <summary>Bảng tập luyện của CLB: tuổi, chỉ số tổng hiện tại, tiềm năng (sao), tổng đổi chỉ số mùa này (trận + tập).</summary>
    public async Task<List<TrainingPlayerRow>> SquadAsync(Guid seasonId, Guid clubId, CancellationToken ct = default)
    {
        var squad = await (from m in db.SquadMemberships
                           join p in db.Players on m.PlayerId equals p.Id
                           where m.ClubId == clubId && m.LeftAt == null
                           select new { p.Id, p.Name, p.Position }).ToListAsync(ct);
        var ids = squad.Select(s => s.Id).ToList();
        var start = await db.PlayerAttributes.AsNoTracking().Where(a => a.SeasonId == seasonId && ids.Contains(a.PlayerId)).ToDictionaryAsync(a => a.PlayerId, a => a.Values, ct);
        var current = await new EconomyService(db, clock, priceOptions).CurrentAttributesAsync(seasonId, ids, ct);
        var deltas = await db.AttributeChanges.Where(c => c.SeasonId == seasonId && ids.Contains(c.PlayerId) && c.Source != AttributeChangeSource.Admin)
            .GroupBy(c => c.PlayerId).Select(g => new { g.Key, D = g.Sum(c => c.Delta) }).ToDictionaryAsync(x => x.Key, x => x.D, ct);
        return squad.Where(s => start.ContainsKey(s.Id) && current.ContainsKey(s.Id)).Select(s =>
        {
            var age = current[s.Id].GetValueOrDefault("AGE", 25);
            var pot = TrainingRules.Potential(s.Id, _model.Overall(s.Position, start[s.Id]), age);
            return new TrainingPlayerRow(s.Id, s.Name, s.Position, age, _model.Overall(s.Position, current[s.Id]), TrainingRules.Stars(pot), deltas.GetValueOrDefault(s.Id));
        }).OrderBy(r => r.Position).ThenByDescending(r => r.Overall).ToList();
    }
}
