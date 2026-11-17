namespace Pes17League.OptionFile;

public sealed class OptionFileValidationException(IReadOnlyList<ValidationIssue> issues)
    : Exception($"OF không hợp lệ: {issues.Count} lỗi, ví dụ: {issues.FirstOrDefault()?.Message}")
{
    public IReadOnlyList<ValidationIssue> Issues { get; } = issues;
}

/// <summary>
/// Sinh OF cho một vòng: OF gốc của mùa + squad lấy từ DB (DB là nguồn sự thật, OF chỉ là đầu ra).
/// Chỉ ghi squad (+ sơ đồ/thiết lập đội nếu CLB đã chọn) của các CLB trong giải, chỉ số đã tiến hoá (C19); kiểm hợp lệ, lỗi thì không xuất.
/// <paramref name="names"/>: tên cầu thủ admin đã sửa (PES ID → tên, ≤ <see cref="OptionFileImage.MaxNameLength"/> sau khi bỏ dấu).
/// <paramref name="shirtNames"/>: tên in áo (PES ID → ASCII), hiện chỉ cho cầu thủ tạo mới.
/// </summary>
public static class OptionFileBuilder
{
    public static byte[] Build(ReadOnlySpan<byte> baseOptionFile, IReadOnlyCollection<ClubSquad> leagueSquads, SquadRulesOptions? options = null,
        IReadOnlyDictionary<int, TeamTactics>? tactics = null, IReadOnlyDictionary<int, IReadOnlyDictionary<PlayerField, int>>? attributes = null,
        IReadOnlyDictionary<int, string>? names = null, IReadOnlyDictionary<int, string>? shirtNames = null)
    {
        var image = OptionFileCodec.Decode(baseOptionFile);
        foreach (var (player, values) in attributes ?? new Dictionary<int, IReadOnlyDictionary<PlayerField, int>>())
            foreach (var (f, v) in values) image.Set(player, f, v);
        foreach (var (player, name) in names ?? new Dictionary<int, string>()) image.SetPlayerName(player, name);
        foreach (var (player, name) in shirtNames ?? new Dictionary<int, string>()) image.SetShirtName(player, name);
        foreach (var s in leagueSquads) image.SetSquad(s);
        foreach (var (club, t) in tactics ?? new Dictionary<int, TeamTactics>()) image.SetTactics(club, t);
        var issues = OptionFileValidator.Validate(image, leagueSquads.Select(s => s.Club), options);
        if (issues.Count > 0) throw new OptionFileValidationException(issues);
        return OptionFileCodec.Encode(image);
    }
}
