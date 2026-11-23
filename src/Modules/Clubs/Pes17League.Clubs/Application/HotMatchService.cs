using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Domain;
using Pes17League.Clubs.Infrastructure;

namespace Pes17League.Clubs.Application;

/// <param name="HomePos">Hạng hiện tại trong BXH giải khi lý do là BXH; null = xét theo sức mạnh (đầu mùa / loại trực tiếp) hoặc bán kết, chung kết.</param>
/// <param name="Reason">Vì sao là trận cầu đinh, hiện cho người xem ("Top 4 gặp nhau: hạng 1 – hạng 3", "Chung kết"…).</param>
/// <param name="Human">Có CLB của người chơi (người hoặc trợ lý cầm).</param>
public sealed record HotFixture(Guid FixtureId, Guid MatchdayId, int Matchday, Guid EditionId, string Competition, string? CompetitionLogo, FixtureStage Stage,
    Guid Home, Guid Away, string HomeName, string AwayName, int? HomePos, int? AwayPos, string Reason, bool Human, FixtureStatus Status);

/// <summary>
/// Trận cầu đinh (PO 23/11: "chỉ trận cầu đinh giữa các đội mạnh, top đầu"):
/// trận vòng tròn khi 2 đội đều đã đá ≥ <see cref="MinPlayed"/> trận → cả 2 trong top <see cref="Top"/> BXH giải;
/// đầu mùa / vòng loại trực tiếp → cả 2 trong top <see cref="Top"/> sức mạnh (OVR đội hình) của giải; bán kết, chung kết luôn tính.
/// Dùng cho: ghi hình chỉ trận cầu đinh (record-mode = hot), mỗi khung chọn 1 trận BOT đá thật trên máy game + phát trực tiếp
/// (<see cref="PickLiveAsync"/>), trang Trực tiếp liệt kê trận sắp tới.
/// </summary>
public sealed class HotMatchService(ClubsDbContext db, IOptionFileStore store, TimeProvider clock)
{
    public const int Top = 4, MinPlayed = 3;

