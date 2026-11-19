using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Infrastructure;

namespace Pes17League.Clubs.Application;

/// <summary>Giải VĐQG trong dữ liệu game (tools/pes17db/pesdb.py world): mã giải CompetitionEntry, đội theo thứ tự trong game.</summary>
public sealed record Pes17WorldLeague(int Comp, string Country, int Tier, string Name, int Legs, string? Menu, IReadOnlyList<int> Teams);
public sealed record Pes17WorldCup(string Country, string Name, string Short);
/// <param name="Clubs">Mã đội PES17 → nước (CLB dự giải = nước của giải; CLB châu Âu ngoài giải = nước thật; null = không dự giải nào).</param>
public sealed record Pes17World(IReadOnlyList<Pes17WorldLeague> Leagues, IReadOnlyList<Pes17WorldCup> Cups, IReadOnlyDictionary<string, string?> Clubs);

/// <param name="OutsideLeagues">CLB thuộc nước có giải nhưng game không xếp vào giải nào → không dự mùa này.</param>
public sealed record Pes17WorldResult(bool Reset, int ReleasedClubs, int Countries, IReadOnlyList<string> Competitions, IReadOnlyList<string> OutsideLeagues,
    SeasonAllocation Allocation);

/// <summary>
/// Thế giới Career PES17 (PO chốt 19/11/2026): mọi giải VĐQG trong game (thành viên đúng như game), cúp quốc gia (CLB hạng 1+2 cùng nước,
/// 1 lượt), Champions League + Europa League thể thức mới (36 đội, 8 trận league phase, top 8 vào vòng 1/8, 9–24 đá play-off 2 lượt,
/// chung kết 1 trận); suất C1/C2 mùa đầu theo sức mạnh (OVR) — <see cref="DivisionService"/>. Mùa đang mở mà chưa đá trận nào thì làm lại
/// từ đầu (xoá lịch, kỳ CN, ngân sách đầu mùa; CLB người về BOT để PO tự chọn CLB khi bắt đầu).
/// </summary>
public sealed class Pes17WorldService(ClubsDbContext db, TimeProvider clock)
{
    public const string ChampionsLeague = "Champions League", EuropaLeague = "Europa League";

    /// <summary>Suất C1/C2 của giải hạng 1 châu Âu (mùa đầu: CLB mạnh nhất theo OVR trong giải; mùa sau: theo BXH).</summary>
    public static readonly IReadOnlyDictionary<string, (int C1, int C2)> CupPlaces = new Dictionary<string, (int, int)>
    {
        ["ENG"] = (4, 2), ["ESP"] = (4, 2), ["ITA"] = (4, 2), ["GER"] = (4, 2), ["FRA"] = (3, 2), ["POR"] = (2, 2), ["NED"] = (2, 2),
    };

    /// <summary>Số đội xuống hạng 1 → lên từ hạng 2 (nước có 2 hạng).</summary>
    public static readonly IReadOnlyDictionary<string, int> Relegation = new Dictionary<string, int> { ["ENG"] = 3, ["ESP"] = 3, ["ITA"] = 3, ["FRA"] = 2 };

    /// <summary>PO chọn 19/11: tối đa 5 CLB/nước mỗi cúp (không thì suất thêm theo OVR dồn ~10 CLB Anh vào C1 → bốc thăm vô nghiệm).</summary>
    public const int MaxClubsPerCountry = 5;

    /// <summary>36 đội × 8 trận, 4 nhóm hạt giống 9 đội; CLB châu Âu ngoài giải: C1 7, C2 12; còn thiếu = suất thêm theo OVR; tối đa 5 CLB/nước.</summary>
    public static CompetitionRules NewFormatCupRules(int looseClubPlaces) => new()
    {
        LeaguePhaseTeams = 36, LeaguePhaseMatches = 8, DirectKnockoutPlaces = 8, PlayoffPlaces = 24, LooseClubPlaces = looseClubPlaces,
        MaxClubsPerCountry = MaxClubsPerCountry,
    };

    public static CompetitionRules LeagueRules(Pes17WorldLeague l, bool hasSecondTier)
    {
        var (c1, c2) = l.Tier == 1 ? CupPlaces.GetValueOrDefault(l.Country) : (0, 0);
        var moves = hasSecondTier ? Relegation.GetValueOrDefault(l.Country, 3) : 0;
        return new CompetitionRules
        {
            Legs = l.Legs, ContinentalPlaces = c1, SecondaryContinentalPlaces = c2,
            RelegationPlaces = l.Tier == 1 ? moves : 0, PromotionPlaces = l.Tier == 2 ? moves : 0,
        };
    }

