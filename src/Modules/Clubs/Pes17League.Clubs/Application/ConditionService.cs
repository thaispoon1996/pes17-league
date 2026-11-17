using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Infrastructure;

namespace Pes17League.Clubs.Application;

public sealed record ConditionRow(Guid PlayerId, string Name, Position Position, int Fatigue, int Morale, int InjuredRounds, string? Injury);

/// <summary>Thể lực / chấn thương / tinh thần (FM đợt 5), mô phỏng khi chốt vòng. Luật ở <see cref="ConditionRules"/>.</summary>
public sealed class ConditionService(ClubsDbContext db, TimeProvider clock)
{
    /// <summary>Gọi khi chốt vòng (sau tập luyện). CLB không có trận ở vòng (nghỉ) → chỉ hồi thể lực, không trừ tinh thần.</summary>
    public async Task<int> ApplyRoundAsync(Guid matchdayId, CancellationToken ct = default)
    {
        if (await db.PlayerConditions.AnyAsync(c => c.LastMatchdayId == matchdayId, ct)) return 0;
        var fixtures = await db.Fixtures.Where(x => x.MatchdayId == matchdayId && x.HomeGoals != null && x.AwayGoals != null)
            .Select(x => new { x.Id, x.HomeClubId, x.AwayClubId, Diff = x.HomeGoals!.Value - x.AwayGoals!.Value }).ToListAsync(ct);
        var diffByClub = fixtures.SelectMany(x => new[] { (x.HomeClubId, x.Diff), (x.AwayClubId, -x.Diff) }).ToDictionary(t => t.Item1, t => t.Item2);
        var fixtureIds = fixtures.Select(x => x.Id).ToList();
        var ratings = (await db.MatchRatings.Where(r => fixtureIds.Contains(r.FixtureId)).Select(r => new { r.PlayerId, r.Rating }).ToListAsync(ct))
            .GroupBy(r => r.PlayerId).ToDictionary(g => g.Key, g => g.First().Rating);
        var squad = await (from m in db.SquadMemberships
                           join c in db.Clubs on m.ClubId equals c.Id
                           where m.LeftAt == null && c.Status == ClubStatus.Active
                           select new { m.ClubId, m.PlayerId }).ToListAsync(ct);
        var ids = squad.Select(s => s.PlayerId).ToList();
        var conditions = await db.PlayerConditions.Where(c => ids.Contains(c.PlayerId)).ToDictionaryAsync(c => c.PlayerId, ct);
        var hard = (await db.ClubTrainings.Where(t => t.Intensity == TrainingIntensity.Hard).Select(t => t.ClubId).ToListAsync(ct)).ToHashSet();
        var staff = await BoardService.LevelsAsync(db, ct);   // FM đợt 7: y tế, trợ lý
        var now = clock.GetUtcNow();
        var injuries = 0;
        foreach (var s in squad)
        {
            if (!conditions.TryGetValue(s.PlayerId, out var c)) db.PlayerConditions.Add(c = conditions[s.PlayerId] = new PlayerCondition { PlayerId = s.PlayerId });
            var clubPlayed = diffByClub.TryGetValue(s.ClubId, out var diff);
            var played = ratings.TryGetValue(s.PlayerId, out var rating);
            var before = c.Fatigue;
            var physio = staff.GetValueOrDefault((s.ClubId, StaffRole.Physio), 1);
            if (c.InjuredRounds > 0 && --c.InjuredRounds == 0) c.Injury = null;
            if (played)
            {
                c.Fatigue = Math.Min(100, c.Fatigue + ConditionRules.MatchFatigue + (hard.Contains(s.ClubId) ? ConditionRules.HardTrainingFatigue : 0));
                var roll = ConditionRules.Roll(s.PlayerId, matchdayId);
                if (c.InjuredRounds == 0 && ConditionRules.Injured(before, roll, StaffRules.PhysioInjuryCut(physio)))
                {
                    c.InjuredRounds = ConditionRules.InjuryRounds(roll);
                    c.Injury = ConditionRules.Injuries[roll % ConditionRules.Injuries.Length];
                    injuries++;
                }
            }
            else c.Fatigue = Math.Max(0, c.Fatigue - ConditionRules.RestRecovery - StaffRules.PhysioRecovery(physio));
            if (c.Morale < 50) c.Morale += StaffRules.AssistantMorale(staff.GetValueOrDefault((s.ClubId, StaffRole.Assistant), 1));
            if (clubPlayed) c.Morale = Math.Clamp(c.Morale + ConditionRules.MoraleDelta(played, rating, diff), 0, 100);
            c.LastMatchdayId = matchdayId;
            c.UpdatedAt = now;
        }
        await db.SaveChangesAsync(ct);
        return injuries;
    }

    public async Task<Dictionary<Guid, PlayerCondition>> ForPlayersAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default) =>
        await db.PlayerConditions.AsNoTracking().Where(c => ids.Contains(c.PlayerId)).ToDictionaryAsync(c => c.PlayerId, ct);

    public async Task<List<ConditionRow>> SquadAsync(Guid clubId, CancellationToken ct = default)
    {
        var squad = await (from m in db.SquadMemberships
                           join p in db.Players on m.PlayerId equals p.Id
                           where m.ClubId == clubId && m.LeftAt == null
                           select new { p.Id, p.Name, p.Position }).ToListAsync(ct);
        var cond = await ForPlayersAsync(squad.Select(s => s.Id).ToList(), ct);
        return squad.Select(s => cond.GetValueOrDefault(s.Id) is { } c
                ? new ConditionRow(s.Id, s.Name, s.Position, c.Fatigue, c.Morale, c.InjuredRounds, c.Injury)
                : new ConditionRow(s.Id, s.Name, s.Position, 0, ConditionRules.DefaultMorale, 0, null))
            .OrderBy(r => r.Position).ThenBy(r => r.Name).ToList();
    }
}
