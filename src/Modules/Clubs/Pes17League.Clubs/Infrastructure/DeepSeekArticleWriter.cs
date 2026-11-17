using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Pes17League.Clubs.Application;

namespace Pes17League.Clubs.Infrastructure;

/// <summary>
/// Viết bài qua API kiểu OpenAI (mặc định DeepSeek). Không có khóa, lỗi mạng, hoặc bài bịa tình huống → bài mẫu.
/// </summary>
public sealed class DeepSeekArticleWriter(HttpClient http, string? apiKey, string model = "deepseek-flash", string? endpoint = null) : IArticleWriter
{
    public const string DefaultEndpoint = "https://api.deepseek.com/chat/completions";

    public async Task<ArticleDraft> WriteAsync(MatchBrief brief, CancellationToken ct)
    {
        var fallback = MatchReportText.Compose(brief);
        if (string.IsNullOrWhiteSpace(apiKey)) return new ArticleDraft(fallback.Title, fallback.Body, false);
        try
        {
            var prompt = "Bạn là phóng viên trang tin PES League. Viết tiếng Việt, 3 đến 5 đoạn ngắn.\n"
                + "Chỉ dùng sự kiện trong phần SỰ KIỆN. Không thêm cầu thủ, phút, thẻ, phạt đền, phản lưới, sút xa, đánh đầu, kiến tạo, phỏng vấn.\n"
                + "Nếu không có người ghi bàn, viết \"không rõ người ghi\". Nếu không có thống kê đội, đừng nhắc giữ bóng hay phạt góc.\n"
                + "Trả về đúng JSON, không markdown: {\"title\":\"...\",\"body\":\"...\"}\n"
                + $"title phải chứa \"{brief.Home} {brief.Score} {brief.Away}\" (dấu –).\n"
                + $"body các đoạn cách nhau bằng một dòng trống và phải chứa \"{brief.Score}\".\n\nSỰ KIỆN:\n{MatchReportText.Facts(brief)}";
            using var req = new HttpRequestMessage(HttpMethod.Post, endpoint ?? DefaultEndpoint);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            req.Content = new StringContent(JsonSerializer.Serialize(new
            {
                model,
                temperature = 0.3,
                messages = new[] { new { role = "user", content = prompt } },
            }), Encoding.UTF8, "application/json");
            using var resp = await http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return new ArticleDraft(fallback.Title, fallback.Body, false);
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var content = Unwrap(doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString());
            using var article = JsonDocument.Parse(content);
            var title = article.RootElement.GetProperty("title").GetString();
            var body = article.RootElement.GetProperty("body").GetString()?.Replace("\r\n", "\n");
            return MatchReportText.Accepts(brief, title, body)
                ? new ArticleDraft(title!.Trim(), body!.Trim(), true)
                : new ArticleDraft(fallback.Title, fallback.Body, false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return new ArticleDraft(fallback.Title, fallback.Body, false);
        }
    }

    private static string Unwrap(string? s)
    {
        s = (s ?? "").Trim();
        if (!s.StartsWith("```", StringComparison.Ordinal)) return s;
        var nl = s.IndexOf('\n');
        if (nl >= 0) s = s[(nl + 1)..];
        if (s.EndsWith("```", StringComparison.Ordinal)) s = s[..^3];
        return s.Trim();
    }
}
