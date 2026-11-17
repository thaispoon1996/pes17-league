using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Application;
using Pes17League.Clubs.Infrastructure;
using Pes17League.OptionFile;

namespace Pes17League.Web;

/// <param name="Pinned">Mùa chọn qua <c>?mua=N</c> (xem lại mùa cũ): link trong trang giữ <c>mua=N</c>.</param>
public sealed record SeasonInfo(Guid Id, string Name, Guid HumanClubId, IReadOnlyDictionary<Guid, string> Clubs, SeasonEconomy Economy, IReadOnlyDictionary<Guid, string> Logos,
    int Number = 0, SeasonStatus Status = SeasonStatus.Active, bool Pinned = false, IReadOnlySet<Guid>? HumanClubs = null)
{
    /// <summary>Trận chờ HLV người tự đá (đúng 1 bên Human, chưa giao trợ lý); còn lại máy đá (đợt 2) — như <see cref="MatchOrchestrator.NeedsHuman"/>.</summary>
    public bool NeedsHuman(FixtureView f) => HumanClubs is { } h && !f.AssistantPlays && h.Contains(f.Home) != h.Contains(f.Away);
    public bool IsHuman(Guid club) => HumanClubs?.Contains(club) == true;

    /// <summary>Logo CLB (lấy từ game hoặc admin tải lên); null → huy hiệu chữ.</summary>
    public string? Logo(Guid club) => Logos.GetValueOrDefault(club);

    /// <summary>Link trang chỉ đọc trong mùa đang xem: "lich?khung=3" → "lich?khung=3&amp;mua=1" khi <see cref="Pinned"/>.</summary>
    public string Url(string path) => Pinned ? $"{path}{(path.Contains('?') ? '&' : '?')}mua={Number}" : path;

    /// <summary>Nhãn mùa ở đầu trang: "Alpha 1 · đã kết thúc".</summary>
    public string Label => Status == SeasonStatus.Finished ? $"{Name} · đã kết thúc" : Name;
}
/// <param name="EditionId">Mùa giải của trận; null = mùa alpha 1 giải.</param>
public sealed record FixtureView(Guid Id, int Matchday, Guid Home, Guid Away, FixtureStatus Status, int? HomeGoals, int? AwayGoals,
    IReadOnlyList<GoalView> Goals, int? HomePenalties = null, int? AwayPenalties = null, Guid? EditionId = null, FixtureStage Stage = FixtureStage.League, int? Leg = null,
    bool AssistantPlays = false);
/// <summary>Giải của mùa liên đoàn (mùa giải có CLB); <see cref="Rules"/> = luật đã khoá của mùa giải.</summary>
public sealed record EditionView(Guid Id, string Name, CompetitionType Type, Guid CompetitionId, string? Logo, string? Country, int? Tier, EditionStatus Status, CompetitionRules Rules);
/// <summary>Tóm tắt 1 giải (bước 7): đầu bảng, số trận đã đá/tổng của đội đá nhiều nhất (cúp: league phase), giai đoạn cúp đang đá; <see cref="Champion"/> khi giải đã xong.</summary>
public sealed record CompetitionSummary(EditionView Edition, StandingRow<Guid>? Leader, int Played, int Total, FixtureStage? Stage, Guid? Champion);
/// <summary>Vùng tô màu BXH (PO chọn 06/11, bước 7): hạng <see cref="From"/>..<see cref="To"/>, lớp màu z-*.</summary>
public sealed record Zone(int From, int To, string Css, string Label);
/// <summary>Bàn thắng để hiển thị; Scorer null = không rõ người ghi (phản lưới, nhập tay, trận trước 04/11).</summary>
public sealed record GoalView(Guid ClubId, int Half, int Minute, Guid? ScorerId, string? Scorer);
/// <summary>Trang trận <c>/tran/{id}</c> (B3): <see cref="Home"/>/<see cref="Away"/> null = chưa có thống kê (trận đá trước 07/11 hoặc đọc lỗi).</summary>
/// <param name="Edition">Giải của trận; null = mùa alpha 1 giải.</param>
public sealed record MatchPage(SeasonInfo Season, FixtureView Fixture, DateTimeOffset? PlayedAt, EditionView? Edition, string Round,
    MatchTeamStats? Home, MatchTeamStats? Away, IReadOnlyList<MatchEventView> Events, IReadOnlyList<MatchPlayerView> Players);
/// <param name="Player">Thay người: người vào; <paramref name="Other"/> = người ra.</param>
public sealed record MatchEventView(Guid ClubId, int Half, int Minute, bool AddedTime, MatchEventType Type, string? Player, string? Other, bool? OnTarget);
/// <summary>1 cầu thủ đã ra sân: điểm + thống kê cá nhân (cột null = trận cũ chưa có); <see cref="Position"/> = vị trí đăng ký.</summary>
public sealed record MatchPlayerView(string Name, MatchRating Stats, Position Position = Position.CF);
/// <summary>Thẻ tin trên trang chủ và trang Tin tức. <see cref="Image"/> = đường dẫn tương đối trong thư mục ảnh, null = chưa có ảnh.</summary>
public sealed record NewsCard(Guid Id, Guid FixtureId, string Title, string? Image, string Home, string Away, int HomeGoals, int AwayGoals,
    int? HomePenalties, int? AwayPenalties, string? Competition, Guid? CompetitionId, int Round, DateTimeOffset PublishedAt);
