using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Infrastructure;

namespace Pes17League.Clubs.Application;

/// <summary>Thao tác admin vi phạm luật nghiệp vụ; web hiện nguyên thông báo.</summary>
public sealed class AdminRuleException(string message) : Exception(message);

/// <summary>Thông tin giải admin nhập khi thêm/sửa.</summary>
public sealed record CompetitionInput(string Name, string? ShortName, string? Country, int? Tier, CompetitionType Type, string? LogoPath,
    CompetitionRules Rules, Guid? PromotesToId = null, Guid? RelegatesToId = null);

/// <summary>
/// Admin giải đấu (bước 3a, docs/domain-admin-design.md mục 5).
/// - Giải: thêm/sửa thông tin + luật. Chưa có mùa giải nào thì xoá được, có rồi thì lưu trữ.
///   Luật sửa lúc nào cũng được nhưng chỉ áp dụng cho mùa giải chưa Scheduled (đã Scheduled thì dùng RulesSnapshot, A7).
/// - Mùa giải (edition): tạo cho mùa chưa kết thúc; sửa CLB/hạt giống và xoá khi còn Draft.
///   Mỗi mùa, 1 CLB dự tối đa 1 giải quốc gia, 1 cúp châu lục và 1 cúp quốc gia (theo <see cref="CompetitionType"/>).
/// - Cúp châu lục: Tier 1 = C1, Tier 2 = C2 (mỗi hạng 1 cúp đang hoạt động); Legs = số lượt mỗi cặp loại trực tiếp (chung kết 1 trận).
///   Giải quốc gia: ContinentalPlaces → C1, SecondaryContinentalPlaces → C2.
/// - Cúp quốc gia: 1 cúp/nước, Legs = 1, đội = hạng 1+2 cùng nước.
/// - Chung kết / trận knockout 1 lượt hoà sau 120' mà không đọc được loạt penalty: admin xem video rồi nhập tỷ số penalty (PO 06/11).
/// Mọi thao tác ghi AdminAuditEntry.
/// </summary>
public sealed class CompetitionAdminService(ClubsDbContext db, TimeProvider clock)
{
    /// <summary>Bộ giải mùa đầu (PO chốt 05/11, tên thật): 4 nước × 2 hạng, 2 lên 2 xuống; hạng 1 có 4 suất C1 + 2 suất C2.</summary>
    public static readonly IReadOnlyList<(string Country, string Top, string Second)> DefaultLeagues =
    [
        ("ENG", "Premier League", "Championship"),
        ("ESP", "La Liga", "La Liga 2"),
        ("ITA", "Serie A", "Serie B"),
        ("GER", "Bundesliga", "2. Bundesliga"),
    ];

    /// <summary>Cúp quốc gia mặc định (1 / nước).</summary>
    public static readonly IReadOnlyList<(string Country, string Name, string Short)> DefaultDomesticCups =
    [
        ("ENG", "FA Cup", "FA Cup"),
        ("ESP", "Copa del Rey", "CdR"),
        ("ITA", "Coppa Italia", "CI"),
        ("GER", "DFB-Pokal", "DFB"),
    ];

    public static readonly CompetitionRules TopTierRules = new() { RelegationPlaces = 2, ContinentalPlaces = 4, SecondaryContinentalPlaces = 2 };
    public static readonly CompetitionRules SecondTierRules = new() { PromotionPlaces = 2 };
    public static readonly CompetitionRules DomesticCupRules = new() { Legs = 1 };

    /// <summary>League phase 24 đội × 8 trận; thứ 1–4 vào thẳng, 5–12 đá play-off. CLB lẻ: C1 8, C2 15 (+1 suất thêm theo OVR).</summary>
    public static CompetitionRules CupRules(int looseClubPlaces) => new()
    {
        LeaguePhaseTeams = 24, LeaguePhaseMatches = 8, DirectKnockoutPlaces = 4, PlayoffPlaces = 12, LooseClubPlaces = looseClubPlaces,
    };

