using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Infrastructure;

namespace Pes17League.Clubs.Application;

/// <summary>Trạng thái career của người chơi cho khung giao diện (UI đợt 1, PO 19/11): đầu trang, menu mở dần, thẻ mở khoá, màn kết quả.</summary>
public sealed record CareerProgress(
    Guid ParticipantId, Guid ClubId, string ClubName, string? ClubLogo,
    Guid SeasonId, string SeasonName, bool Finished, int Slot, int Slots, int MyPlayed, long Budget,
    CareerState State, IReadOnlySet<string> Unlocked, IReadOnlySet<string> Seen, int MarketSlotsLeft, int? NextMarketSlot, Guid? PendingResult)
{
    public bool Has(string feature) => Unlocked.Contains(feature);

    /// <summary>Đã mở mà người chơi chưa vào lần nào → nhãn MỚI.</summary>
    public bool IsNew(string feature) => Has(feature) && CareerUnlocks.Introduced.Contains(feature) && !Seen.Contains("visit:" + feature);

    /// <summary>Thẻ "Vừa mở khoá" còn chờ hiện, theo thứ tự.</summary>
    public IReadOnlyList<string> PendingIntros => [.. CareerUnlocks.Introduced.Where(f => Has(f) && !Seen.Contains("intro:" + f))];
}

/// <param name="Home">true = bàn của đội nhà.</param>
public sealed record CareerScorer(string Name, int Minute, bool Home);

/// <summary>Kết quả trận của người chơi cho màn "Hết giờ".</summary>
public sealed record CareerMatchResult(
    Guid FixtureId, string Competition, FixtureStage Stage, int? Leg, string Home, string? HomeLogo, string Away, string? AwayLogo, bool MineHome,
    int HomeGoals, int AwayGoals, int? HomePens, int? AwayPens, char Verdict, IReadOnlyList<CareerScorer> Scorers,
    Guid? BestId, string? BestName, decimal? BestRating, long? Prize, int? RankBefore, int? RankAfter, int Slot);

public sealed class CareerProgressService(ClubsDbContext db, TimeProvider clock)
{
    /// <summary>null = người chơi không cầm CLB nào trong career.</summary>
    public async Task<CareerProgress?> LoadAsync(Guid participantId, CancellationToken ct = default)
    {
        var mine = await (from a in db.TeamAssignments join c in db.Clubs on a.ClubId equals c.Id
                          where a.ParticipantId == participantId && a.To == null && a.ControllerType == ControllerType.Human
                          select new { c.Id, c.Name, c.LogoPath }).AsNoTracking().FirstOrDefaultAsync(ct);
        if (mine is null) return null;
        var season = await db.Seasons.AsNoTracking().Where(s => s.Status == SeasonStatus.Active).OrderBy(s => s.Number).FirstOrDefaultAsync(ct)
            ?? await db.Seasons.AsNoTracking().Where(s => s.Status == SeasonStatus.Finished).OrderByDescending(s => s.Number).FirstOrDefaultAsync(ct);
        if (season is null) return null;

        var finished = season.Status == SeasonStatus.Finished;
        var slots = await db.Matchdays.CountAsync(m => m.SeasonId == season.Id, ct);
        var open = finished ? null : await db.Matchdays.AsNoTracking().Where(m => m.SeasonId == season.Id
                && (m.Status == MatchdayStatus.WindowOpen || m.Status == MatchdayStatus.Locked || m.Status == MatchdayStatus.Playing))
            .OrderBy(m => m.Number).FirstOrDefaultAsync(ct);
        var slot = open?.Number ?? 0;
        var played = await (from f in db.Fixtures join m in db.Matchdays on f.MatchdayId equals m.Id
                            where m.SeasonId == season.Id && (f.HomeClubId == mine.Id || f.AwayClubId == mine.Id)
                                  && f.Status == FixtureStatus.Played && f.HomeGoals != null
                            orderby f.PlayedAt descending, m.Number descending
                            select f.Id).ToListAsync(ct);
        var budget = await db.Ledger.Where(e => e.ClubId == mine.Id).SumAsync(e => e.Amount, ct);
        var window = await new FreeMarketService(db, clock).WindowAsync(season.Id, ct);
        var marketOpen = window is not null && window.IsOpenAt(clock.GetUtcNow());
        var windows = db.TransferWindows.Where(w => w.SeasonId == season.Id).Select(w => w.Id);
        var hadOffer = await db.TransferOffers.AnyAsync(o => windows.Contains(o.TransferWindowId) && (o.SellerClubId == mine.Id || o.BuyerClubId == mine.Id), ct);
        var expiring = await db.Contracts.AnyAsync(k => k.ClubId == mine.Id && k.Status == ContractStatus.Active && k.MatchesTotal != null
            && k.MatchesTotal - k.MatchesPlayed <= CareerUnlocks.ContractWarn, ct);

        var state = new CareerState(true, finished, slot, slots, played.Count, marketOpen, hadOffer, expiring);
        var seen = (await db.CareerSeen.Where(s => s.ParticipantId == participantId).Select(s => s.Key).ToListAsync(ct)).ToHashSet();
        var last = played.Count > 0 ? played[0] : (Guid?)null;
        return new(participantId, mine.Id, mine.Name, mine.LogoPath, season.Id, season.Name, finished, slot, slots, played.Count, budget,
            state, CareerUnlocks.Unlocked(state), seen,
            marketOpen ? CareerUnlocks.MarketSlotsLeft(Math.Max(slot, 1), slots) : 0,
            finished ? null : CareerUnlocks.NextMarketSlot(Math.Max(slot, 1), slots),
            last is { } r && !seen.Contains("result:" + r) ? r : null);
    }

