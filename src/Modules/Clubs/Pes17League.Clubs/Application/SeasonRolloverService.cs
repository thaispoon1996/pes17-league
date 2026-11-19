using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Infrastructure;

namespace Pes17League.Clubs.Application;

/// <param name="Retired">Cầu thủ giải nghệ; <paramref name="Deferred"/> = tới tuổi nhưng CLB sẽ dưới tối thiểu squad/thủ môn → đá thêm 1 mùa.</param>
/// <param name="Replaced">Cầu thủ tự do CLB BOT ký thay người giải nghệ.</param>
/// <param name="MyRetired">Người giải nghệ của CLB người chơi (tên, tuổi).</param>
public sealed record RolloverResult(string Season, int Players, int Retired, int Deferred, int Replaced, IReadOnlyList<string> MyRetired,
    IReadOnlyList<string> Moves, CareerStartResult Start);

/// <summary>
/// Sang mùa career PES17 (PO 19/11): mùa trước xong → tạo mùa N+1 cùng patch: chỉ số mới = chỉ số cuối mùa (snapshot + tiến hoá trận)
/// qua <see cref="AgingRules"/> (+1 tuổi, phát triển/sa sút theo tuổi), giải nghệ theo tuổi (CLB BOT ký cầu thủ tự do cùng tuyến, cùng tầm thay),
/// hết mệt; chia giải theo BXH (<see cref="DivisionService"/>: lên/xuống hạng, suất C1/C2), rồi bắt đầu mùa như lần đầu
/// (<see cref="Pes17CareerService.StartAsync"/>: tham số kinh tế, lịch, giá, ngân sách mùa mới, chợ). Hợp đồng tính theo trận nên không đổi.
/// Lỗi giữa chừng sau khi đã chia giải: gọi lại thì bỏ qua bước tạo mùa, chỉ bắt đầu mùa đang chờ.
/// </summary>
public sealed class SeasonRolloverService(ClubsDbContext db, IOptionFileStore store, TimeProvider clock)
{
    private readonly PriceModel _price = new();

    /// <summary>Mùa PES17 vừa xong, chưa có mùa sau (sang mùa được), không thì null.</summary>
    public async Task<Season?> ReadyAsync(CancellationToken ct = default)
    {
        var last = await db.Seasons.OrderByDescending(s => s.Number).FirstOrDefaultAsync(ct);
        return last is { Status: SeasonStatus.Finished } && await new Pes17RoundService(db, store, clock).IsPes17SeasonAsync(last.Id, ct) ? last : null;
    }

    public async Task<RolloverResult> NextSeasonAsync(int seed, string actor, Action<string>? progress = null, CancellationToken ct = default)
    {
        var career = new Pes17CareerService(db, store, clock);
        int players = 0, retired = 0, deferred = 0, replaced = 0;
        List<string> mine = [], moves = [];
        if (await ReadyAsync(ct) is { } last)
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var next = new Season { Name = $"Mùa {last.Number + 1}", Number = last.Number + 1, PatchVersionId = last.PatchVersionId, CreatedAt = clock.GetUtcNow() };
            db.Seasons.Add(next);
            await db.SaveChangesAsync(ct);
            progress?.Invoke($"Tạo {next.Name}: cộng tuổi, phát triển chỉ số…");
            var rng = new Random(seed);
            var (aged, age) = await AgeAsync(last.Id, next.Id, rng, ct);
            players = aged;
            progress?.Invoke("Giải nghệ…");
            (retired, deferred, replaced, mine) = await RetireAsync(next.Id, age, rng, actor, ct);
            await db.PlayerConditions.ExecuteUpdateAsync(s => s.SetProperty(c => c.Fatigue, 0), ct);
            var closeAt = clock.GetUtcNow();   // chợ / kỳ CN mùa cũ còn mở → đóng (mùa mới mở chợ riêng)
            await db.TransferWindows.Where(w => w.SeasonId == last.Id && w.Status == TransferWindowStatus.Open)
                .ExecuteUpdateAsync(s => s.SetProperty(w => w.Status, TransferWindowStatus.Closed).SetProperty(w => w.ClosesAt, closeAt), ct);
            progress?.Invoke("Chia giải theo BXH (lên/xuống hạng, suất C1/C2)…");
            var division = new DivisionService(db, clock);
            moves = Moves(await division.PreviewAsync(next.Id, ct), await LeagueOfAsync(last.Id, ct));
            await division.ApplyAsync(next.Id, actor, ct);
            db.AdminAudit.Add(new AdminAuditEntry
            {
                At = clock.GetUtcNow(), Actor = actor, EntityType = nameof(Season), EntityId = next.Id, Action = "Rollover",
                After = JsonSerializer.Serialize(new { From = last.Id, Players = players, Retired = retired, Deferred = deferred, Replaced = replaced }),
            });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            db.ChangeTracker.Clear();
        }
        else if (await career.PendingSeasonAsync(ct) is not { Number: > 1 })
            throw new AdminRuleException("Chưa sang mùa được: mùa hiện tại chưa kết thúc");