    /// <summary>Số đội vào vòng loại trực tiếp chính = vào thẳng + đội thắng play-off (8 = tứ kết, 16 = vòng 1/8).</summary>
    public static int? KnockoutTeams(CompetitionRules r) =>
        r.DirectKnockoutPlaces is { } d && r.PlayoffPlaces is { } p ? d + (p - d) / 2 : null;

    public static string KnockoutStageName(int teams) => teams switch
    {
        2 => "Chung kết", 4 => "Bán kết", 8 => "Tứ kết", 16 => "Vòng 1/8", 32 => "Vòng 1/16", 64 => "Vòng 1/32",
        _ => $"Vòng {teams} đội",
    };

    /// <summary>Số trận loại trực tiếp (play-off + các vòng, chung kết 1 trận); null khi luật cúp chưa hợp lệ.</summary>
    public static int? KnockoutMatches(CompetitionRules r)
    {
        if (KnockoutTeams(r) is not { } ko || ko < 2) return null;
        // Cặp play-off + (ko − 2) cặp trước chung kết, mỗi cặp Legs trận; chung kết 1 trận.
        return (r.PlayoffPlaces!.Value - r.DirectKnockoutPlaces!.Value) / 2 * r.Legs + (ko - 2) * r.Legs + 1;
    }

    /// <summary>Lỗi luật; rỗng = hợp lệ.</summary>
    public static IReadOnlyList<string> Validate(CompetitionType type, CompetitionRules r)
    {
        var e = new List<string>();
        if (r.PointsLoss < 0 || r.PointsDraw < r.PointsLoss || r.PointsWin <= r.PointsDraw) e.Add("Điểm phải: thắng > hoà ≥ thua ≥ 0");
        if (r.Legs is not (1 or 2)) e.Add("Số lượt phải là 1 hoặc 2");
        if (r.PromotionPlaces < 0 || r.RelegationPlaces < 0 || r.ContinentalPlaces < 0 || r.SecondaryContinentalPlaces < 0)
            e.Add("Số suất không được âm");
        if (type == CompetitionType.DomesticCup)
        {
            if (r.Legs != 1) e.Add("Cúp quốc gia chỉ đá 1 lượt");
            if (r.LeaguePhaseTeams is not null || r.LeaguePhaseMatches is not null || r.DirectKnockoutPlaces is not null
                || r.PlayoffPlaces is not null || r.LooseClubPlaces is not null)
                e.Add("Cúp quốc gia không dùng luật league phase");
            if (r.PromotionPlaces > 0 || r.RelegationPlaces > 0 || r.ContinentalPlaces > 0 || r.SecondaryContinentalPlaces > 0)
                e.Add("Cúp quốc gia không có lên/xuống hạng hay suất châu Âu");
            return e;
        }
        if (type != CompetitionType.ContinentalCup) return e;

        if (r.LeaguePhaseTeams is not { } n || r.LeaguePhaseMatches is not { } k || r.DirectKnockoutPlaces is not { } d || r.PlayoffPlaces is not { } p)
        {
            e.Add("Cúp cần đủ: số đội, số trận mỗi đội, suất vào thẳng, hạng cuối đá play-off");
            return e;
        }
        if (n < 4) e.Add("League phase cần ít nhất 4 đội");
        if (k < 2 || k >= n || k % 2 != 0) e.Add("Số trận mỗi đội phải chẵn, từ 2 tới ít hơn số đội");
        if (d < 0 || p < d || p > n) e.Add("Cần 0 ≤ suất vào thẳng ≤ hạng cuối đá play-off ≤ số đội");
        else if ((p - d) % 2 != 0) e.Add("Số đội đá play-off phải chẵn");
        else if (KnockoutTeams(r) is not (2 or 4 or 8 or 16)) e.Add("Vào thẳng + thắng play-off phải là 2, 4, 8 hoặc 16 đội");
        if (r.LooseClubPlaces is { } loose && (loose < 0 || loose > n)) e.Add("Suất CLB lẻ phải từ 0 tới số đội");
        return e;
    }

