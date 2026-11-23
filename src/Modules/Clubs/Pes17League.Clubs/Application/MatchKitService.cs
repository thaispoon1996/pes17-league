using System.Text.Json;

namespace Pes17League.Clubs.Application;

/// <summary>Áo đấu của trận (PO 23/11): bộ áo cầu thủ (1st/2nd/3rd) + áo thủ môn (gk) mỗi bên; null = để game tự chọn.</summary>
public sealed record MatchKits(string? Home = null, string? HomeGk = null, string? Away = null, string? AwayGk = null);

/// <summary>
/// Lựa chọn áo của trận ở kho file <c>match-kits/{trận}.json</c> (trang Chuẩn bị ghi; bộ chạy trận tools/match17 đọc lúc tới màn
/// Kickoff, vào menu UNIFORM của game chọn đúng bộ đó — không cần mở lại game).
/// </summary>
public sealed class MatchKitService(IOptionFileStore store)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static string PathOf(Guid fixtureId) => $"match-kits/{fixtureId:N}.json";

    public async Task<MatchKits> GetAsync(Guid fixtureId, CancellationToken ct = default)
    {
        try { return JsonSerializer.Deserialize<MatchKits>(await store.ReadAsync(PathOf(fixtureId), ct), Json) ?? new(); }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException or JsonException) { return new(); }
    }

    public Task SaveAsync(Guid fixtureId, MatchKits kits, CancellationToken ct = default) =>
        store.SaveAsync(PathOf(fixtureId), JsonSerializer.SerializeToUtf8Bytes(kits, Json), ct);
}
