using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Domain;
using Pes17League.Clubs.Infrastructure;

namespace Pes17League.Clubs.Application;

/// <summary>Một khung hình cần cắt từ bản ghi. <see cref="FallbackSec"/> dùng khi mốc <see cref="AtSec"/> nằm sau hết video.</summary>
public sealed record FrameGrab(string DestPath, double AtSec, double? FallbackSec);

/// <summary>Cắt ảnh jpg từ bản ghi trận. Không ném khi một khung thất bại — file không có tức là bỏ ảnh đó.</summary>
public interface IFrameGrabber
{
    Task GrabAsync(string recordingPath, IReadOnlyList<FrameGrab> frames, CancellationToken ct);
}

/// <summary>Đăng bài sau khi trận đã ghi kết quả. Trả lỗi (không ném). Đã có bài → null.</summary>
public interface IMatchPress
{
    Task<string?> PublishAsync(Guid fixtureId, string? recordingPath, CancellationToken ct);
}

/// <summary>
/// Sau trận: chọn tối đa 3 bàn có mốc video, cắt ảnh, viết bài từ số liệu, lưu. Không có video vẫn đăng bài.
/// Lỗi ảnh không chặn bài. Lỗi viết/ghi trả về chuỗi, không làm đá lại trận.
/// </summary>
public sealed class MatchPress(Func<ClubsDbContext> dbFactory, IArticleWriter writer, IFrameGrabber frames, string photoDir, TimeProvider clock) : IMatchPress
{
    public const double AfterGoalSec = 3;
    /// <summary>Trận không có bàn (hoặc bàn không có mốc video): 1 ảnh giữa hiệp 1; video ngắn thì lùi về mốc dự phòng.</summary>
    public const double SceneSec = 240, SceneFallbackSec = 60;