    /// <summary>Cảnh báo giữa các giải đang hoạt động (không chặn lưu): liên kết lên/xuống hạng lệch, số suất cúp thừa.</summary>
    public static IReadOnlyList<string> Warnings(IReadOnlyList<Competition> competitions)
    {
        var w = new List<string>();
        var active = competitions.Where(c => c.Status == CompetitionStatus.Active).ToList();
        var byId = active.ToDictionary(c => c.Id);
        foreach (var c in active)
        {
            if (c.PromotesToId is { } up && !byId.ContainsKey(up)) w.Add($"{c.Name}: giải hạng trên đã lưu trữ");
            if (c.RelegatesToId is not { } down) continue;
            if (!byId.TryGetValue(down, out var lower)) { w.Add($"{c.Name}: giải hạng dưới đã lưu trữ"); continue; }
            if (lower.PromotesToId != c.Id) w.Add($"{c.Name} xuống {lower.Name} nhưng {lower.Name} không lên {c.Name}");
            else if (lower.Rules.PromotionPlaces != c.Rules.RelegationPlaces)
                w.Add($"{c.Name} xuống {c.Rules.RelegationPlaces} đội nhưng {lower.Name} lên {lower.Rules.PromotionPlaces} đội: số đội mỗi hạng sẽ đổi");
        }
        foreach (var cup in active.Where(c => c.Type == CompetitionType.ContinentalCup && c.Rules.LeaguePhaseTeams is not null))
        {
            var (domestic, loose) = CupEntrants(cup, active);
            if (domestic + loose > cup.Rules.LeaguePhaseTeams) w.Add($"{cup.Name}: {domestic} suất quốc nội + {loose} CLB lẻ vượt {cup.Rules.LeaguePhaseTeams} đội");
        }
        return w;
    }

    /// <summary>Suất vào cúp: từ giải quốc gia hạng 1 đang hoạt động (C1 = ContinentalPlaces, C2 = SecondaryContinentalPlaces) + CLB lẻ.
    /// Còn thiếu so với N = suất thêm theo OVR.</summary>
    public static (int Domestic, int Loose) CupEntrants(Competition cup, IReadOnlyList<Competition> competitions)
    {
        var leagues = competitions.Where(c => c.Status == CompetitionStatus.Active && c.Type == CompetitionType.DomesticLeague);
        var domestic = cup.Tier switch
        {
            1 => leagues.Sum(c => c.Rules.ContinentalPlaces),
            2 => leagues.Sum(c => c.Rules.SecondaryContinentalPlaces),
            _ => 0,
        };
        return (domestic, cup.Rules.LooseClubPlaces ?? 0);
    }

    /// <summary>Tạo bộ 14 giải mặc định (8 quốc nội + 4 cúp quốc gia + C1/C2); chỉ khi chưa có giải nào trùng tên. Trả số giải đã tạo.</summary>
    public async Task<int> SeedDefaultsAsync(string actor, CancellationToken ct = default)
    {
        var names = DefaultLeagues.SelectMany(l => new[] { l.Top, l.Second })
            .Concat(DefaultDomesticCups.Select(c => c.Name)).Concat(["Champions League", "Europa League"]).ToList();
        if (await db.Competitions.AnyAsync(c => names.Contains(c.Name), ct)) throw new AdminRuleException("Đã có giải trùng tên trong bộ mặc định");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var now = clock.GetUtcNow();
        Competition New(string name, string shortName, string? country, int? tier, CompetitionType type, CompetitionRules rules) =>
            new() { Name = name, ShortName = shortName, Country = country, Tier = tier, Type = type, Rules = rules, CreatedAt = now };
        var pairs = DefaultLeagues.Select(l => (Top: New(l.Top, $"{l.Country} 1", l.Country, 1, CompetitionType.DomesticLeague, TopTierRules),
            Second: New(l.Second, $"{l.Country} 2", l.Country, 2, CompetitionType.DomesticLeague, SecondTierRules))).ToList();
        List<Competition> all =
        [
            .. pairs.SelectMany(p => new[] { p.Top, p.Second }),
            .. DefaultDomesticCups.Select(c => New(c.Name, c.Short, c.Country, null, CompetitionType.DomesticCup, DomesticCupRules)),
            New("Champions League", "C1", null, 1, CompetitionType.ContinentalCup, CupRules(8)),
            New("Europa League", "C2", null, 2, CompetitionType.ContinentalCup, CupRules(15)),
        ];
        db.Competitions.AddRange(all);
        await db.SaveChangesAsync(ct);      // nối lên/xuống hạng sau khi đã có dòng (2 giải trỏ nhau)
        foreach (var (top, second) in pairs) (top.RelegatesToId, second.PromotesToId) = (second.Id, top.Id);
        foreach (var c in all) Audit(nameof(Competition), c.Id, "Create", null, Snapshot(c), actor);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return all.Count;
    }