public sealed record NewsPhoto(string File, string Caption);
public sealed record NewsPost(NewsCard Card, SeasonInfo Season, string Body, bool ByAi, IReadOnlyList<NewsPhoto> Photos);
public sealed record NewsComp(Guid Id, string Name);
public sealed record SquadRow(Guid PlayerId, string Name, Position Position, int Slot, int Shirt, int Overall, long Price,
    int? MatchesTotal, int MatchesPlayed, long? Wage, RenewalStatus? Renewal);
/// <param name="BasePrice">Giá gốc đầu mùa (so biến động giá).</param><param name="Age">Tuổi theo chỉ số AGE mùa này.</param>
public sealed record MarketRow(Guid PlayerId, string Name, Position Position, int? BirthYear, Guid? ClubId, int Overall, long Price, long BasePrice, int? Age);
public sealed record VisibilityRow(Guid PlayerId, int PesPlayerId, string Name, Position Position, Guid? ClubId, int Overall, long Price, bool? Override, bool Visible);

/// <summary>Truy vấn đọc cho các trang (mỗi lệnh 1 DbContext).</summary>
public sealed partial class LeagueQueries(IDbContextFactory<ClubsDbContext> dbs, CurrentUser? me = null)
{
    public async Task<SeasonInfo?> SeasonAsync()
    {
        await using var db = await dbs.CreateDbContextAsync();
        var s = await db.Seasons.Where(x => x.Status == SeasonStatus.Active).SingleOrDefaultAsync();
        return s is null ? null : await InfoAsync(db, me is null ? null : await me.ManagerIdAsync(), s, pinned: false);
    }

    /// <summary>Mùa cho các trang chỉ đọc (Giải đấu, Lịch, BXH, Các mùa): <paramref name="number"/> = <c>?mua=N</c> (xem lại mùa cũ);
    /// không chọn → mùa đang chạy, chưa có thì mùa đã xong gần nhất (hết mùa vẫn xem được BXH/vô địch). Mùa chưa bắt đầu không xem ở đây.</summary>
    public async Task<SeasonInfo?> ViewSeasonAsync(int? number)
    {
        await using var db = await dbs.CreateDbContextAsync();
        var q = db.Seasons.Where(x => x.Status != SeasonStatus.Planned);
        var s = number is { } n
            ? await q.Where(x => x.Number == n).OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync()
            : await q.OrderBy(x => x.Status == SeasonStatus.Active ? 0 : 1).ThenByDescending(x => x.CreatedAt).FirstOrDefaultAsync();
        return s is null ? null : await InfoAsync(db, me is null ? null : await me.ManagerIdAsync(), s, pinned: number is not null);
    }

    /// <summary>Số mùa (đang chạy + đã xong), mới nhất trước — trang Các mùa.</summary>
    public async Task<List<int>> SeasonNumbersAsync()
    {
        await using var db = await dbs.CreateDbContextAsync();
        return await db.Seasons.Where(s => s.Status != SeasonStatus.Planned).OrderByDescending(s => s.CreatedAt).Select(s => s.Number).ToListAsync();
    }

    private static async Task<SeasonInfo> InfoAsync(ClubsDbContext db, Guid? manager, Season s, bool pinned)
    {
        var clubIds = db.SeasonClubs.Where(x => x.SeasonId == s.Id).Select(x => x.ClubId);
        var clubs = await db.Clubs.Where(c => clubIds.Contains(c.Id)).Select(c => new { c.Id, c.Name, c.LogoPath }).ToListAsync();
        var allHumans = db.TeamAssignments.Where(a => clubIds.Contains(a.ClubId) && a.ControllerType == ControllerType.Human);
        // CLB "của tôi" = CLB người đang xem cầm (đợt 2: nhiều HLV; admin = phân công ParticipantId null)
        var humans = allHumans.Where(a => a.ParticipantId == manager);
        var humanClubs = s.Status == SeasonStatus.Active ? (await allHumans.Where(a => a.To == null).Select(a => a.ClubId).ToListAsync()).ToHashSet() : [];
        Guid human;
        if (s.Status == SeasonStatus.Active) human = await humans.Where(a => a.To == null).Select(a => a.ClubId).FirstOrDefaultAsync();
        else
        {
            // Mùa đã xong: CLB PO cầm lúc đá trận cuối của mùa (phân công có thể đã đóng lúc chốt mùa, hoặc PO đang cầm CLB khác ở mùa sau);
            // mùa chưa đá trận nào → phân công còn mở, không có thì phân công đóng gần nhất.
            var last = await (from a in db.MatchAttempts join f in db.Fixtures on a.FixtureId equals f.Id join m in db.Matchdays on f.MatchdayId equals m.Id
                              where m.SeasonId == s.Id select (DateTimeOffset?)a.StartedAt).MaxAsync();
            human = last is { } t
                ? await humans.Where(a => a.From <= t && (a.To == null || a.To >= t)).OrderByDescending(a => a.From).Select(a => a.ClubId).FirstOrDefaultAsync()
                : await humans.OrderBy(a => a.To != null).ThenByDescending(a => a.To).Select(a => a.ClubId).FirstOrDefaultAsync();
        }
        var eco = await db.SeasonEconomies.FindAsync(s.Id) ?? new SeasonEconomy { SeasonId = s.Id };
        return new(s.Id, s.Name, human, clubs.ToDictionary(c => c.Id, c => c.Name), eco, clubs.Where(c => c.LogoPath != null).ToDictionary(c => c.Id, c => c.LogoPath!),
            s.Number, s.Status, pinned, humanClubs);
    }