    public async Task<string?> PublishAsync(Guid fixtureId, string? recordingPath, CancellationToken ct)
    {
        try
        {
            await using var db = dbFactory();
            if (await db.MatchArticles.AnyAsync(a => a.FixtureId == fixtureId, ct)) return null;
            var f = await db.Fixtures.AsNoTracking().SingleOrDefaultAsync(x => x.Id == fixtureId, ct);
            if (f is not { Status: FixtureStatus.Played, HomeGoals: { } hg, AwayGoals: { } ag }) return "trận chưa có kết quả";

            var clubs = await db.Clubs.Where(c => c.Id == f.HomeClubId || c.Id == f.AwayClubId).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
            var goals = await db.Goals.AsNoTracking().Where(g => g.FixtureId == fixtureId).OrderBy(g => g.Half).ThenBy(g => g.Minute).ThenBy(g => g.VideoOffsetSec).ToListAsync(ct);
            var ratings = await db.MatchRatings.AsNoTracking().Where(r => r.FixtureId == fixtureId && r.Rating != null)
                .OrderByDescending(r => r.Rating).Take(5).ToListAsync(ct);
            var ids = goals.Select(g => g.ScorerPlayerId).OfType<Guid>().Concat(ratings.Select(r => r.PlayerId)).Distinct().ToList();
            var names = await db.Players.Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, ct);
            var stats = await db.MatchTeamStats.AsNoTracking().Where(t => t.FixtureId == fixtureId).ToListAsync(ct);
            var round = await db.Matchdays.Where(m => m.Id == f.MatchdayId).Select(m => m.Number).SingleAsync(ct);
            string? competition = null;
            if (f.CompetitionEditionId is { } eid)
                competition = await (from e in db.Editions join c in db.Competitions on e.CompetitionId equals c.Id where e.Id == eid select c.Name).SingleOrDefaultAsync(ct);

            string? Who(Guid? id) => id is { } x && names.TryGetValue(x, out var n) ? n : null;
            BriefSide? Side(bool home)
            {
                var s = stats.SingleOrDefault(t => t.IsHome == home);
                return s is null ? null : new BriefSide(s.PossessionPct, s.Shots, s.ShotsOnTarget, s.Corners);
            }
            var brief = new MatchBrief(clubs[f.HomeClubId], clubs[f.AwayClubId], hg, ag, f.HomePenalties, f.AwayPenalties, competition, round,
                [.. goals.Select(g => new BriefGoal(g.Minute, clubs[g.ClubId], Who(g.ScorerPlayerId)))],
                [.. ratings.Select(r => new BriefPlayer(names.GetValueOrDefault(r.PlayerId) ?? "?", clubs[r.ClubId], r.Rating!.Value, r.Goals))],
                Side(true), Side(false));

            var photos = await PhotosAsync(f, goals, clubs, Who, recordingPath, ct);
            var draft = await writer.WriteAsync(brief, ct);
            if (string.IsNullOrWhiteSpace(draft.Title) || string.IsNullOrWhiteSpace(draft.Body)) return "không viết được bài";
            var article = new MatchArticle { FixtureId = fixtureId, Title = draft.Title.Trim(), Body = draft.Body.Trim(), ByAi = draft.ByAi, PublishedAt = clock.GetUtcNow() };
            db.MatchArticles.Add(article);
            foreach (var p in photos) { p.ArticleId = article.Id; db.MatchPhotos.Add(p); }
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException e) when (Duplicate(e)) { return null; }
            return null;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return (e.InnerException ?? e).Message;
        }
    }

    private async Task<List<MatchPhoto>> PhotosAsync(Fixture f, List<Goal> goals, Dictionary<Guid, string> clubs, Func<Guid?, string?> who, string? recordingPath, CancellationToken ct)
    {
        var timed = goals.Where(g => g.VideoOffsetSec is not null).ToList();
        var picked = Pick(timed, g => g.ClubId == f.HomeClubId);
        if (recordingPath is null || !File.Exists(recordingPath)) return [];
        if (picked.Count == 0) return await SceneAsync(f, clubs, recordingPath, ct);
        var planned = new List<(Goal Goal, int Home, int Away, int Sort, string Dest)>();
        var grabs = new List<FrameGrab>();
        int h = 0, a = 0, sort = 0;
        foreach (var g in goals)
        {
            if (g.ClubId == f.HomeClubId) h++; else a++;
            if (!picked.Contains(g)) continue;
            sort++;
            var dest = Path.Combine(photoDir, f.Id.ToString("N"), $"{sort}.jpg");
            planned.Add((g, h, a, sort, dest));
            grabs.Add(new FrameGrab(dest, g.VideoOffsetSec!.Value + AfterGoalSec, g.VideoOffsetSec));
        }
        try { await frames.GrabAsync(recordingPath, grabs, ct); }
        catch (Exception e) when (e is not OperationCanceledException) { return []; }
        var list = new List<MatchPhoto>();
        foreach (var (g, gh, ga, s, dest) in planned)
        {
            if (!File.Exists(dest)) continue;
            var scorer = who(g.ScorerPlayerId) ?? "Không rõ người ghi";
            var caption = $"{g.Minute}' {scorer} — {clubs[f.HomeClubId]} {gh}–{ga} {clubs[f.AwayClubId]}";
            list.Add(new MatchPhoto
            {
                FileName = $"{f.Id:N}/{s}.jpg",
                Caption = caption.Length <= 200 ? caption : caption[..200],
                Sort = s,
            });
        }
        return list;
    }

    private async Task<List<MatchPhoto>> SceneAsync(Fixture f, Dictionary<Guid, string> clubs, string recordingPath, CancellationToken ct)
    {
        var dest = Path.Combine(photoDir, f.Id.ToString("N"), "1.jpg");
        try { await frames.GrabAsync(recordingPath, [new FrameGrab(dest, SceneSec, SceneFallbackSec)], ct); }
        catch (Exception e) when (e is not OperationCanceledException) { return []; }
        if (!File.Exists(dest)) return [];
        var caption = $"{clubs[f.HomeClubId]} {f.HomeGoals}–{f.AwayGoals} {clubs[f.AwayClubId]}";
        return [new MatchPhoto { FileName = $"{f.Id:N}/1.jpg", Caption = caption.Length <= 200 ? caption : caption[..200], Sort = 1 }];
    }

    /// <summary>Tối đa 3 bàn, theo thứ tự phút: bàn đầu, bàn lập thế dẫn cuối cùng (hoặc bàn gỡ hoà cuối nếu hoà), bàn cuối.</summary>
    public static List<T> Pick<T>(IReadOnlyList<T> ordered, Func<T, bool> isHome) where T : class
    {
        if (ordered.Count <= 3) return [.. ordered];
        T? decisive = null;
        int h = 0, a = 0, lead = 0;
        foreach (var g in ordered)
        {
            if (isHome(g)) h++; else a++;
            var now = h.CompareTo(a);
            if (now == 0 || now != lead) { decisive = g; lead = now; }
        }
        var chosen = new List<T> { ordered[0], ordered[^1] };
        var third = decisive is not null && !chosen.Contains(decisive) ? decisive : ordered[ordered.Count / 2];
        if (!chosen.Contains(third)) chosen.Add(third);
        var index = ordered.ToList();
        return [.. chosen.OrderBy(g => index.IndexOf(g))];
    }

    private static bool Duplicate(DbUpdateException e) =>
        (e.InnerException?.Message ?? e.Message).Contains("23505", StringComparison.Ordinal)
        || (e.InnerException?.Message ?? e.Message).Contains("duplicate", StringComparison.OrdinalIgnoreCase);
}