    /// <summary>Thêm 4 cúp quốc gia mặc định nếu chưa có (cho DB đã seed 10 giải cũ). Trả số giải đã tạo.</summary>
    public async Task<int> SeedDomesticCupsAsync(string actor, CancellationToken ct = default)
    {
        var existing = await db.Competitions.Where(c => DefaultDomesticCups.Select(x => x.Name).Contains(c.Name)).Select(c => c.Name).ToListAsync(ct);
        var missing = DefaultDomesticCups.Where(c => !existing.Contains(c.Name)).ToList();
        if (missing.Count == 0) throw new AdminRuleException("Đã có đủ 4 cúp quốc gia mặc định");
        var now = clock.GetUtcNow();
        var created = missing.Select(c => new Competition
        {
            Name = c.Name, ShortName = c.Short, Country = c.Country, Type = CompetitionType.DomesticCup, Rules = DomesticCupRules, CreatedAt = now,
        }).ToList();
        db.Competitions.AddRange(created);
        foreach (var c in created) Audit(nameof(Competition), c.Id, "Create", null, Snapshot(c), actor);
        await db.SaveChangesAsync(ct);
        return created.Count;
    }

    public static CompetitionInput Input(Competition c) =>
        new(c.Name, c.ShortName, c.Country, c.Tier, c.Type, c.LogoPath, c.Rules, c.PromotesToId, c.RelegatesToId);

    public async Task<Competition> CreateAsync(CompetitionInput input, string actor, CancellationToken ct = default)
    {
        input = Normalize(input);
        await ValidateInputAsync(null, input, ct);
        var c = new Competition { Name = input.Name, CreatedAt = clock.GetUtcNow() };
        Apply(c, input);
        db.Competitions.Add(c);
        Audit(nameof(Competition), c.Id, "Create", null, Snapshot(c), actor);
        await db.SaveChangesAsync(ct);
        return c;
    }