    /// <summary>Số trận đã đá / đã lên lịch của mùa (trận loại trực tiếp chỉ có sau khi bốc).</summary>
    public async Task<(int Played, int Total)> FixtureCountAsync(Guid seasonId)
    {
        await using var db = await dbs.CreateDbContextAsync();
        var q = from f in db.Fixtures join m in db.Matchdays on f.MatchdayId equals m.Id where m.SeasonId == seasonId select f.Status;
        return (await q.CountAsync(x => x == FixtureStatus.Played), await q.CountAsync());
    }

    /// <summary>Vòng đang diễn ra: vòng chưa chốt nhỏ nhất (mở kỳ CN / khoá / đang đá / đá xong chờ chốt).</summary>
    public async Task<Matchday?> CurrentMatchdayAsync(Guid seasonId)
    {
        await using var db = await dbs.CreateDbContextAsync();
        return await db.Matchdays.Where(m => m.SeasonId == seasonId && m.Status != MatchdayStatus.Settled && m.Status != MatchdayStatus.Planned)
            .OrderBy(m => m.Number).FirstOrDefaultAsync();
    }

    public async Task<TransferWindow?> OpenWindowAsync(Guid seasonId)
    {
        await using var db = await dbs.CreateDbContextAsync();
        return await db.TransferWindows.Where(w => w.SeasonId == seasonId && w.Status == TransferWindowStatus.Open).FirstOrDefaultAsync();
    }

    /// <summary>Mọi khung ngày của mùa theo số.</summary>
    public async Task<List<Matchday>> MatchdaysAsync(Guid seasonId)
    {
        await using var db = await dbs.CreateDbContextAsync();
        return await db.Matchdays.AsNoTracking().Where(m => m.SeasonId == seasonId).OrderBy(m => m.Number).ToListAsync();
    }

    public async Task<List<FixtureView>> FixturesAsync(Guid seasonId, Guid? matchdayId = null, Guid? editionId = null)
    {
        await using var db = await dbs.CreateDbContextAsync();
        var rows = await (from f in db.Fixtures
                          join m in db.Matchdays on f.MatchdayId equals m.Id
                          where m.SeasonId == seasonId && (matchdayId == null || m.Id == matchdayId) && (editionId == null || f.CompetitionEditionId == editionId)
                          orderby m.Number
                          select new { f, m.Number }).ToListAsync();
        var ids = rows.Select(r => r.f.Id).ToList();
        var goals = (await (from g in db.Goals.Where(g => ids.Contains(g.FixtureId))
                            join p in db.Players on g.ScorerPlayerId equals p.Id into ps
                            from p in ps.DefaultIfEmpty()
                            select new { g.FixtureId, View = new GoalView(g.ClubId, g.Half, g.Minute, g.ScorerPlayerId, p == null ? null : p.Name) }).ToListAsync())
            .OrderBy(g => g.View.Half).ThenBy(g => g.View.Minute).ToLookup(g => g.FixtureId, g => g.View);
        return [.. rows.Select(r => new FixtureView(r.f.Id, r.Number, r.f.HomeClubId, r.f.AwayClubId, r.f.Status, r.f.HomeGoals, r.f.AwayGoals, [.. goals[r.f.Id]],
            r.f.HomePenalties, r.f.AwayPenalties, r.f.CompetitionEditionId, r.f.Stage, r.f.Leg, r.f.AssistantPlays))];
    }

