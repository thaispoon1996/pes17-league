using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Infrastructure;

namespace Pes17League.Clubs.Application;

public enum InboxKind { Task, Warning, Info }

public sealed record InboxItem(InboxKind Kind, string Title, string Body, string? Link);

public sealed record PressPending(Guid FixtureId, string Opponent, int GoalsFor, int GoalsAgainst);

public sealed record MediaDesk(IReadOnlyList<InboxItem> Inbox, PressPending? Press, string FanMood, int FanPoints, int FanGames,
    IReadOnlyList<ConditionRow> Squad, IReadOnlySet<Guid> TalkedThisRound);

/// <summary>Hộp thư + họp báo + nói chuyện cầu thủ + tâm trạng CĐV (FM đợt 9). Luật ở <see cref="MediaRules"/>.</summary>
public sealed class MediaService(ClubsDbContext db, TimeProvider clock)
{
    public async Task<MediaDesk> DeskAsync(Guid seasonId, Guid clubId, CancellationToken ct = default)
    {
        var names = await db.Clubs.ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var played = await (from f in db.Fixtures join m in db.Matchdays on f.MatchdayId equals m.Id
                            where m.SeasonId == seasonId && (f.HomeClubId == clubId || f.AwayClubId == clubId) && f.HomeGoals != null && f.AwayGoals != null
                            orderby f.PlayedAt descending, m.Number descending
                            select f).AsNoTracking().Take(5).ToListAsync(ct);
        int For(Fixture f) => f.HomeClubId == clubId ? f.HomeGoals!.Value : f.AwayGoals!.Value;
        int Against(Fixture f) => f.HomeClubId == clubId ? f.AwayGoals!.Value : f.HomeGoals!.Value;
        var points = played.Sum(f => For(f) > Against(f) ? 3 : For(f) == Against(f) ? 1 : 0);

        PressPending? press = null;
        if (played.FirstOrDefault() is { } last && !await db.PressConferences.AnyAsync(p => p.FixtureId == last.Id && p.ClubId == clubId, ct))
            press = new(last.Id, names.GetValueOrDefault(last.HomeClubId == clubId ? last.AwayClubId : last.HomeClubId, "?"), For(last), Against(last));

        var squad = await new ConditionService(db, clock).SquadAsync(clubId, ct);
        var key = await RoundKeyAsync(seasonId, ct);
        var talked = (await db.PlayerTalks.Where(t => t.ClubId == clubId && t.RoundKey == key).Select(t => t.PlayerId).ToListAsync(ct)).ToHashSet();
        return new(await InboxAsync(seasonId, clubId, names, squad, press, ct), press, MediaRules.FanMood(points, played.Count), points, played.Count, squad, talked);
    }