    public async Task UpdateAsync(Guid id, CompetitionInput input, string actor, CancellationToken ct = default)
    {
        var c = await db.Competitions.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new AdminRuleException("Không tìm thấy giải");
        input = Normalize(input);
        if (input.Type != c.Type && await db.Editions.AnyAsync(e => e.CompetitionId == id, ct))
            throw new AdminRuleException("Đã có mùa giải: không đổi loại giải được");
        await ValidateInputAsync(id, input, ct);
        var before = Snapshot(c);
        Apply(c, input);
        Audit(nameof(Competition), id, "Update", before, Snapshot(c), actor);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Chưa có mùa giải → xoá (trả true); có rồi → lưu trữ (trả false). Giải khác còn liên kết lên/xuống hạng tới thì không xoá.</summary>
    public async Task<bool> DeleteOrArchiveAsync(Guid id, string actor, CancellationToken ct = default)
    {
        var c = await db.Competitions.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new AdminRuleException("Không tìm thấy giải");
        if (await db.Editions.AnyAsync(e => e.CompetitionId == id, ct))
        {
            if (c.Status == CompetitionStatus.Archived) return false;
            c.Status = CompetitionStatus.Archived;
            Audit(nameof(Competition), id, "Archive", null, null, actor);
            await db.SaveChangesAsync(ct);
            return false;
        }
        var linked = await db.Competitions.Where(x => x.PromotesToId == id || x.RelegatesToId == id).Select(x => x.Name).ToListAsync(ct);
        if (linked.Count > 0) throw new AdminRuleException($"{string.Join(", ", linked)} còn liên kết lên/xuống hạng tới giải này: bỏ liên kết trước");
        Audit(nameof(Competition), id, "Delete", Snapshot(c), null, actor);
        db.Competitions.Remove(c);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task RestoreAsync(Guid id, string actor, CancellationToken ct = default)
    {
        var c = await db.Competitions.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new AdminRuleException("Không tìm thấy giải");
        if (c.Status == CompetitionStatus.Active) return;
        await ValidateInputAsync(id, Input(c), ct);
        c.Status = CompetitionStatus.Active;
        Audit(nameof(Competition), id, "Restore", null, null, actor);
        await db.SaveChangesAsync(ct);
    }

    public async Task<CompetitionEdition> CreateEditionAsync(Guid competitionId, Guid seasonId, string actor, CancellationToken ct = default)
    {
        var c = await db.Competitions.SingleOrDefaultAsync(x => x.Id == competitionId, ct) ?? throw new AdminRuleException("Không tìm thấy giải");
        if (c.Status != CompetitionStatus.Active) throw new AdminRuleException("Giải đã lưu trữ");
        var season = await db.Seasons.SingleOrDefaultAsync(s => s.Id == seasonId, ct) ?? throw new AdminRuleException("Không tìm thấy mùa");
        if (season.Status == SeasonStatus.Finished) throw new AdminRuleException("Mùa đã kết thúc");
        if (await db.Editions.AnyAsync(e => e.CompetitionId == competitionId && e.SeasonId == seasonId, ct))
            throw new AdminRuleException($"{c.Name} đã có mùa giải {season.Name}");
        var e = new CompetitionEdition { CompetitionId = competitionId, SeasonId = seasonId };
        db.Editions.Add(e);
        Audit(nameof(CompetitionEdition), e.Id, "Create", null, new { Competition = c.Name, Season = season.Name }, actor);
        await db.SaveChangesAsync(ct);
        return e;
    }

    public async Task DeleteEditionAsync(Guid editionId, string actor, CancellationToken ct = default)
    {
        var e = await DraftEditionAsync(editionId, ct);
        var clubs = await db.EditionClubs.Where(x => x.EditionId == editionId).ToListAsync(ct);
        Audit(nameof(CompetitionEdition), editionId, "Delete",
            new { e.CompetitionId, e.SeasonId, Clubs = clubs.Select(x => new { x.ClubId, x.Seed }) }, null, actor);
        db.EditionClubs.RemoveRange(clubs);
        db.Editions.Remove(e);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Thêm CLB vào mùa giải Draft hoặc đổi hạt giống (null = chưa xếp).</summary>
    public async Task SetEditionClubAsync(Guid editionId, Guid clubId, int? seed, string actor, CancellationToken ct = default)
    {
        var e = await DraftEditionAsync(editionId, ct);
        if (seed < 1) throw new AdminRuleException("Hạt giống phải từ 1");
        var club = await db.Clubs.SingleOrDefaultAsync(c => c.Id == clubId, ct) ?? throw new AdminRuleException("Không tìm thấy CLB");
        if (club.Status != ClubStatus.Active) throw new AdminRuleException($"{club.Name} không còn hoạt động");
        var type = await db.Competitions.Where(c => c.Id == e.CompetitionId).Select(c => c.Type).SingleAsync(ct);
        var other = await (from ec in db.EditionClubs
                           join ed in db.Editions on ec.EditionId equals ed.Id
                           join c in db.Competitions on ed.CompetitionId equals c.Id
                           where ec.ClubId == clubId && ed.SeasonId == e.SeasonId && ed.Id != editionId && c.Type == type
                           select c.Name).FirstOrDefaultAsync(ct);
        if (other is not null) throw new AdminRuleException($"{club.Name} đã dự {other} mùa này");
        var row = await db.EditionClubs.SingleOrDefaultAsync(x => x.EditionId == editionId && x.ClubId == clubId, ct);
        var oldSeed = row?.Seed;
        if (row is not null && oldSeed == seed) return;
        if (seed is not null && await (from ec in db.EditionClubs join c in db.Clubs on ec.ClubId equals c.Id
                                       where ec.EditionId == editionId && ec.Seed == seed && ec.ClubId != clubId select c.Name).FirstOrDefaultAsync(ct) is { } taken)
            throw new AdminRuleException($"Hạt giống {seed} đã là {taken}");
        if (row is null) db.EditionClubs.Add(new EditionClub { EditionId = editionId, ClubId = clubId, Seed = seed });
        else row.Seed = seed;
        Audit(nameof(CompetitionEdition), editionId, row is null ? "AddClub" : "SetSeed",
            row is null ? null : new { Club = club.Name, Seed = oldSeed }, new { Club = club.Name, Seed = seed }, actor);
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveEditionClubAsync(Guid editionId, Guid clubId, string actor, CancellationToken ct = default)
    {
        await DraftEditionAsync(editionId, ct);
        var row = await db.EditionClubs.SingleOrDefaultAsync(x => x.EditionId == editionId && x.ClubId == clubId, ct);
        if (row is null) return;
        var name = await db.Clubs.Where(c => c.Id == clubId).Select(c => c.Name).SingleAsync(ct);
        Audit(nameof(CompetitionEdition), editionId, "RemoveClub", new { Club = name, row.Seed }, null, actor);
        db.EditionClubs.Remove(row);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Nhập loạt penalty cho trận knockout 1 lượt đã đá, hoà sau 120', chưa có tỷ số penalty; chung kết → cúp Finished.</summary>
    public async Task SetFinalPenaltiesAsync(Guid fixtureId, int home, int away, string actor, CancellationToken ct = default)
    {
        var f = await db.Fixtures.SingleOrDefaultAsync(x => x.Id == fixtureId, ct) ?? throw new AdminRuleException("Không tìm thấy trận");
        if (f.Leg is not null || f.Stage <= FixtureStage.LeaguePhase || f.Status != FixtureStatus.Played)
            throw new AdminRuleException("Chỉ nhập penalty cho trận knockout 1 lượt đã đá");
        if (f.HomeGoals != f.AwayGoals) throw new AdminRuleException("Trận không hoà sau 120': không có loạt penalty");
        if (f.HomePenalties is not null) throw new AdminRuleException("Trận đã có tỷ số penalty");
        if (!Fixture.PenaltiesValid(f.HomeGoals!.Value, f.AwayGoals!.Value, home, away)) throw new AdminRuleException("Tỷ số penalty phải không âm và không hoà");
        (f.HomePenalties, f.AwayPenalties) = (home, away);
        Audit(nameof(Fixture), f.Id, "SetPenalties", null, new { Home = home, Away = away }, actor);
        if (f.Stage == FixtureStage.Final
            && await db.Editions.SingleOrDefaultAsync(e => e.Id == f.CompetitionEditionId && e.Status == EditionStatus.Active, ct) is { } edition)
            edition.Status = EditionStatus.Finished;     // có đội vô địch → cúp kết thúc (bước 6)
        await db.SaveChangesAsync(ct);
    }

    private async Task<CompetitionEdition> DraftEditionAsync(Guid editionId, CancellationToken ct)
    {
        var e = await db.Editions.SingleOrDefaultAsync(x => x.Id == editionId, ct) ?? throw new AdminRuleException("Không tìm thấy mùa giải");
        if (e.Status != EditionStatus.Draft) throw new AdminRuleException("Mùa giải đã xếp lịch: không sửa được");
        return e;
    }

    private static CompetitionInput Normalize(CompetitionInput i) => i with
    {
        Name = i.Name.Trim(),
        ShortName = string.IsNullOrWhiteSpace(i.ShortName) ? null : i.ShortName.Trim(),
        Country = string.IsNullOrWhiteSpace(i.Country) ? null : i.Country.Trim().ToUpperInvariant(),
        LogoPath = string.IsNullOrWhiteSpace(i.LogoPath) ? null : i.LogoPath.Trim(),
    };

    private async Task ValidateInputAsync(Guid? id, CompetitionInput i, CancellationToken ct)
    {
        var e = new List<string>();
        if (i.Name.Length is 0 or > 100) e.Add("Tên giải 1–100 ký tự");
        if (i.ShortName?.Length > 20) e.Add("Tên ngắn tối đa 20 ký tự");
        if (i.Country?.Length > 3) e.Add("Mã nước tối đa 3 ký tự");
        if (i.Tier is < 1) e.Add("Hạng phải từ 1");
        if (i.Type == CompetitionType.DomesticLeague && (i.Country is null || i.Tier is null)) e.Add("Giải quốc gia cần mã nước và hạng");
        if (i.Type == CompetitionType.DomesticCup)
        {
            if (i.Country is null) e.Add("Cúp quốc gia cần mã nước");
            if (i.Tier is not null) e.Add("Cúp quốc gia không có hạng");
            if (i.PromotesToId is not null || i.RelegatesToId is not null) e.Add("Cúp quốc gia không có lên/xuống hạng");
            if (i.Country is not null && await db.Competitions.AnyAsync(c => c.Id != id && c.Type == CompetitionType.DomesticCup
                    && c.Country == i.Country && c.Status == CompetitionStatus.Active, ct))
                e.Add($"Đã có cúp quốc gia của {i.Country} đang hoạt động");
        }
        if (i.Type == CompetitionType.ContinentalCup)
        {
            if (i.Country is not null) e.Add("Cúp châu lục không có mã nước");
            if (i.Tier is not (1 or 2)) e.Add("Cúp châu lục: hạng 1 = C1, hạng 2 = C2");
            if (i.PromotesToId is not null || i.RelegatesToId is not null) e.Add("Cúp châu lục không có lên/xuống hạng");
            if (await db.Competitions.AnyAsync(c => c.Id != id && c.Type == CompetitionType.ContinentalCup && c.Tier == i.Tier
                    && c.Status == CompetitionStatus.Active, ct))
                e.Add($"Đã có cúp hạng {i.Tier} đang hoạt động");
        }
        if (i.Rules.PromotionPlaces > 0 && i.PromotesToId is null) e.Add("Có suất lên hạng thì phải chọn giải hạng trên");
        if (i.Rules.RelegationPlaces > 0 && i.RelegatesToId is null) e.Add("Có suất xuống hạng thì phải chọn giải hạng dưới");
        e.AddRange(Validate(i.Type, i.Rules));
        foreach (var (linkId, upper) in new[] { (i.PromotesToId, true), (i.RelegatesToId, false) })
        {
            if (linkId is not { } l) continue;
            var other = await db.Competitions.SingleOrDefaultAsync(c => c.Id == l, ct);
            if (other is null || l == id) { e.Add("Giải liên kết lên/xuống hạng không hợp lệ"); continue; }
            if (other.Type != CompetitionType.DomesticLeague || other.Country != i.Country) e.Add($"{other.Name} không phải giải quốc gia cùng nước");
            else if (upper ? other.Tier >= i.Tier : other.Tier <= i.Tier) e.Add($"{other.Name} không phải hạng {(upper ? "trên" : "dưới")}");
        }
        if (await db.Competitions.AnyAsync(c => c.Name == i.Name && c.Id != id, ct)) e.Add($"Đã có giải tên {i.Name}");
        if (e.Count > 0) throw new AdminRuleException(string.Join("; ", e));
    }

    private static void Apply(Competition c, CompetitionInput i)
    {
        c.Name = i.Name;
        c.ShortName = i.ShortName;
        c.Country = i.Country;
        c.Tier = i.Tier;
        c.Type = i.Type;
        c.LogoPath = i.LogoPath;
        c.Rules = i.Rules;
        c.PromotesToId = i.PromotesToId;
        c.RelegatesToId = i.RelegatesToId;
    }

    private static object Snapshot(Competition c) => new
    {
        c.Name, c.ShortName, c.Country, c.Tier, Type = c.Type.ToString(), c.LogoPath, c.Rules, Status = c.Status.ToString(), c.PromotesToId, c.RelegatesToId,
    };

    private void Audit(string entityType, Guid id, string action, object? before, object? after, string actor) =>
        db.AdminAudit.Add(new AdminAuditEntry
        {
            At = clock.GetUtcNow(), Actor = actor, EntityType = entityType, EntityId = id, Action = action,
            Before = before is null ? null : JsonSerializer.Serialize(before),
            After = after is null ? null : JsonSerializer.Serialize(after),
        });
}
