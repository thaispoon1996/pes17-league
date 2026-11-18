using System.Globalization;
using System.Text.RegularExpressions;

namespace Pes17League.Clubs.Application;

/// <param name="Round">Số khung/vòng trên lịch.</param>
public sealed record MatchBrief(string Home, string Away, int HomeGoals, int AwayGoals, int? HomePenalties, int? AwayPenalties,
    string? Competition, int Round, IReadOnlyList<BriefGoal> Goals, IReadOnlyList<BriefPlayer> Players, BriefSide? HomeSide, BriefSide? AwaySide)
{
    public string Score => MatchReportText.Dash(HomeGoals, AwayGoals);
}

public sealed record BriefGoal(int Minute, string Club, string? Scorer);
public sealed record BriefPlayer(string Name, string Club, decimal Rating, int? Goals);
public sealed record BriefSide(int PossessionPct, int Shots, int ShotsOnTarget, int Corners);

public sealed record ArticleDraft(string Title, string Body, bool ByAi);

/// <summary>Viết bài từ <see cref="MatchBrief"/>. Bản AI có thể bọc bản mẫu.</summary>
public interface IArticleWriter
{
    Task<ArticleDraft> WriteAsync(MatchBrief brief, CancellationToken ct);
}

/// <summary>Bài mẫu, không gọi mạng. Chỉ nhắc sự kiện có trong <see cref="MatchBrief"/>.</summary>
public sealed class TemplateArticleWriter : IArticleWriter
{
    public Task<ArticleDraft> WriteAsync(MatchBrief brief, CancellationToken ct)
    {
        var (title, body) = MatchReportText.Compose(brief);
        return Task.FromResult(new ArticleDraft(title, body, false));
    }
}

/// <summary>Dựng bài và chặn bài AI bịa tình huống (phút, kiểu sút, thẻ…) không có trong số liệu.</summary>
public static partial class MatchReportText
{
    public static string Dash(int home, int away) => $"{home}\u2013{away}";

    public static (string Title, string Body) Compose(MatchBrief b)
    {
        var head = $"{b.Home} {b.Score} {b.Away}";
        var pen = b.HomePenalties is { } ph && b.AwayPenalties is { } pa ? $" (pen {Dash(ph, pa)})" : "";
        var title = head + pen + Clause(b);
        if (title.Length > 180) title = head + pen;
        if (title.Length > 180) title = title[..180];

        var parts = new List<string> { Lead(b) };
        parts.Add(GoalsStory(b));
        if (PlayersStory(b) is { } players) parts.Add(players);
        if (StatsStory(b) is { } stats) parts.Add(stats);
        parts.Add(Closing(b));
        return (title, string.Join("\n\n", parts));
    }

    /// <summary>Sự kiện đưa cho model. Không thêm diễn giải.</summary>
    public static string Facts(MatchBrief b)
    {
        var lines = new List<string>
        {
            $"Giải: {b.Competition ?? "không rõ"}",
            $"Vòng: {b.Round}",
            $"Tỷ số: {b.Home} {b.Score} {b.Away}",
        };
        if (b.HomePenalties is { } h && b.AwayPenalties is { } a) lines.Add($"Loạt penalty: {b.Home} {Dash(h, a)} {b.Away}");
        if (b.Goals.Count == 0) lines.Add("Bàn thắng: không có");
        else lines.AddRange(b.Goals.Select(g => $"Bàn: phút {g.Minute}, {g.Scorer ?? "không rõ người ghi"}, đội {g.Club}"));
        if (b.Players.Count == 0) lines.Add("Điểm cầu thủ: không có");
        else lines.AddRange(b.Players.Select(p => $"Điểm: {p.Name} ({p.Club}) {p.Rating.ToString("0.0", CultureInfo.InvariantCulture)}" + (p.Goals is { } n ? $", {n} bàn" : "")));
        if (b.HomeSide is { } hs && b.AwaySide is { } aw)
        {
            lines.Add($"Giữ bóng %: {b.Home} {hs.PossessionPct}, {b.Away} {aw.PossessionPct}");
            lines.Add($"Sút (trúng đích): {b.Home} {hs.Shots} ({hs.ShotsOnTarget}), {b.Away} {aw.Shots} ({aw.ShotsOnTarget})");
            lines.Add($"Phạt góc: {b.Home} {hs.Corners}, {b.Away} {aw.Corners}");
        }
        else lines.Add("Thống kê đội: không có");
        return string.Join("\n", lines);
    }