    public async Task MarkAsync(Guid participantId, params string[] keys)
    {
        var have = await db.CareerSeen.Where(s => s.ParticipantId == participantId && keys.Contains(s.Key)).Select(s => s.Key).ToListAsync();
        var now = clock.GetUtcNow();
        foreach (var k in keys.Distinct().Except(have)) db.CareerSeen.Add(new CareerSeen { ParticipantId = participantId, Key = k, At = now });
        if (db.ChangeTracker.HasChanges())
        {
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateException) { /* hai tab cùng ghi một khoá: đã có là được */ }
        }
    }

    public async Task<CareerMatchResult?> ResultAsync(Guid fixtureId, Guid clubId, CancellationToken ct = default)
    {
        var x = await (from fx in db.Fixtures join m in db.Matchdays on fx.MatchdayId equals m.Id
                       join h in db.Clubs on fx.HomeClubId equals h.Id join a in db.Clubs on fx.AwayClubId equals a.Id
                       where fx.Id == fixtureId
                       select new { F = fx, m.Number, m.SeasonId, Home = h.Name, HomeLogo = h.LogoPath, Away = a.Name, AwayLogo = a.LogoPath }).AsNoTracking().SingleOrDefaultAsync(ct);
        if (x?.F.HomeGoals is not { } hg || x.F.AwayGoals is not { } ag) return null;
        var f = x.F;
        var comp = f.CompetitionEditionId is { } eid
            ? await (from e in db.Editions join c in db.Competitions on e.CompetitionId equals c.Id where e.Id == eid select c.Name).SingleOrDefaultAsync(ct) ?? ""
            : "";
        var home = f.HomeClubId == clubId;
        var verdict = f.WinnerClubId() is { } w ? (w == clubId ? 'W' : 'L') : hg == ag ? 'D' : (hg > ag) == home ? 'W' : 'L';

        var scorers = await (from g in db.Goals join p in db.Players on g.ScorerPlayerId equals p.Id
                             where g.FixtureId == fixtureId orderby g.Half, g.Minute
                             select new CareerScorer(p.Name, g.Minute, g.ClubId == f.HomeClubId)).AsNoTracking().ToListAsync(ct);
        var best = await (from r in db.MatchRatings join p in db.Players on r.PlayerId equals p.Id
                          where r.FixtureId == fixtureId && r.ClubId == clubId && r.Rating != null
                          orderby r.Rating descending, r.Goals descending, r.Slot
                          select new { p.Id, p.Name, r.Rating }).AsNoTracking().FirstOrDefaultAsync(ct);

        var settlement = await db.RoundSettlements.Where(s => s.MatchdayId == f.MatchdayId).Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct);
        long? prize = settlement is { } sid
            ? await db.Ledger.Where(e => e.ClubId == clubId && e.RefId == sid && e.Reason == LedgerReason.Prize).SumAsync(e => e.Amount, ct)
            : null;

        int? before = null, after = null;
        if (f.Stage == FixtureStage.League && f.CompetitionEditionId is { } edition)
        {
            var clubs = await db.EditionClubs.Where(e => e.EditionId == edition).Select(e => e.ClubId).ToListAsync(ct);
            var all = await db.Fixtures.Where(o => o.CompetitionEditionId == edition && o.Status == FixtureStatus.Played && o.Stage == FixtureStage.League
                    && o.HomeGoals != null && o.AwayGoals != null)
                .Select(o => new { o.Id, P = new PlayedFixture<Guid>(o.HomeClubId, o.AwayClubId, o.HomeGoals!.Value, o.AwayGoals!.Value) }).ToListAsync(ct);
            int Rank(IEnumerable<PlayedFixture<Guid>> ps) =>
                Standings.Compute(clubs, ps.ToList(), id => id.ToString()).Select((r, i) => (r.Club, i)).First(t => t.Club == clubId).i + 1;
            if (clubs.Contains(clubId))
            {
                after = Rank(all.Select(o => o.P));
                before = all.Count > 1 ? Rank(all.Where(o => o.Id != fixtureId).Select(o => o.P)) : null;
            }
        }
        return new(f.Id, comp, f.Stage, f.Leg, x.Home, x.HomeLogo, x.Away, x.AwayLogo, home,
            hg, ag, f.HomePenalties, f.AwayPenalties, verdict, scorers, best?.Id, best?.Name, best?.Rating, prize, before, after, x.Number);
    }
}