    /// <summary>Trang trận: mùa của trận (mùa cũ → link giữ <c>mua=N</c>), giải, tên vòng, thống kê 2 đội, diễn biến, cầu thủ theo ô; null = không có trận.</summary>
    public async Task<MatchPage?> MatchAsync(Guid fixtureId)
    {
        await using var db = await dbs.CreateDbContextAsync();
        var row = await (from f in db.Fixtures join m in db.Matchdays on f.MatchdayId equals m.Id join s in db.Seasons on m.SeasonId equals s.Id
                         where f.Id == fixtureId select new { f.CompetitionEditionId, f.PlayedAt, s }).SingleOrDefaultAsync();
        if (row is null) return null;
        var season = await InfoAsync(db, me is null ? null : await me.ManagerIdAsync(), row.s, pinned: row.s.Status != SeasonStatus.Active);
        var own = await FixturesAsync(row.s.Id, editionId: row.CompetitionEditionId);
        var fixture = own.Single(x => x.Id == fixtureId);
        var edition = row.CompetitionEditionId is { } eid ? (await EditionsAsync(row.s.Id)).SingleOrDefault(e => e.Id == eid) : null;
        var teams = await db.MatchTeamStats.AsNoTracking().Where(t => t.FixtureId == fixtureId).ToListAsync();
        var ratings = await db.MatchRatings.AsNoTracking().Where(r => r.FixtureId == fixtureId).OrderBy(r => r.Slot).ToListAsync();
        var events = await db.MatchEvents.AsNoTracking().Where(e => e.FixtureId == fixtureId).OrderBy(e => e.Seq).ToListAsync();
        var ids = ratings.Select(r => r.PlayerId).Concat(events.SelectMany(e => new[] { e.PlayerId, e.OtherPlayerId }).OfType<Guid>()).Distinct().ToList();
        var players = await db.Players.Where(p => ids.Contains(p.Id)).Select(p => new { p.Id, p.Name, p.Position }).ToDictionaryAsync(p => p.Id);
        string? Name(Guid? id) => id is { } x ? players.GetValueOrDefault(x)?.Name : null;
        return new(season, fixture, row.PlayedAt, edition, Fmt.Round(own, fixture.Matchday), teams.SingleOrDefault(t => t.IsHome), teams.SingleOrDefault(t => !t.IsHome),
            [.. events.Select(e => new MatchEventView(e.ClubId, e.Half, e.Minute, e.AddedTime, e.Type, Name(e.PlayerId), Name(e.OtherPlayerId), e.OnTarget))],
            [.. ratings.Select(r => new MatchPlayerView(Name(r.PlayerId) ?? "?", r, players.GetValueOrDefault(r.PlayerId)?.Position ?? Position.CF))]);
    }

    public async Task<IReadOnlyList<StandingRow<Guid>>> StandingsAsync(Guid seasonId, IOptionFileStore store, TimeProvider clock)
    {
        await using var db = await dbs.CreateDbContextAsync();
        return await new CompetitionService(db, store, clock).GetStandingsAsync(seasonId);
    }

    /// <summary>Các giải của mùa như trang Lịch mùa: quốc nội (nước, hạng) rồi cúp (hạng); rỗng = mùa alpha 1 giải.</summary>
    public async Task<List<EditionView>> EditionsAsync(Guid seasonId)
    {
        await using var db = await dbs.CreateDbContextAsync();
        var rows = await (from e in db.Editions
                          join c in db.Competitions on e.CompetitionId equals c.Id
                          where e.SeasonId == seasonId && db.EditionClubs.Any(x => x.EditionId == e.Id)
                          select new { e.Id, c.Name, c.Type, c.Country, c.Tier, CompetitionId = c.Id, c.LogoPath, e.Status, e.RulesSnapshot, c.Rules }).ToListAsync();
        return [.. rows.OrderBy(r => r.Type).ThenBy(r => r.Country, StringComparer.Ordinal).ThenBy(r => r.Tier).ThenBy(r => r.Name, StringComparer.Ordinal)
            .Select(r => new EditionView(r.Id, r.Name, r.Type, r.CompetitionId, r.LogoPath, r.Country, r.Tier, r.Status, r.RulesSnapshot ?? r.Rules))];
    }

    /// <summary>Tóm tắt các giải (trang Các giải, cột phải trang chủ).</summary>
    public async Task<List<CompetitionSummary>> SummariesAsync(Guid seasonId, IReadOnlyList<EditionView> editions, IOptionFileStore store, TimeProvider clock)
    {
        await using var db = await dbs.CreateDbContextAsync();
        var tables = await new CompetitionService(db, store, clock).GetSeasonEditionStandingsAsync(seasonId);
        var ids = editions.Select(e => e.Id).ToList();
        var fixtures = (await db.Fixtures.AsNoTracking().Where(f => f.CompetitionEditionId != null && ids.Contains(f.CompetitionEditionId.Value)).ToListAsync())
            .ToLookup(f => f.CompetitionEditionId!.Value);
        return [.. editions.Select(e =>
        {
            var rows = tables.GetValueOrDefault(e.Id) ?? [];
            var own = fixtures[e.Id].ToList();
            var cup = e.Type is CompetitionType.ContinentalCup or CompetitionType.DomesticCup;
            // Cúp: giai đoạn sớm nhất còn trận chưa đá (đá hết → giai đoạn cuối).
            FixtureStage? stage = cup ? own.Where(f => f.Status != FixtureStatus.Played).Min(f => (FixtureStage?)f.Stage) ?? own.Max(f => (FixtureStage?)f.Stage) ?? FixtureStage.LeaguePhase : null;
            // Giải bị chốt khi chưa đá hết (mùa dừng giữa chừng) → không có vô địch.
            Guid? champion = e.Status != EditionStatus.Finished ? null : cup ? own.SingleOrDefault(f => f.Stage == FixtureStage.Final)?.WinnerClubId()
                : own.All(f => f.Status == FixtureStatus.Played) ? rows.FirstOrDefault()?.Club : null;
            var total = e.Type switch
            {
                CompetitionType.ContinentalCup => e.Rules.LeaguePhaseMatches ?? 0,
                CompetitionType.DomesticCup => Math.Max(0, rows.Count - 1),
                _ => (rows.Count - 1) * e.Rules.Legs,
            };
            return new CompetitionSummary(e, rows.FirstOrDefault(), rows.Count == 0 ? 0 : rows.Max(r => r.Played), total, stage, champion);
        })];
    }