    async Task<List<InboxItem>> InboxAsync(Guid seasonId, Guid clubId, Dictionary<Guid, string> names, List<ConditionRow> squad, PressPending? press, CancellationToken ct)
    {
        var items = new List<InboxItem>();
        var playerNames = await db.Players.Where(p => db.TransferOffers.Any(o => o.PlayerId == p.Id && (o.BuyerClubId == clubId || o.SellerClubId == clubId)))
            .ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        var offers = await db.TransferOffers.AsNoTracking().Where(o => (o.SellerClubId == clubId && o.Status == OfferStatus.Pending)
            || (o.BuyerClubId == clubId && o.Status == OfferStatus.Countered)).ToListAsync(ct);
        foreach (var o in offers)
            items.Add(o.SellerClubId == clubId
                ? new(InboxKind.Task, $"Đề nghị mua {playerNames.GetValueOrDefault(o.PlayerId)}", $"{names.GetValueOrDefault(o.BuyerClubId)} trả {o.Fee:N0}. Đồng ý, từ chối hoặc trả giá.", "de-nghi")
                : new(InboxKind.Task, $"Bị trả giá {playerNames.GetValueOrDefault(o.PlayerId)}", $"{names.GetValueOrDefault(o.SellerClubId)} đòi {o.CounterFee:N0}.", "de-nghi"));
        if (press is not null)
            items.Add(new(InboxKind.Task, "Họp báo sau trận", $"Phóng viên chờ bạn sau trận {press.GoalsFor}–{press.GoalsAgainst} với {press.Opponent}.", null));

        var next = await (from f in db.Fixtures join m in db.Matchdays on f.MatchdayId equals m.Id
                          where m.SeasonId == seasonId && f.Status == FixtureStatus.Scheduled && (f.HomeClubId == clubId || f.AwayClubId == clubId)
                          orderby m.Number select new { f.Id, f.HomeClubId, f.AwayClubId, m.Number }).FirstOrDefaultAsync(ct);
        if (next is not null)
            items.Add(new(InboxKind.Task, $"Chuẩn bị vòng {next.Number}", $"Gặp {names.GetValueOrDefault(next.HomeClubId == clubId ? next.AwayClubId : next.HomeClubId)} ({(next.HomeClubId == clubId ? "sân nhà" : "sân khách")}). Kiểm tra đội hình, chiến thuật.", $"chuan-bi/{next.Id}"));
        if (!await db.ClubTrainings.AnyAsync(t => t.ClubId == clubId, ct))
            items.Add(new(InboxKind.Task, "Chưa đặt giáo án", "Ban huấn luyện đang tập Cân bằng/Vừa theo mặc định.", "tap-luyen"));

        var injured = squad.Where(s => s.InjuredRounds > 0).ToList();
        if (injured.Count > 0)
            items.Add(new(InboxKind.Warning, $"{injured.Count} cầu thủ chấn thương", string.Join(", ", injured.Select(s => $"{s.Name} ({s.Injury}, {s.InjuredRounds} vòng)")), "tap-luyen"));
        var tired = squad.Where(s => s.InjuredRounds == 0 && s.Fatigue > ConditionRules.TiredAt).ToList();
        if (tired.Count > 0)
            items.Add(new(InboxKind.Warning, $"{tired.Count} cầu thủ quá mệt", "Nên cho nghỉ: " + string.Join(", ", tired.Select(s => s.Name)), null));
        var unhappy = squad.Where(s => s.Morale < 35).ToList();
        if (unhappy.Count > 0)
            items.Add(new(InboxKind.Warning, $"{unhappy.Count} cầu thủ không vui", "Nói chuyện để vực tinh thần: " + string.Join(", ", unhappy.Select(s => s.Name)), null));

        var expiring = await (from k in db.Contracts join p in db.Players on k.PlayerId equals p.Id
                              where k.ClubId == clubId && k.Status == ContractStatus.Active && k.MatchesTotal != null && k.MatchesTotal - k.MatchesPlayed <= 3
                              select new { p.Name, Left = k.MatchesTotal!.Value - k.MatchesPlayed }).ToListAsync(ct);
        if (expiring.Count > 0)
            items.Add(new(InboxKind.Warning, $"{expiring.Count} hợp đồng sắp hết", string.Join(", ", expiring.Select(e => $"{e.Name} ({e.Left} trận)")), "chuyen-nhuong"));
        var renewals = await db.ContractRenewals.CountAsync(r => r.ClubId == clubId && r.Status == RenewalStatus.Pending, ct);
        if (renewals > 0) items.Add(new(InboxKind.Info, $"{renewals} yêu cầu gia hạn đang chờ", "Gia hạn được xử lý khi chốt.", null));

        if (await new BoardService(db, clock).StatusAsync(seasonId, clubId, ct) is { } board)
            items.Add(new(board.Confidence < BoardRules.ReviewBelow ? InboxKind.Warning : InboxKind.Info, $"Ban lãnh đạo: {board.Verdict}",
                $"Mục tiêu {board.Objective} ({board.League}), hiện hạng {board.Position?.ToString() ?? "—"}/{board.Teams}. Độ tin cậy {board.Confidence}/100.", "ban-lanh-dao"));
        var report = await new FinanceService(db, clock).ReportAsync(seasonId, clubId, ct);
        if (report.Forecast < 0)
            items.Add(new(InboxKind.Warning, "Ban tài chính cảnh báo", $"Dự báo cuối mùa âm {Math.Abs(report.Forecast):N0}. Giảm lương hoặc tăng doanh thu.", "tai-chinh"));
        else if (report.Rounds.FirstOrDefault() is { } r)
            items.Add(new(InboxKind.Info, $"Báo cáo tài chính vòng {r.Matchday}", $"Thu chi ròng {r.Net:N0}. Số dư {report.Balance:N0}.", "tai-chinh"));

        var article = await (from a in db.MatchArticles join f in db.Fixtures on a.FixtureId equals f.Id
                             where f.HomeClubId == clubId || f.AwayClubId == clubId
                             orderby a.PublishedAt descending select new { a.Id, a.Title }).FirstOrDefaultAsync(ct);
        if (article is not null) items.Add(new(InboxKind.Info, "Báo chí viết về bạn", article.Title, $"tin/{article.Id}"));
        return items;
    }

