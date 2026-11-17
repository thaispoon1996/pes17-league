using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Infrastructure;

namespace Pes17League.Clubs.Application;

public sealed record BoardStatus(string League, int Teams, int ExpectedPosition, int? Position, string Objective, int Confidence, string Verdict);

public sealed record StaffRow(StaffRole Role, int Level, long Wage, long NextFee);

/// <summary>Ban lãnh đạo + nhân sự (FM đợt 7). Luật ở <see cref="BoardRules"/>, <see cref="StaffRules"/>.</summary>
public sealed class BoardService(ClubsDbContext db, TimeProvider clock)
{
    /// <summary>Gọi khi chốt vòng: cập nhật độ tin cậy mọi CLB trong giải quốc nội có trận vòng này. Mỗi vòng 1 lần.</summary>
    public async Task ApplyRoundAsync(Guid seasonId, Guid matchdayId, CancellationToken ct = default)
    {
        var tables = await LeagueTablesAsync(seasonId, ct);
        var results = await db.Fixtures.Where(x => x.MatchdayId == matchdayId && x.HomeGoals != null && x.AwayGoals != null)
            .Select(x => new { x.HomeClubId, x.AwayClubId, D = x.HomeGoals!.Value - x.AwayGoals!.Value }).ToListAsync(ct);
        var diff = results.SelectMany(r => new[] { (r.HomeClubId, r.D), (r.AwayClubId, -r.D) }).ToDictionary(t => t.Item1, t => t.Item2);
        var rows = await db.BoardConfidences.Where(b => b.SeasonId == seasonId).ToDictionaryAsync(b => b.ClubId, ct);
        var now = clock.GetUtcNow();
        foreach (var (clubId, d) in diff)
        {
            if (!tables.TryGetValue(clubId, out var t)) continue;
            if (!rows.TryGetValue(clubId, out var b))
                db.BoardConfidences.Add(b = rows[clubId] = new BoardConfidence { ClubId = clubId, SeasonId = seasonId, ExpectedPosition = t.Expected });
            if (b.LastMatchdayId == matchdayId) continue;
            b.Confidence = Math.Clamp(b.Confidence + BoardRules.Delta(t.Position, b.ExpectedPosition, d), 0, 100);
            b.LastMatchdayId = matchdayId;
            b.UpdatedAt = now;
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<BoardStatus?> StatusAsync(Guid seasonId, Guid clubId, CancellationToken ct = default)
    {
        var tables = await LeagueTablesAsync(seasonId, ct);
        if (!tables.TryGetValue(clubId, out var t)) return null;
        var b = await db.BoardConfidences.AsNoTracking().SingleOrDefaultAsync(x => x.ClubId == clubId && x.SeasonId == seasonId, ct);
        var expected = b?.ExpectedPosition ?? t.Expected;
        var confidence = b?.Confidence ?? BoardRules.StartConfidence;
        return new(t.League, t.Teams, expected, t.Played ? t.Position : null, BoardRules.Objective(expected, t.Teams), confidence, BoardRules.Verdict(confidence));
    }

    sealed record Table(string League, int Teams, int Expected, int Position, bool Played);

    /// <summary>Theo từng giải quốc nội của mùa: hạng kỳ vọng (theo tổng giá squad) và hạng hiện tại.</summary>
    async Task<Dictionary<Guid, Table>> LeagueTablesAsync(Guid seasonId, CancellationToken ct)
    {
        var editions = await (from e in db.Editions join c in db.Competitions on e.CompetitionId equals c.Id
                              where e.SeasonId == seasonId && c.Type == CompetitionType.DomesticLeague select new { e.Id, c.Name }).ToListAsync(ct);
        var ids = editions.Select(e => e.Id).ToList();
        var members = await db.EditionClubs.Where(x => ids.Contains(x.EditionId)).Select(x => new { x.EditionId, x.ClubId }).ToListAsync(ct);
        var clubIds = members.Select(m => m.ClubId).ToList();
        var values = await (from m in db.SquadMemberships join p in db.PlayerPrices on m.PlayerId equals p.PlayerId
                            where m.LeftAt == null && p.SeasonId == seasonId && clubIds.Contains(m.ClubId)
                            group p.Price by m.ClubId into g select new { g.Key, V = g.Sum() }).ToDictionaryAsync(x => x.Key, x => x.V, ct);
        var played = await db.Fixtures.Where(f => f.CompetitionEditionId != null && ids.Contains(f.CompetitionEditionId.Value) && f.Status == FixtureStatus.Played
                && f.Stage == FixtureStage.League && f.HomeGoals != null && f.AwayGoals != null)
            .Select(f => new { E = f.CompetitionEditionId!.Value, P = new PlayedFixture<Guid>(f.HomeClubId, f.AwayClubId, f.HomeGoals!.Value, f.AwayGoals!.Value) }).ToListAsync(ct);
        var result = new Dictionary<Guid, Table>();
        foreach (var e in editions)
        {
            var clubs = members.Where(m => m.EditionId == e.Id).Select(m => m.ClubId).ToList();
            var fixtures = played.Where(p => p.E == e.Id).Select(p => p.P).ToList();
            var standings = Standings.Compute(clubs, fixtures, id => id.ToString());
            var expected = clubs.OrderByDescending(c => values.GetValueOrDefault(c)).ThenBy(c => c).ToList();
            for (var i = 0; i < standings.Count; i++)
                result[standings[i].Club] = new(e.Name, clubs.Count, expected.IndexOf(standings[i].Club) + 1, i + 1, fixtures.Count > 0);
        }
        return result;
    }

    public async Task<List<StaffRow>> StaffAsync(Guid clubId, CancellationToken ct = default)
    {
        var levels = await db.ClubStaff.Where(s => s.ClubId == clubId).ToDictionaryAsync(s => s.Role, s => s.Level, ct);
        return Enum.GetValues<StaffRole>().Select(r =>
        {
            var l = levels.GetValueOrDefault(r, 1);
            return new StaffRow(r, l, StaffRules.Wage(l), l < StaffRules.MaxLevel ? StaffRules.SigningFee(l + 1) : 0);
        }).ToList();
    }

    public static async Task<Dictionary<(Guid Club, StaffRole Role), int>> LevelsAsync(ClubsDbContext db, CancellationToken ct = default) =>
        await db.ClubStaff.ToDictionaryAsync(s => (s.ClubId, s.Role), s => s.Level, ct);

    /// <summary>Thuê người cấp cao hơn 1 bậc (trả phí ký ngay) hoặc hạ xuống cấp thấp hơn (miễn phí, giảm lương).</summary>
    public async Task<string?> SetLevelAsync(Guid clubId, StaffRole role, int level, CancellationToken ct = default)
    {
        if (level is < 1 or > StaffRules.MaxLevel) return $"Cấp 1–{StaffRules.MaxLevel}.";
        var s = await db.ClubStaff.FindAsync([clubId, role], ct);
        var current = s?.Level ?? 1;
        if (level == current) return null;
        var now = clock.GetUtcNow();
        if (level > current)
        {
            var fee = StaffRules.SigningFee(level);
            if (await db.Ledger.Where(l => l.ClubId == clubId).SumAsync(l => l.Amount, ct) < fee) return "Không đủ tiền.";
            db.Ledger.Add(new ClubLedgerEntry { ClubId = clubId, Amount = -fee, Reason = LedgerReason.Staff, RefType = nameof(ClubStaff), RefId = clubId, At = now });
        }
        if (s is null) db.ClubStaff.Add(s = new ClubStaff { ClubId = clubId, Role = role });
        s.Level = level;
        s.HiredAt = now;
        await db.SaveChangesAsync(ct);
        return null;
    }
}