    public async Task<IReadOnlyList<BracketRound>?> BracketAsync(Guid editionId, IOptionFileStore store, TimeProvider clock)
    {
        await using var db = await dbs.CreateDbContextAsync();
        return await new CompetitionService(db, store, clock).GetBracketAsync(editionId);
    }

    public async Task<IReadOnlyList<StandingRow<Guid>>> EditionStandingsAsync(Guid editionId, IOptionFileStore store, TimeProvider clock)
    {
        await using var db = await dbs.CreateDbContextAsync();
        return await new CompetitionService(db, store, clock).GetEditionStandingsAsync(editionId);
    }

    public async Task<List<SquadRow>> SquadAsync(Guid seasonId, Guid clubId)
    {
        await using var db = await dbs.CreateDbContextAsync();
        var rows = await (from m in db.SquadMemberships
                          join p in db.Players on m.PlayerId equals p.Id
                          join pr in db.PlayerPrices.Where(x => x.SeasonId == seasonId) on p.Id equals pr.PlayerId into prs
                          from pr in prs.DefaultIfEmpty()
                          join c in db.Contracts.Where(x => x.Status == ContractStatus.Active) on p.Id equals c.PlayerId into cs
                          from c in cs.DefaultIfEmpty()
                          where m.ClubId == clubId && m.LeftAt == null
                          orderby m.SlotOrder
                          select new { p.Id, p.Name, p.Position, m.SlotOrder, m.ShirtNumber, Overall = pr == null ? 0 : pr.Overall, Price = pr == null ? 0 : pr.Price,
                                       Total = c == null ? null : c.MatchesTotal, Played = c == null ? 0 : c.MatchesPlayed, Wage = c == null ? null : c.Wage }).ToListAsync();
        var ids = rows.Select(r => r.Id).ToList();
        var renewals = (await db.ContractRenewals.Where(r => ids.Contains(r.PlayerId) && r.SeasonId == seasonId).ToListAsync())
            .GroupBy(r => r.PlayerId).ToDictionary(g => g.Key, g => g.MaxBy(r => r.RequestedAt)!.Status);
        return [.. rows.Select(r => new SquadRow(r.Id, r.Name, r.Position, r.SlotOrder, r.ShirtNumber, r.Overall, r.Price, r.Total, r.Played, r.Wage,
            renewals.TryGetValue(r.Id, out var st) ? st : null))];
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(string, int), FormationLayout> PatchLayouts = new();

    /// <summary>Bố cục đội hình gốc của CLB trong OF patch mùa này (chiến thuật "Theo patch"); null = CLB không có trong patch.</summary>
    public async Task<FormationLayout?> PatchLayoutAsync(Guid seasonId, Guid clubId, IOptionFileStore store)
    {
        await using var db = await dbs.CreateDbContextAsync();
        var patchId = await db.Seasons.Where(s => s.Id == seasonId).Select(s => s.PatchVersionId).SingleAsync();
        var path = await db.PatchVersions.Where(p => p.Id == patchId).Select(p => p.BaseOptionFilePath).SingleAsync();
        var index = await db.ClubPesMappings.Where(m => m.PatchVersionId == patchId && m.ClubId == clubId).Select(m => (int?)m.PesClubIndex).SingleOrDefaultAsync();
        if (index is not { } i) return null;
        if (PatchLayouts.TryGetValue((path, i), out var hit)) return hit;
        return PatchLayouts[(path, i)] = OptionFileCodec.Decode(await store.ReadAsync(path)).GetFormationLayout(i);
    }

    public async Task<(long Balance, long Payroll, List<ClubLedgerEntry> Ledger)> FinanceAsync(Guid clubId)
    {
        await using var db = await dbs.CreateDbContextAsync();
        var t = new TransferService(db, TimeProvider.System);
        return (await t.BalanceAsync(clubId), await t.PayrollAsync(clubId),
            await db.Ledger.Where(e => e.ClubId == clubId).OrderByDescending(e => e.At).Take(100).ToListAsync());
    }

    /// <summary>Thị trường: mọi cầu thủ có giá mùa này, trừ CLB của PO và người đang ẩn (MarketVisibility/luật dải ẩn); ClubId null = tự do.</summary>
    public async Task<List<MarketRow>> MarketAsync(Guid seasonId, Guid excludeClubId)
    {
        var rows = await VisibilityAsync(seasonId);
        await using var db = await dbs.CreateDbContextAsync();
        var basePrice = await db.PlayerPrices.Where(p => p.SeasonId == seasonId).ToDictionaryAsync(p => p.PlayerId, p => p.BasePrice);
        // Values là jsonb có converter (không dịch LINQ được) → lấy riêng khoá AGE bằng SQL thay vì tải cả snapshot ~5.500 cầu thủ.
        var age = (await db.Database.SqlQuery<AgeRow>($"""select "PlayerId", ("Values"->>'AGE')::int as "Age" from clubs."PlayerAttributes" where "SeasonId" = {seasonId}""")
            .ToListAsync()).ToDictionary(a => a.PlayerId, a => a.Age);
        return [.. rows.Where(r => r.Visible && r.ClubId != excludeClubId)
            .Select(r => new MarketRow(r.PlayerId, r.Name, r.Position, r.BirthYear, r.ClubId, r.Overall, r.Price, basePrice.GetValueOrDefault(r.PlayerId), age.GetValueOrDefault(r.PlayerId)))];
    }

    private sealed class AgeRow { public Guid PlayerId { get; set; } public int? Age { get; set; } }

    /// <summary>Mọi cầu thủ có giá mùa này kèm trạng thái ẩn/hiện (trang admin).</summary>
    public async Task<List<VisibilityRow>> VisibilityRowsAsync(Guid seasonId) =>
        [.. (await VisibilityAsync(seasonId)).Select(r => new VisibilityRow(r.PlayerId, r.PesPlayerId, r.Name, r.Position, r.ClubId, r.Overall, r.Price, r.Override, r.Visible))];

    private async Task<List<(Guid PlayerId, int PesPlayerId, string Name, Position Position, int? BirthYear, Guid? ClubId, int Overall, long Price, bool? Override, bool Visible)>>
        VisibilityAsync(Guid seasonId)
    {
        await using var db = await dbs.CreateDbContextAsync();
        var patch = await db.Seasons.Where(s => s.Id == seasonId).Select(s => s.PatchVersionId).SingleAsync();
        var rows = await (from pr in db.PlayerPrices
                          join p in db.Players on pr.PlayerId equals p.Id
                          join pm in db.PlayerPesMappings.Where(x => x.PatchVersionId == patch) on p.Id equals pm.PlayerId
                          join m in db.SquadMemberships.Where(x => x.LeftAt == null) on p.Id equals m.PlayerId into ms
                          from m in ms.DefaultIfEmpty()
                          where pr.SeasonId == seasonId
                          select new { p.Id, pm.PesPlayerId, p.Name, p.Position, p.BirthYear, ClubId = m == null ? (Guid?)null : m.ClubId, pr.Overall, pr.Price, p.MarketVisibility })
                          .ToListAsync();
        return [.. rows.Select(r => (r.Id, r.PesPlayerId, r.Name, r.Position, r.BirthYear, r.ClubId, r.Overall, r.Price, r.MarketVisibility,
            MarketVisibilityService.IsVisible(r.MarketVisibility, r.PesPlayerId, r.ClubId is not null)))];
    }

    /// <summary>Tin của mùa, mới nhất trước. <paramref name="round"/> = số vòng; <paramref name="competitionId"/> = giải.</summary>
    public async Task<List<NewsCard>> NewsAsync(Guid seasonId, int? round, Guid? competitionId, int take = 24)
    {
        await using var db = await dbs.CreateDbContextAsync();
        var editions = await (from e in db.Editions
                              join c in db.Competitions on e.CompetitionId equals c.Id
                              where e.SeasonId == seasonId
                              select new { e.Id, c.Name, CompId = c.Id }).ToListAsync();
        var rows = await (from a in db.MatchArticles.AsNoTracking()
                          join f in db.Fixtures on a.FixtureId equals f.Id
                          join m in db.Matchdays on f.MatchdayId equals m.Id
                          where m.SeasonId == seasonId && (round == null || m.Number == round)
                          orderby a.PublishedAt descending
                          select new
                          {
                              a.Id, a.FixtureId, a.Title, a.PublishedAt, f.HomeClubId, f.AwayClubId, f.HomeGoals, f.AwayGoals,
                              f.HomePenalties, f.AwayPenalties, f.CompetitionEditionId, Round = m.Number,
                          }).ToListAsync();
        if (competitionId is { } cid)
        {
            var eids = editions.Where(e => e.CompId == cid).Select(e => (Guid?)e.Id).ToHashSet();
            rows = rows.Where(r => eids.Contains(r.CompetitionEditionId)).ToList();
        }
        rows = rows.Take(take).ToList();
        var clubIds = rows.SelectMany(r => new[] { r.HomeClubId, r.AwayClubId }).Distinct().ToList();
        var clubs = await db.Clubs.Where(c => clubIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name);
        var articleIds = rows.Select(r => r.Id).ToList();
        var photos = await db.MatchPhotos.AsNoTracking().Where(p => articleIds.Contains(p.ArticleId)).OrderBy(p => p.Sort).ToListAsync();
        var image = photos.GroupBy(p => p.ArticleId).ToDictionary(g => g.Key, g => g.First().FileName);
        return rows.Select(r =>
        {
            var ed = editions.FirstOrDefault(e => r.CompetitionEditionId == e.Id);
            return new NewsCard(r.Id, r.FixtureId, r.Title, image.GetValueOrDefault(r.Id), clubs[r.HomeClubId], clubs[r.AwayClubId],
                r.HomeGoals ?? 0, r.AwayGoals ?? 0, r.HomePenalties, r.AwayPenalties, ed?.Name, ed?.CompId, r.Round, r.PublishedAt);
        }).ToList();
    }

    public async Task<(List<int> Rounds, List<NewsComp> Comps)> NewsFiltersAsync(Guid seasonId)
    {
        var all = await NewsAsync(seasonId, null, null, 500);
        return ([.. all.Select(c => c.Round).Distinct().OrderBy(n => n)],
            [.. all.Where(c => c.CompetitionId is { } && c.Competition is { }).GroupBy(c => c.CompetitionId!.Value)
                .Select(g => new NewsComp(g.Key, g.First().Competition!)).OrderBy(c => c.Name, StringComparer.Ordinal)]);
    }

    public async Task<NewsPost?> NewsPostAsync(Guid id)
    {
        await using var db = await dbs.CreateDbContextAsync();
        var row = await (from a in db.MatchArticles.AsNoTracking()
                         join f in db.Fixtures on a.FixtureId equals f.Id
                         join m in db.Matchdays on f.MatchdayId equals m.Id
                         join s in db.Seasons on m.SeasonId equals s.Id
                         where a.Id == id
                         select new { a, s }).SingleOrDefaultAsync();
        if (row is null) return null;
        var card = (await NewsAsync(row.s.Id, null, null, 500)).SingleOrDefault(c => c.Id == id);
        if (card is null) return null;
        var photos = await db.MatchPhotos.AsNoTracking().Where(p => p.ArticleId == id).OrderBy(p => p.Sort)
            .Select(p => new NewsPhoto(p.FileName, p.Caption)).ToListAsync();
        return new NewsPost(card, await InfoAsync(db, me is null ? null : await me.ManagerIdAsync(), row.s, pinned: row.s.Status != SeasonStatus.Active), row.a.Body, row.a.ByAi, photos);
    }

    public async Task<Guid?> ArticleIdAsync(Guid fixtureId)
    {
        await using var db = await dbs.CreateDbContextAsync();
        return await db.MatchArticles.Where(a => a.FixtureId == fixtureId).Select(a => (Guid?)a.Id).SingleOrDefaultAsync();
    }
}

public static class Fmt
{
    public static string Country(string? code) => code switch
    {
        null => "Cúp châu Âu", "ENG" => "Anh", "ESP" => "Tây Ban Nha", "GER" => "Đức", "ITA" => "Ý", _ => code,
    };

