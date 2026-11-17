using System.Net;
using System.Text;
using System.Text.Json;
using Pes17League.Clubs.Application;
using Pes17League.Clubs.Infrastructure;

namespace Pes17League.Clubs.Tests;

public class MatchPressTests
{
    static MatchBrief Brief(int home = 1, int away = 0, IReadOnlyList<BriefGoal>? goals = null, IReadOnlyList<BriefPlayer>? players = null,
        BriefSide? homeSide = null, int? homePens = null, int? awayPens = null) =>
        new("Arsenal", "Chelsea", home, away, homePens, awayPens, "Ngoại hạng", 3, goals ?? [new(12, "Arsenal", "Saka")], players ?? [], homeSide, null);

    [Fact]
    public void Template_uses_only_the_score_and_the_scorer()
    {
        var goals = new BriefGoal[] { new(12, "Arsenal", "Saka"), new(60, "Chelsea", "Jackson"), new(88, "Arsenal", "Saka") };
        var (title, body) = MatchReportText.Compose(Brief(2, 1, goals));
        Assert.Contains($"Arsenal {MatchReportText.Dash(2, 1)} Chelsea", title);
        Assert.Contains("Saka", title);
        Assert.Contains("cú đúp", title);
        Assert.Contains("phút 88, Saka (Arsenal).", body);
        Assert.DoesNotContain("không rõ người ghi", body);
        Assert.DoesNotContain("sút xa", body);
        Assert.True(MatchReportText.Accepts(Brief(2, 1, goals), title, body));
    }

    [Fact]
    public void Goalless_draw_names_nobody()
    {
        var brief = Brief(0, 0, []);
        var (title, body) = MatchReportText.Compose(brief);
        Assert.Contains($"Arsenal {MatchReportText.Dash(0, 0)} Chelsea", title);
        Assert.Contains("chia điểm", title);
        Assert.Contains("Trận không có bàn thắng.", body);
        Assert.DoesNotContain("Saka", body);
        Assert.True(MatchReportText.Accepts(brief, title, body));
    }

    [Fact]
    public void Rejects_an_invented_shot_or_minute()
    {
        var brief = Brief();
        var (title, body) = MatchReportText.Compose(brief);
        Assert.False(MatchReportText.Accepts(brief, title, body + " Saka sút xa."));
        Assert.False(MatchReportText.Accepts(brief, title, $"Arsenal tiếp Chelsea và kết thúc {brief.Score} nhờ bàn ở phút 99."));
        Assert.True(MatchReportText.Accepts(brief, title, body));
    }

    [Fact]
    public void Picks_opener_go_ahead_and_last_goal()
    {
        var goals = new[] { G(10, true), G(20, false), G(30, false), G(70, true) };
        var picked = MatchPress.Pick(goals, g => g.Club == "Arsenal").Select(g => g.Minute).ToList();
        Assert.Equal([10, 30, 70], picked);
    }

    [Fact]
    public async Task Without_a_key_the_writer_does_not_call_the_network()
    {
        var http = new HttpClient(new FixedHandler(_ => throw new InvalidOperationException("không được gọi")));
        var draft = await new DeepSeekArticleWriter(http, null).WriteAsync(Brief(0, 0, []), default);
        Assert.False(draft.ByAi);
        Assert.Contains(MatchReportText.Dash(0, 0), draft.Title);
    }

    [Fact]
    public async Task Keeps_model_text_that_stays_with_the_facts()
    {
        var brief = Brief();
        var score = MatchReportText.Dash(1, 0);
        var title = $"Arsenal {score} Chelsea: Saka ghi bàn duy nhất";
        var body = $"Ngoại hạng, vòng 3: Arsenal tiếp Chelsea. Trận đấu kết thúc {score}. Saka ghi bàn ở phút 12.";
        var http = new HttpClient(new FixedHandler(req =>
        {
            Assert.Equal("test-key", req.Headers.Authorization?.Parameter);
            return Json(title, body);
        }));
        var draft = await new DeepSeekArticleWriter(http, "test-key").WriteAsync(brief, default);
        Assert.True(draft.ByAi);
        Assert.Equal(title, draft.Title);
    }

    [Fact]
    public async Task Falls_back_when_the_model_invents_a_long_shot()
    {
        var brief = Brief();
        var (_, body) = MatchReportText.Compose(brief);
        var http = new HttpClient(new FixedHandler(_ => Json($"Arsenal {MatchReportText.Dash(1, 0)} Chelsea", body + " Đó là một cú sút xa.")));
        var draft = await new DeepSeekArticleWriter(http, "test-key").WriteAsync(brief, default);
        Assert.False(draft.ByAi);
        Assert.Equal(body, draft.Body);
    }

    static BriefGoal G(int minute, bool home) => new(minute, home ? "Arsenal" : "Chelsea", "X");

    static HttpResponseMessage Json(string title, string body)
    {
        var inner = JsonSerializer.Serialize(new { title, body });
        var outer = JsonSerializer.Serialize(new { choices = new[] { new { message = new { content = inner } } } });
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(outer, Encoding.UTF8, "application/json") };
    }

    sealed class FixedHandler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(reply(request));
    }
}