        progress?.Invoke("Bắt đầu mùa mới: kinh tế, lịch thi đấu, ngân sách, chợ…");
        var start = await career.StartAsync(null, null, seed, actor, ct);
        var manager = await db.TeamAssignments.Where(a => a.To == null && a.ControllerType == ControllerType.Human && a.ParticipantId != null)
            .Select(a => a.ParticipantId).FirstOrDefaultAsync(ct);
        if (manager is not null)
        {
            var body = $"Chào mừng {start.Season}! {start.Slots} khung ngày, {start.Matches} trận."
                + (mine.Count > 0 ? $"\nGiải nghệ ở CLB bạn: {string.Join(", ", mine)}. Vào Chuyển nhượng để bổ sung." : "")
                + (moves.Count > 0 ? "\n" + string.Join("\n", moves) : "");
            await new MailService(db, clock).SystemAsync(manager, $"Sang {start.Season}", body, ct: ct);
        }
        return new(start.Season, players, retired, deferred, replaced, mine, moves, start);
    }

    /// <summary>Snapshot mùa mới = chỉ số cuối mùa trước qua <see cref="AgingRules.NextSeason"/>. Trả số cầu thủ và tuổi mới.</summary>
    private async Task<(int Count, Dictionary<Guid, int> Age)> AgeAsync(Guid lastId, Guid nextId, Random rng, CancellationToken ct)
    {
        var values = await new EconomyService(db, clock).CurrentAttributesAsync(lastId, null, ct);
        var pos = await db.Players.Where(p => values.Keys.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Position, ct);
        var potentials = await TrainingService.PotentialsAsync(db, _price, lastId, pos.Select(kv => (kv.Key, kv.Value)).ToList(), ct);
        var now = clock.GetUtcNow();
        var age = new Dictionary<Guid, int>();
        foreach (var (id, v) in values.OrderBy(kv => kv.Key))
        {
            var known = pos.TryGetValue(id, out var p);
            var room = known && potentials.TryGetValue(id, out var pt) ? Math.Max(0, pt - _price.Overall(p, v)) : 0;
            var nv = AgingRules.NextSeason(v, known ? AttributeEvolution.RoleFields(p) : [], rng.Next(1000), room);
            age[id] = nv.GetValueOrDefault("AGE");
            db.PlayerAttributes.Add(new PlayerAttributes { PlayerId = id, SeasonId = nextId, Values = nv, CapturedAt = now });
        }
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        return (values.Count, age);
    }

    /// <summary>Giải nghệ theo tuổi mới (cầu thủ trong squad + tự do còn hiện). CLB BOT: ký trước cầu thủ tự do cùng tuyến, OVR gần nhất,
    /// ≤ <see cref="AgingRules.ReplacementMaxAge"/> tuổi (hợp đồng tối đa, lương tính lúc bắt đầu mùa), rồi mới cho người cũ nghỉ.</summary>
    private async Task<(int Retired, int Deferred, int Replaced, List<string> Mine)> RetireAsync(Guid seasonId, Dictionary<Guid, int> age, Random rng,
        string actor, CancellationToken ct)
    {
        var players = await db.Players.Where(p => p.MarketVisibility != false && age.Keys.Contains(p.Id))
            .Select(p => new { p.Id, p.Name, p.Position }).ToListAsync(ct);
        var club = await db.SquadMemberships.Where(m => m.LeftAt == null).ToDictionaryAsync(m => m.PlayerId, m => m.ClubId, ct);
        var humans = (await db.TeamAssignments.Where(a => a.To == null && a.ControllerType == ControllerType.Human).Select(a => a.ClubId).ToListAsync(ct)).ToHashSet();
        var values = await new EconomyService(db, clock).CurrentAttributesAsync(seasonId, null, ct);
        int Ovr(Guid id, Position p) => values.TryGetValue(id, out var v) ? _price.Overall(p, v) : 0;

        var leaving = players.Where(p => AgingRules.Retires(age[p.Id], rng.Next(1000))).OrderBy(p => p.Id).ToList();
        var leavingIds = leaving.Select(p => p.Id).ToHashSet();
        var pool = players.Where(p => !club.ContainsKey(p.Id) && !leavingIds.Contains(p.Id) && age[p.Id] <= AgingRules.ReplacementMaxAge)
            .Select(p => (p.Id, p.Position, Ovr: Ovr(p.Id, p.Position), Age: age[p.Id])).ToList();
        var admin = new PlayerAdminService(db, clock);
        var policy = new SquadPolicy<Guid>();
        int retired = 0, deferred = 0, replaced = 0;
        var mine = new List<string>();
        var now = clock.GetUtcNow();
        foreach (var p in leaving)
        {
            var at = club.GetValueOrDefault(p.Id);
            if (at != Guid.Empty && !humans.Contains(at))
            {
                var ovr = Ovr(p.Id, p.Position);
                var picks = pool.Where(c => c.Position.Group() == p.Position.Group()).OrderBy(c => Math.Abs(c.Ovr - ovr)).ThenBy(c => c.Age).Take(1).ToList();
                foreach (var c in picks)
                {
                    var bot = await db.Clubs.SingleAsync(x => x.Id == at, ct);
                    var squad = await SquadStore.LoadAsync(db, at, ct);
                    var add = policy.Add(squad.Entries, c.Id, c.Position);
                    if (add.Ok)
                    {
                        SquadStore.Apply(db, bot, squad, add, now);
                        db.Contracts.Add(new PlayerContract { PlayerId = c.Id, ClubId = at, MatchesTotal = ContractRules.MaxMatches, StartedAt = now });
                        db.AdminAudit.Add(new AdminAuditEntry { At = now, Actor = actor, EntityType = "Player", EntityId = c.Id, Action = "RolloverSign",
                            After = JsonSerializer.Serialize(new { ClubId = at, Replaces = p.Id }) });
                        await db.SaveChangesAsync(ct);
                        pool.Remove(c);
                        replaced++;
                    }
                    db.ChangeTracker.Clear();
                }
            }
            try
            {
                await admin.RetireAsync(p.Id, actor, ct);
                retired++;
                if (humans.Contains(at)) mine.Add($"{p.Name} ({age[p.Id]})");
            }
            catch (AdminRuleException) { deferred++; }
            db.ChangeTracker.Clear();
        }
        return (retired, deferred, replaced, mine);
    }

    private async Task<Dictionary<Guid, (string League, int Tier)>> LeagueOfAsync(Guid seasonId, CancellationToken ct) =>
        (await (from x in db.EditionClubs join e in db.Editions on x.EditionId equals e.Id join c in db.Competitions on e.CompetitionId equals c.Id
                where e.SeasonId == seasonId && c.Type == CompetitionType.DomesticLeague select new { x.ClubId, c.Name, c.Tier }).ToListAsync(ct))
        .GroupBy(r => r.ClubId).ToDictionary(g => g.Key, g => (g.First().Name, g.First().Tier ?? 1));

    /// <summary>Lên/xuống hạng: CLB đổi giải quốc nội so với mùa trước.</summary>
    private static List<string> Moves(SeasonAllocation plan, Dictionary<Guid, (string League, int Tier)> before)
    {
        var lines = new List<string>();
        foreach (var e in plan.Editions.Where(e => e.Competition.Type == CompetitionType.DomesticLeague))
        {
            var tier = e.Competition.Tier ?? 1;
            var moved = e.Clubs.Select(c => c.Club).Where(c => before.TryGetValue(c.ClubId, out var b) && b.League != e.Competition.Name).ToList();
            var up = moved.Where(c => before[c.ClubId].Tier > tier).Select(c => c.Name).ToList();
            var down = moved.Where(c => before[c.ClubId].Tier < tier).Select(c => c.Name).ToList();
            if (up.Count > 0) lines.Add($"Lên {e.Competition.Name}: {string.Join(", ", up)}");
            if (down.Count > 0) lines.Add($"Xuống {e.Competition.Name}: {string.Join(", ", down)}");
        }
        return lines;
    }
}