    /// <summary>Ghi thế giới vào mùa chưa xong sớm nhất; <paramref name="dry"/> = chỉ xem trước chia suất cúp (rollback).</summary>
    public async Task<Pes17WorldResult> SetupAsync(Pes17World world, string actor, bool dry, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var season = await db.Seasons.Where(s => s.Status != SeasonStatus.Finished).OrderBy(s => s.Number).FirstOrDefaultAsync(ct)
            ?? throw new AdminRuleException("Chưa có mùa đang mở");
        if (season.PatchVersionId is not { } patch) throw new AdminRuleException($"{season.Name} chưa nhập patch");
        var reset = season.Status == SeasonStatus.Active;
        if (reset) await ResetSeasonAsync(season, ct);

        // CLB người → BOT (PO tự chọn CLB khi bắt đầu mùa)
        var humans = await db.TeamAssignments.Where(a => a.To == null && a.ControllerType == ControllerType.Human).Select(a => a.ParticipantId).ToListAsync(ct);
        var clubs = new ClubAdminService(db, clock);
        foreach (var p in humans.Distinct()) await clubs.SetHumanClubAsync(null, actor, p, ct);

        // Nước CLB
        var clubOf = await db.ClubPesMappings.Where(m => m.PatchVersionId == patch).ToDictionaryAsync(m => m.PesClubIndex, m => m.ClubId, ct);
        var all = await db.Clubs.Where(c => clubOf.Values.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        // CLB nước có giải mà không thuộc giải nào trong game (vd. Sochaux, Dijon, Nîmes) → không dự giải (nước null), không thành "CLB lẻ"
        var inLeague = world.Leagues.SelectMany(l => l.Teams).ToHashSet();
        var leagueCountries = world.Leagues.Select(l => l.Country).ToHashSet();
        var outside = new List<string>();
        foreach (var (pes, id) in clubOf)
        {
            if (!all.TryGetValue(id, out var club)) continue;
            var country = world.Clubs.GetValueOrDefault(pes.ToString());
            if (country is not null && leagueCountries.Contains(country) && !inLeague.Contains(pes)) { outside.Add(club.Name); country = null; }
            club.Country = country;
        }
        await db.SaveChangesAsync(ct);

        // Giải, cúp
        var admin = new CompetitionAdminService(db, clock);
        var existing = await db.Competitions.Where(c => c.Status == CompetitionStatus.Active).ToListAsync(ct);
        async Task<Competition> Upsert(string name, string? shortName, string? country, int? tier, CompetitionType type, CompetitionRules rules,
            Guid? up = null, Guid? down = null)
        {
            var c = existing.FirstOrDefault(x => x.Name == name && x.Type == type);
            if (c is null)
            {
                c = await admin.CreateAsync(new CompetitionInput(name, shortName, country, tier, type, null, rules, up, down), actor, ct);
                existing.Add(c);
            }
            else await admin.UpdateAsync(c.Id, new CompetitionInput(name, c.ShortName ?? shortName, country, tier, type, c.LogoPath, rules, up, down), actor, ct);
            return c;
        }
        var names = new List<string>();
        var leagueComp = new Dictionary<Pes17WorldLeague, Competition>();
        foreach (var l in world.Leagues.OrderBy(l => l.Country, StringComparer.Ordinal).ThenBy(l => l.Tier))
        {
            // lượt 1 chưa lên/xuống (luật: có suất lên/xuống thì phải có giải liên kết) — lượt 2 nối 2 hạng
            leagueComp[l] = await Upsert(l.Name, $"{l.Country} {l.Tier}", l.Country, l.Tier, CompetitionType.DomesticLeague, LeagueRules(l, false));
            names.Add(l.Name);
        }
        foreach (var g in world.Leagues.GroupBy(l => l.Country).Where(g => g.Count() == 2))
        {   // liên kết lên/xuống hạng
            var (top, low) = (g.Single(l => l.Tier == 1), g.Single(l => l.Tier == 2));
            var (t, b) = (leagueComp[top], leagueComp[low]);
            await admin.UpdateAsync(t.Id, new CompetitionInput(t.Name, t.ShortName, t.Country, 1, t.Type, t.LogoPath, LeagueRules(top, true), null, b.Id), actor, ct);
            await admin.UpdateAsync(b.Id, new CompetitionInput(b.Name, b.ShortName, b.Country, 2, b.Type, b.LogoPath, LeagueRules(low, true), t.Id, null), actor, ct);
        }
        foreach (var c in world.Cups)
        {
            await Upsert(c.Name, c.Short, c.Country, null, CompetitionType.DomesticCup, CompetitionAdminService.DomesticCupRules);
            names.Add(c.Name);
        }
        await Upsert(ChampionsLeague, "UCL", null, 1, CompetitionType.ContinentalCup, NewFormatCupRules(7));
        await Upsert(EuropaLeague, "UEL", null, 2, CompetitionType.ContinentalCup, NewFormatCupRules(12));
        names.AddRange([ChampionsLeague, EuropaLeague]);
        var keep = names.ToHashSet();
        foreach (var c in existing.Where(c => !keep.Contains(c.Name))) await admin.DeleteOrArchiveAsync(c.Id, actor, ct);   // giải cũ ngoài thế giới

        // Mùa giải: làm lại; giải quốc nội = thành viên như trong game
        var old = await db.Editions.Where(e => e.SeasonId == season.Id).Select(e => e.Id).ToListAsync(ct);
        await db.EditionClubs.Where(x => old.Contains(x.EditionId)).ExecuteDeleteAsync(ct);
        await db.Editions.Where(e => old.Contains(e.Id)).ExecuteDeleteAsync(ct);
        foreach (var (l, comp) in leagueComp)
        {
            var e = new CompetitionEdition { CompetitionId = comp.Id, SeasonId = season.Id };
            db.Editions.Add(e);
            var missing = l.Teams.Where(t => !clubOf.ContainsKey(t)).ToList();
            if (missing.Count > 0) throw new AdminRuleException($"{l.Name}: đội chưa nhập {string.Join(",", missing)}");
            db.EditionClubs.AddRange(l.Teams.Select(t => new EditionClub { EditionId = e.Id, ClubId = clubOf[t] }));
        }
        await db.SaveChangesAsync(ct);

        var division = new DivisionService(db, clock);
        var plan = await division.PreviewAsync(season.Id, ct);
        if (!dry && plan.Errors.Count == 0) await division.ApplyAsync(season.Id, actor, ct);
        var result = new Pes17WorldResult(reset, humans.Count, world.Clubs.Values.Where(c => c is not null).Distinct().Count(), names, outside, plan);
        if (dry || plan.Errors.Count > 0) await tx.RollbackAsync(ct);
        else await tx.CommitAsync(ct);
        return result;
    }

    /// <summary>Mùa đã bắt đầu nhưng chưa đá trận nào → về chưa bắt đầu: xoá khung ngày + trận + lần chạy trận, kỳ CN, CLB của mùa, ngân sách đầu mùa.</summary>
    private async Task ResetSeasonAsync(Season season, CancellationToken ct)
    {
        var mdIds = await db.Matchdays.Where(m => m.SeasonId == season.Id).Select(m => m.Id).ToListAsync(ct);
        var fixtures = db.Fixtures.Where(f => mdIds.Contains(f.MatchdayId));
        if (await fixtures.AnyAsync(f => f.Status != FixtureStatus.Scheduled, ct))
            throw new AdminRuleException($"{season.Name} đã có trận đá/đang đá: không làm lại được");
        var fxIds = await fixtures.Select(f => f.Id).ToListAsync(ct);
        await db.MatchAttempts.Where(a => fxIds.Contains(a.FixtureId)).ExecuteDeleteAsync(ct);
        await db.OptionFileBuilds.Where(b => b.MatchdayId != null && mdIds.Contains(b.MatchdayId.Value)).ExecuteDeleteAsync(ct);
        await db.Fixtures.Where(f => mdIds.Contains(f.MatchdayId)).ExecuteDeleteAsync(ct);
        await db.Matchdays.Where(m => m.SeasonId == season.Id).ExecuteUpdateAsync(s => s.SetProperty(m => m.TransferWindowId, (Guid?)null), ct);
        await db.TransferWindows.Where(w => w.SeasonId == season.Id).ExecuteDeleteAsync(ct);
        await db.Matchdays.Where(m => m.SeasonId == season.Id).ExecuteDeleteAsync(ct);
        await db.SeasonClubs.Where(x => x.SeasonId == season.Id).ExecuteDeleteAsync(ct);
        await db.Ledger.Where(e => e.RefId == season.Id && (e.Reason == LedgerReason.InitialBudget || e.RefType == EconomyService.InitialBudgetTopUp)).ExecuteDeleteAsync(ct);
        season.Status = SeasonStatus.Planned;
        db.AdminAudit.Add(new AdminAuditEntry { At = clock.GetUtcNow(), Actor = SeasonScheduleService.SystemActor, EntityType = nameof(Season), EntityId = season.Id, Action = "ResetSeason" });
        await db.SaveChangesAsync(ct);
    }
}