    /// <summary>"Anh · hạng 1", "Anh · cúp", "Cúp châu Âu · C1".</summary>
    public static string Kind(EditionView e) => e.Type switch
    {
        CompetitionType.ContinentalCup => $"{Country(null)} · C{e.Tier}",
        CompetitionType.DomesticCup => $"{Country(e.Country)} · cúp",
        _ => $"{Country(e.Country)} · hạng {e.Tier}",
    };

    /// <summary>Tên vòng của giải trong khung <paramref name="matchday"/> (<paramref name="own"/> = các trận của giải đó):
    /// vòng tròn "Vòng k", league phase "League phase · vòng k", loại trực tiếp "Tứ kết lượt 1", "Chung kết".</summary>
    public static string Round(IEnumerable<FixtureView> own, int matchday)
    {
        var f = own.FirstOrDefault(x => x.Matchday == matchday);
        if (f is null) return "";
        if (f.Stage is not (FixtureStage.League or FixtureStage.LeaguePhase)) return AdminText.SlotKind(f.Stage, f.Leg);
        var k = own.Where(x => x.Stage == f.Stage && x.Matchday < matchday).Select(x => x.Matchday).Distinct().Count() + 1;
        return f.Stage == FixtureStage.LeaguePhase ? $"League phase · vòng {k}" : $"Vòng {k}";
    }