    /// <summary>Bài AI được đăng khi còn đúng tỷ số, đúng phút, và không thêm kiểu bàn / thẻ / phỏng vấn.</summary>
    public static bool Accepts(MatchBrief b, string? title, string? body)
    {
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(body)) return false;
        title = title.Trim();
        body = body.Trim();
        if (title.Length > 180 || body.Length is < 40 or > 3500) return false;
        var text = title + "\n" + body;
        if (!title.Contains($"{b.Home} {b.Score} {b.Away}", StringComparison.Ordinal)) return false;
        if (!body.Contains(b.Score, StringComparison.Ordinal)) return false;
        foreach (var ban in new[] { "phạt đền", "phản lưới", "sút xa", "đánh đầu", "kiến tạo", "phỏng vấn", "thẻ vàng", "thẻ đỏ" })
            if (text.Contains(ban, StringComparison.OrdinalIgnoreCase)) return false;
        if (b.Goals.Count == 0 && text.Contains("phút ", StringComparison.Ordinal)) return false;
        if (b.Goals.Count > 0 && text.Contains("không có bàn", StringComparison.OrdinalIgnoreCase)) return false;
        if ((b.HomeGoals != b.AwayGoals || b.HomePenalties is not null) && text.Contains("chia điểm", StringComparison.OrdinalIgnoreCase)) return false;
        var hat = b.Goals.Where(g => g.Scorer is not null).GroupBy(g => g.Scorer).Select(g => g.Count()).DefaultIfEmpty(0).Max();
        if (hat < 3 && text.Contains("hat-trick", StringComparison.OrdinalIgnoreCase)) return false;
        if (hat < 2 && text.Contains("cú đúp", StringComparison.OrdinalIgnoreCase)) return false;
        if (b.HomeSide is null && (text.Contains("giữ bóng", StringComparison.OrdinalIgnoreCase) || text.Contains("phạt góc", StringComparison.OrdinalIgnoreCase))) return false;
        if (b.Players.Count == 0 && text.Contains("điểm cao", StringComparison.OrdinalIgnoreCase)) return false;
        foreach (Match m in MinuteSource().Matches(text))
        {
            var raw = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
            if (!b.Goals.Any(g => g.Minute == int.Parse(raw, CultureInfo.InvariantCulture))) return false;
        }
        return true;
    }

    static string Where(MatchBrief b) => b.Competition is { } c ? $"{c}, vòng {b.Round}" : $"Vòng {b.Round}";

    static string Winner(MatchBrief b) => b.HomeGoals > b.AwayGoals ? b.Home : b.Away;
    static string Loser(MatchBrief b) => b.HomeGoals > b.AwayGoals ? b.Away : b.Home;
    static int Margin(MatchBrief b) => Math.Abs(b.HomeGoals - b.AwayGoals);

    static string Clause(MatchBrief b)
    {
        var top = b.Goals.Where(g => g.Scorer is not null).GroupBy(g => g.Scorer!).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).FirstOrDefault();
        if (top?.Count() >= 3) return $": {top.Key} lập hat-trick";
        if (top?.Count() == 2) return $": {top.Key} lập cú đúp";
        if (b.HomePenalties is { } ph && b.AwayPenalties is { } pa)
            return ph > pa ? $": {b.Home} thắng loạt penalty" : $": {b.Away} thắng loạt penalty";
        if (b.Goals.Count == 1 && b.Goals[0].Scorer is { } s) return $": {s} ghi bàn duy nhất";
        if (b.HomeGoals == b.AwayGoals) return ": hai đội chia điểm";
        var m = Margin(b);
        if (m >= 3) return $": {Winner(b)} hủy diệt";
        if (m == 1 && b.Goals.Count > 0 && b.Goals[^1].Minute >= 80) return $": {Winner(b)} thắng sát nút";
        if (m == 1) return $": {Winner(b)} thắng sát nút";
        return $": {Winner(b)} thắng thuyết phục";
    }

    static string Lead(MatchBrief b)
    {
        var where = Where(b);
        if (b.HomePenalties is { } h && b.AwayPenalties is { } a)
        {
            var penWinner = h > a ? b.Home : b.Away;
            return $"{where}: {b.Home} và {b.Away} không phân thắng bại sau 120 phút ({b.Score}). Loạt penalty căng như dây đàn — {penWinner} thắng {Dash(h, a)}, niềm vui vỡ òa.";
        }
        if (b.HomeGoals == b.AwayGoals && b.Goals.Count == 0)
            return $"{where}: {b.Home} tiếp {b.Away} trong một buổi chiều khó chịu. Hai đội chia điểm {b.Score} — không bàn thắng, cũng chẳng có tiếng thở phào.";
        if (b.HomeGoals == b.AwayGoals)
            return $"{where}: {b.Home} và {b.Away} đá nhau tới {b.Score}. Hai đội chia điểm sau trận đầy cảm xúc, chẳng bên nào chịu nhường.";
        var m = Margin(b);
        if (m >= 3)
            return $"{where}: {Winner(b)} đè bẹp {Loser(b)} với tỷ số {b.Score}. Một buổi chiều chỉ còn tiếng reo của một phía sân.";
        if (m == 1)
            return $"{where}: {b.Home} tiếp {b.Away} và kết thúc {b.Score}. {Winner(b)} thắng sát nút — nhịp thở cả trận đều nghẹt.";
        return $"{where}: {b.Home} tiếp {b.Away}. Trận đấu kết thúc {b.Score}; {Winner(b)} thắng thuyết phục, {Loser(b)} còn nhiều điều phải nghĩ lại.";
    }

    static string GoalsStory(MatchBrief b)
    {
        if (b.Goals.Count == 0) return "Trận không có bàn thắng. Hai hàng công đều bất lực, khung thành gần như vô sự suốt 90 phút.";
        if (b.Goals.Count == 1)
        {
            var g = b.Goals[0];
            var who = g.Scorer ?? "không rõ người ghi";
            return $"Bàn duy nhất đến ở phút {g.Minute}: {who} ({g.Club}) mở khóa trận đấu. Một khoảnh khắc đủ để định đoạt cả cuộc chơi.";
        }

        var lines = new List<string>();
        for (var i = 0; i < b.Goals.Count; i++)
        {
            var g = b.Goals[i];
            var who = g.Scorer ?? "không rõ người ghi";
            var bit = $"phút {g.Minute}, {who} ({g.Club})";
            if (i == 0) lines.Add($"Mở tỷ số: {bit}.");
            else if (i == b.Goals.Count - 1 && g.Minute >= 80) lines.Add($"Phút cuối đầy cảm xúc: {bit}.");
            else lines.Add($"Tiếp theo: {bit}.");
        }

        var top = b.Goals.Where(g => g.Scorer is not null).GroupBy(g => g.Scorer!).OrderByDescending(g => g.Count()).FirstOrDefault();
        var tail = top?.Count() >= 3 ? $" {top.Key} lập hat-trick — đêm thuộc về một người."
            : top?.Count() == 2 ? $" {top.Key} lập cú đúp, dấu ấn khó quên."
            : "";
        return "Các bàn thắng nối tiếp nhau. " + string.Join(" ", lines) + tail;
    }

    static string? PlayersStory(MatchBrief b)
    {
        if (b.Players.FirstOrDefault() is not { } top) return null;
        var line = $"{top.Name} ({top.Club}) có điểm cao nhất trận, {top.Rating.ToString("0.0", CultureInfo.InvariantCulture)} — xứng đáng là ngôi sao đêm nay.";
        if (top.Goals is > 0) line += $" Ghi {top.Goals} bàn.";
        if (b.Players.Count > 1)
            line += " Theo sát phía sau: " + string.Join(", ", b.Players.Skip(1).Select(p => $"{p.Name} {p.Rating.ToString("0.0", CultureInfo.InvariantCulture)}")) + ".";
        return line;
    }

    static string? StatsStory(MatchBrief b)
    {
        if (b.HomeSide is not { } hs || b.AwaySide is not { } aw) return null;
        var tone = hs.Shots + aw.Shots >= 20 ? "Trận đấu mở và giàu cơ hội."
            : hs.PossessionPct >= aw.PossessionPct + 15 ? $"{b.Home} nắm thế trận rõ rệt."
            : aw.PossessionPct >= hs.PossessionPct + 15 ? $"{b.Away} kiểm soát bóng tốt hơn."
            : "Hai đội chia sẻ thế trận khá cân bằng.";
        return $"{tone} {b.Home} giữ bóng {hs.PossessionPct}%, sút {hs.Shots} ({hs.ShotsOnTarget} trúng đích), {hs.Corners} phạt góc. "
               + $"{b.Away} giữ bóng {aw.PossessionPct}%, sút {aw.Shots} ({aw.ShotsOnTarget} trúng đích), {aw.Corners} phạt góc.";
    }

    static string Closing(MatchBrief b)
    {
        if (b.HomePenalties is not null)
            return "Sau đêm penalty, cảm xúc vỡ òa một bên, nuối tiếc nặng trĩu bên còn lại — bóng đá đôi khi chỉ cách nhau một cú đá.";
        if (b.HomeGoals == b.AwayGoals)
            return "Một điểm mỗi đội. Họ sẽ mang theo cả hụt hẫng lẫn sự thở phào vào vòng kế tiếp.";
        if (Margin(b) >= 3)
            return $"{Winner(b)} ra về trong tiếng reo; {Loser(b)} còn nguyên vết đau của một đêm dài.";
        if (Margin(b) == 1)
            return $"Chiến thắng sát nút của {Winner(b)} đủ làm CĐV vỡ òa. {Loser(b)} chỉ thiếu một khoảnh khắc — và đó là cả sự khác biệt.";
        return $"{Winner(b)} giữ trọn ba điểm. Bài học cho {Loser(b)}: ở cấp độ này, lơi lỏng một nhịp là đủ trả giá.";
    }

    [GeneratedRegex(@"(?:phút\s+(\d+)|(?<!\d)(\d+)')")]
    private static partial Regex MinuteSource();
}