    /// <summary>Họp báo trận gần nhất: tinh thần cả squad + CĐV. Trả về lỗi hoặc null.</summary>
    public async Task<string?> PressAsync(Guid seasonId, Guid clubId, Guid fixtureId, PressTone tone, CancellationToken ct = default)
    {
        var f = await db.Fixtures.AsNoTracking().SingleOrDefaultAsync(x => x.Id == fixtureId && (x.HomeClubId == clubId || x.AwayClubId == clubId), ct);
        if (f?.HomeGoals is null || f.AwayGoals is null) return "Trận chưa có kết quả.";
        if (await db.PressConferences.AnyAsync(p => p.FixtureId == fixtureId && p.ClubId == clubId, ct)) return "Đã họp báo trận này.";
        var diff = (f.HomeGoals.Value - f.AwayGoals.Value) * (f.HomeClubId == clubId ? 1 : -1);
        var (morale, fans) = MediaRules.Press(tone, diff);
        if (await CoachHooks.ForClubAsync(db, clubId, ct) is { } hlv)   // HLV career: Con người (tinh thần), Truyền thông (CĐV)
        {
            if (morale > 0) morale = (int)Math.Round(morale * CoachRules.PeopleFactor(CoachRules.Effective(hlv, CoachSkill.People)), MidpointRounding.AwayFromZero);
            if (fans > 0) fans = (int)Math.Round(fans * CoachRules.MediaFactor(CoachRules.Effective(hlv, CoachSkill.Media), hlv.Trait), MidpointRounding.AwayFromZero);
        }
        var ids = await db.SquadMemberships.Where(m => m.ClubId == clubId && m.LeftAt == null).Select(m => m.PlayerId).ToListAsync(ct);
        var cond = await db.PlayerConditions.Where(c => ids.Contains(c.PlayerId)).ToDictionaryAsync(c => c.PlayerId, ct);
        var now = clock.GetUtcNow();
        foreach (var id in ids)
        {
            if (!cond.TryGetValue(id, out var c)) db.PlayerConditions.Add(c = new PlayerCondition { PlayerId = id });
            c.Morale = Math.Clamp(c.Morale + morale, 0, 100);
            c.UpdatedAt = now;
        }
        if ((await new FinanceService(db, clock).EnsureAsync(seasonId, ct)).GetValueOrDefault(clubId) is { } fin)
            fin.Fans = Math.Clamp(fin.Fans + fin.Fans * fans / 1000, 1000, fin.Capacity * 2);
        db.PressConferences.Add(new PressConference { FixtureId = fixtureId, ClubId = clubId, Tone = tone, MoraleDelta = morale, At = now });
        await db.SaveChangesAsync(ct);
        return null;
    }

    /// <summary>Nói chuyện riêng: mỗi cầu thủ 1 lần/vòng. Trả về (lỗi, độ thay đổi tinh thần).</summary>
    public async Task<(string? Error, int Delta)> TalkAsync(Guid seasonId, Guid clubId, Guid playerId, TalkKind kind, CancellationToken ct = default)
    {
        if (!await db.SquadMemberships.AnyAsync(m => m.ClubId == clubId && m.PlayerId == playerId && m.LeftAt == null, ct)) return ("Cầu thủ không thuộc CLB.", 0);
        var key = await RoundKeyAsync(seasonId, ct);
        if (await db.PlayerTalks.AnyAsync(t => t.PlayerId == playerId && t.RoundKey == key, ct)) return ("Đã nói chuyện với cầu thủ này trong vòng này.", 0);
        var rating = await (from r in db.MatchRatings join f in db.Fixtures on r.FixtureId equals f.Id
                            where r.PlayerId == playerId orderby f.PlayedAt descending select r.Rating).FirstOrDefaultAsync(ct);
        var c = await db.PlayerConditions.FindAsync([playerId], ct);
        if (c is null) db.PlayerConditions.Add(c = new PlayerCondition { PlayerId = playerId });
        var delta = MediaRules.Talk(kind, rating, c.Morale);
        if (delta > 0 && await CoachHooks.ForClubAsync(db, clubId, ct) is { } hlv)   // HLV career: Con người
            delta = (int)Math.Round(delta * CoachRules.PeopleFactor(CoachRules.Effective(hlv, CoachSkill.People)), MidpointRounding.AwayFromZero);
        c.Morale = Math.Clamp(c.Morale + delta, 0, 100);
        c.UpdatedAt = clock.GetUtcNow();
        db.PlayerTalks.Add(new PlayerTalk { PlayerId = playerId, ClubId = clubId, RoundKey = key, Kind = kind, MoraleDelta = delta, At = c.UpdatedAt });
        await db.SaveChangesAsync(ct);
        return (null, delta);
    }

    /// <summary>Vòng chưa chốt sớm nhất của mùa (hết vòng → Guid.Empty).</summary>
    async Task<Guid> RoundKeyAsync(Guid seasonId, CancellationToken ct) =>
        await db.Matchdays.Where(m => m.SeasonId == seasonId && m.Status != MatchdayStatus.Settled).OrderBy(m => m.Number).Select(m => m.Id).FirstOrDefaultAsync(ct);
}