    /// <summary>Tình trạng 1 nhóm trận: "Đã đá", "Đang đá", "Sắp đá".</summary>
    public static string Played(IEnumerable<FixtureView> g) =>
        g.All(f => f.Status == FixtureStatus.Played) ? "Đã đá" : g.Any(f => f.Status is FixtureStatus.Played or FixtureStatus.Playing) ? "Đang đá" : "Sắp đá";

    /// <summary>Vùng BXH theo luật đã khoá: quốc nội = suất C1, C2, lên hạng, xuống hạng; cúp = vào thẳng vòng loại trực tiếp, play-off, bị loại.</summary>
    public static IReadOnlyList<Zone> Zones(EditionView e, IReadOnlyList<EditionView> editions, int clubs)
    {
        var r = e.Rules;
        if (e.Type == CompetitionType.DomesticCup) return [];
        if (e.Type == CompetitionType.ContinentalCup)
            return r is { DirectKnockoutPlaces: { } d, PlayoffPlaces: { } p }
                ? [new(1, d, "z-ko", $"Vào thẳng {AdminText.Stage(CupDraw.KnockoutStages(clubs, d, p)[2].Stage).ToLowerInvariant()}"), new(d + 1, p, "z-po", "Đá play-off"), new(p + 1, clubs, "z-out", "Bị loại")]
                : [];
        string Cup(int tier) => editions.FirstOrDefault(x => x.Type == CompetitionType.ContinentalCup && x.Tier == tier)?.Name ?? $"C{tier}";
        Zone[] all =
        [
            new(1, r.ContinentalPlaces, "z-c1", $"Dự {Cup(1)}"),
            new(r.ContinentalPlaces + 1, r.ContinentalPlaces + r.SecondaryContinentalPlaces, "z-c2", $"Dự {Cup(2)}"),
            new(1, r.PromotionPlaces, "z-up", "Lên hạng"),
            new(clubs - r.RelegationPlaces + 1, clubs, "z-down", "Xuống hạng"),
        ];
        return [.. all.Where(z => z.To >= z.From)];
    }