    /// <summary>Trận cầu đinh trong các khung. <paramref name="humanEditionsOnly"/>: chỉ giải có CLB của người chơi tham dự.</summary>
    public async Task<IReadOnlyList<HotFixture>> ForMatchdaysAsync(IReadOnlyCollection<Guid> matchdayIds, bool humanEditionsOnly, CancellationToken ct = default)
    {
        var fixtures = await db.Fixtures.AsNoTracking().Where(f => matchdayIds.Contains(f.MatchdayId) && f.CompetitionEditionId != null).ToListAsync(ct);
        if (fixtures.Count == 0) return [];
        var humans = (await db.TeamAssignments.AsNoTracking().Where(a => a.To == null && a.ControllerType == ControllerType.Human)
            .Select(a => a.ClubId).ToListAsync(ct)).ToHashSet();
        var editionIds = fixtures.Select(f => f.CompetitionEditionId!.Value).Distinct().ToList();
        if (humanEditionsOnly)
        {
            var withHuman = (await db.EditionClubs.AsNoTracking().Where(x => editionIds.Contains(x.EditionId) && humans.Contains(x.ClubId))
                .Select(x => x.EditionId).Distinct().ToListAsync(ct)).ToHashSet();
            editionIds = [.. editionIds.Where(withHuman.Contains)];
            fixtures = [.. fixtures.Where(f => withHuman.Contains(f.CompetitionEditionId!.Value))];
            if (fixtures.Count == 0) return [];
        }
        var editions = await (from e in db.Editions.AsNoTracking() join c in db.Competitions on e.CompetitionId equals c.Id
                              where editionIds.Contains(e.Id) select new { e.Id, e.SeasonId, c.Name, c.LogoPath }).ToDictionaryAsync(x => x.Id, ct);
        var numbers = await db.Matchdays.AsNoTracking().Where(m => matchdayIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => m.Number, ct);
        var clubIds = fixtures.SelectMany(f => new[] { f.HomeClubId, f.AwayClubId }).Distinct().ToList();
        var names = await db.Clubs.AsNoTracking().Where(c => clubIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var comp = new CompetitionService(db, store, clock);
        var tables = new Dictionary<Guid, Dictionary<Guid, (int Pos, int Played)>>();
        foreach (var id in editionIds)
            tables[id] = (await comp.GetEditionStandingsAsync(id, ct)).Select((r, i) => (r.Club, Pos: i + 1, r.Played)).ToDictionary(x => x.Club, x => (x.Pos, x.Played));

        Dictionary<Guid, HashSet<Guid>>? strong = null;   // giải → top sức mạnh (chỉ tính khi cần: đầu mùa / loại trực tiếp)
        async Task<HashSet<Guid>> StrongAsync(Guid edition)
        {
            if (strong is null)
            {
                strong = [];
                var seasons = editions.Values.Select(e => e.SeasonId).Distinct().ToList();
                var patches = await db.Seasons.AsNoTracking().Where(s => seasons.Contains(s.Id) && s.PatchVersionId != null)
                    .ToDictionaryAsync(s => s.Id, s => s.PatchVersionId!.Value, ct);
                var members = (await db.EditionClubs.AsNoTracking().Where(x => editionIds.Contains(x.EditionId)).ToListAsync(ct)).ToLookup(x => x.EditionId, x => x.ClubId);
                foreach (var s in seasons.Where(patches.ContainsKey))
                {
                    var ovr = (await new DivisionService(db, clock).StrengthsAsync(s, patches[s], ct)).ToDictionary(x => x.ClubId, x => x.Ovr);
                    foreach (var e in editions.Values.Where(e => e.SeasonId == s))
                        strong[e.Id] = [.. members[e.Id].OrderByDescending(c => ovr.GetValueOrDefault(c)).Take(Top)];
                }
            }
            return strong.GetValueOrDefault(edition) ?? [];
        }

        var hot = new List<HotFixture>();
        foreach (var f in fixtures)
        {
            var ed = f.CompetitionEditionId!.Value;
            var t = tables.GetValueOrDefault(ed) ?? [];
            int? hp = t.TryGetValue(f.HomeClubId, out var h) ? h.Pos : null, ap = t.TryGetValue(f.AwayClubId, out var a) ? a.Pos : null;
            string? reason = null;
            var byTable = false;   // hạng chỉ hiện khi lý do là BXH (đầu mùa hạng chưa nói lên gì)
            if (f.Stage is FixtureStage.Final) reason = "Chung kết";
            else if (f.Stage is FixtureStage.SemiFinal) reason = "Bán kết";
            else if (f.Stage is FixtureStage.League or FixtureStage.LeaguePhase && h.Played >= MinPlayed && a.Played >= MinPlayed)
            {
                if (hp <= Top && ap <= Top) (reason, byTable) = ($"Top {Top} gặp nhau: hạng {Math.Min(hp.Value, ap.Value)} – hạng {Math.Max(hp.Value, ap.Value)}", true);
            }
            else if (await StrongAsync(ed) is var top && top.Contains(f.HomeClubId) && top.Contains(f.AwayClubId))
                reason = $"Hai đội trong top {Top} sức mạnh của giải";
            if (reason is null) continue;
            var e = editions[ed];
            hot.Add(new(f.Id, f.MatchdayId, numbers.GetValueOrDefault(f.MatchdayId), ed, e.Name, e.LogoPath, f.Stage, f.HomeClubId, f.AwayClubId,
                names.GetValueOrDefault(f.HomeClubId, "?"), names.GetValueOrDefault(f.AwayClubId, "?"), byTable ? hp : null, byTable ? ap : null, reason,
                humans.Contains(f.HomeClubId) || humans.Contains(f.AwayClubId), f.Status));
        }
        return [.. hot.OrderBy(x => x.Matchday).ThenBy(x => Rank(x))];
    }

    /// <summary>Thứ tự ưu tiên: chung kết, bán kết, rồi tổng hạng nhỏ nhất (1–2 trước 3–4).</summary>
    private static int Rank(HotFixture x) => x.Stage switch { FixtureStage.Final => 0, FixtureStage.SemiFinal => 1, _ => 2 + (x.HomePos ?? Top) + (x.AwayPos ?? Top) };

    public async Task<bool> IsHotAsync(Guid fixtureId, CancellationToken ct = default)
    {
        var md = await db.Fixtures.AsNoTracking().Where(f => f.Id == fixtureId).Select(f => f.MatchdayId).SingleAsync(ct);
        return (await ForMatchdaysAsync([md], false, ct)).Any(x => x.FixtureId == fixtureId);
    }

    /// <summary>
    /// Trận BOT đá thật trên máy game + phát trực tiếp của khung (tối đa 1): trận cầu đinh chưa đá, chưa từng lên máy (lượt lỗi/huỷ → mô phỏng như thường),
    /// không cần người điều khiển, ở giải có CLB người chơi. Khung đã có trận cầu đinh người chơi tự đá → không chọn thêm (trận đó là tâm điểm, khỏi chờ máy).
    /// </summary>
    public async Task<HotFixture?> PickLiveAsync(Guid matchdayId, CancellationToken ct = default)
    {
        var hot = await ForMatchdaysAsync([matchdayId], true, ct);
        if (hot.Count == 0) return null;
        var ids = hot.Select(h => h.FixtureId).ToList();
        var fx = await db.Fixtures.AsNoTracking().Where(f => ids.Contains(f.Id)).ToDictionaryAsync(f => f.Id, ct);
        var humans = await MatchOrchestrator.HumanClubsAsync(db, fx.Values.SelectMany(f => new[] { f.HomeClubId, f.AwayClubId }), ct);
        if (hot.Any(h => MatchOrchestrator.NeedsHuman(fx[h.FixtureId], humans))) return null;
        var tried = (await db.MatchAttempts.AsNoTracking().Where(m => ids.Contains(m.FixtureId)).Select(m => m.FixtureId).Distinct().ToListAsync(ct)).ToHashSet();
        return hot.FirstOrDefault(h => h.Status == FixtureStatus.Scheduled && !tried.Contains(h.FixtureId));
    }
}