    /// <summary>Tiền: 1.250.000 → "1,25 tr"; ≥ 1 tỷ → "1,2 tỷ".</summary>
    /// <summary>Mức màu chỉ số kiểu FM theo % thang (99 hoặc 8): v5 ≥ 90%, v4 ≥ 80%, v3 ≥ 70%, v2 ≥ 60%, còn lại v1.</summary>
    public static string Tier(int value, int max = 99) => (value * 100 / Math.Max(max, 1)) switch { >= 90 => "v5", >= 80 => "v4", >= 70 => "v3", >= 60 => "v2", _ => "v1" };

    /// <summary>Lớp màu nhóm vị trí (TM vàng, HV xanh dương, TV xanh lá, TĐ đỏ — quy ước PES).</summary>
    public static string Group(Position p) => Group(p.Group());
    public static string Group(PositionGroup g) => g switch
    {
        PositionGroup.Goalkeeper => "g-gk", PositionGroup.Defender => "g-df", PositionGroup.Midfielder => "g-mf", _ => "g-fw",
    };

    /// <summary>Chỉ số chính theo nhóm vị trí (viền vàng kiểu "Highlight" của FM) — chỉ để dễ nhìn, không ảnh hưởng tính toán.</summary>
    public static bool IsKey(Position p, string field) => KeyAttrs[p.Group()].Contains(field);
    static readonly Dictionary<PositionGroup, HashSet<string>> KeyAttrs = new()
    {
        [PositionGroup.Goalkeeper] = ["GOAL KEEPING", "RESPONSE", "JUMP", "BALANCE", "AGILITY"],
        [PositionGroup.Defender] = ["DEFENSE", "HEADING", "JUMP", "BALANCE", "RESPONSE", "AGGRESSION", "TOP SPEED"],
        [PositionGroup.Midfielder] = ["SHORT PASS ACCURACY", "LONG PASS ACCURACY", "TECHNIQUE", "DRIBBLE ACCURACY", "TEAM WORK", "STAMINA", "RESPONSE"],
        [PositionGroup.Forward] = ["ATTACK", "SHOT ACCURACY", "SHOT POWER", "SHOT TECHNIQUE", "DRIBBLE ACCURACY", "TOP SPEED", "ACCELERATION"],
    };

    /// <summary>Tên đầy đủ vị trí đăng ký (trang CLB/cầu thủ kiểu FM).</summary>
    public static string PositionName(Position p) => p switch
    {
        Position.GK => "Thủ môn", Position.SW => "Hậu vệ quét", Position.CB => "Trung vệ", Position.SB => "Hậu vệ biên",
        Position.DMF => "Tiền vệ phòng ngự", Position.WB => "Hậu vệ chạy cánh", Position.CMF => "Tiền vệ trung tâm", Position.SMF => "Tiền vệ cánh",
        Position.AMF => "Tiền vệ tấn công", Position.WF => "Tiền đạo cánh", Position.SS => "Hộ công", _ => "Trung phong",
    };
    public static string Money(long v) => Math.Abs(v) >= 1_000_000_000 ? $"{v / 1e9:0.##} tỷ" : $"{v / 1e6:0.##} tr";

    /// <summary>Tỷ số trận đã đá: "1 – 1"; chung kết hoà có loạt penalty → "1 – 1 (pen 3–2)" (PO 06/11).</summary>
    public static string Score(int? home, int? away, int? homePens, int? awayPens) =>
        $"{home} – {away}" + (homePens is { } h && awayPens is { } a ? $" (pen {h}–{a})" : "");

    /// <param name="window">Khung có kỳ CN; không có thì WindowOpen = chờ khoá.</param>
    public static string Status(MatchdayStatus s, bool window = true) => s switch
    {
        MatchdayStatus.Planned => "Chưa tới", MatchdayStatus.WindowOpen => window ? "Mở kỳ CN" : "Chờ khoá", MatchdayStatus.Locked => "Đã khoá, chờ đá",
        MatchdayStatus.Playing => "Đang đá", MatchdayStatus.Completed => "Đá xong", MatchdayStatus.Settled => "Đã chốt", _ => s.ToString(),
    };

    public static string Ledger(LedgerReason r) => r switch
    {
        LedgerReason.InitialBudget => "Ngân sách đầu mùa", LedgerReason.TransferIn => "Mua cầu thủ", LedgerReason.TransferOut => "Bán cầu thủ",
        LedgerReason.Adjustment => "Điều chỉnh", LedgerReason.Wage => "Lương", LedgerReason.LuxuryTax => "Thuế xa xỉ", LedgerReason.RenewalFee => "Phí gia hạn",
        _ => r.ToString(),
    };

    public static string Ledger(ClubLedgerEntry e) =>
        e is { Reason: LedgerReason.Adjustment, RefType: EconomyService.InitialBudgetTopUp } ? "Điều chỉnh: bù ngân sách đầu mùa" : Ledger(e.Reason);
}
