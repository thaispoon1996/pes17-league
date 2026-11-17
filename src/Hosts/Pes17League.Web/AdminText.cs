using System.Text.Json;
using Pes17League.Clubs.Application;
using Pes17League.Clubs.Domain;

namespace Pes17League.Web;

/// <summary>Chữ hiển thị trang admin (bước 3, docs/domain-admin-design.md mục 5).</summary>
public static class AdminText
{
    /// <summary>Chỉ PO là admin, một mật khẩu (PO chốt 05/11) → nhật ký ghi "admin".</summary>
    public const string Actor = "admin";

    public static string Status(EditionStatus s) => s switch
    {
        EditionStatus.Draft => "Nháp", EditionStatus.Scheduled => "Đã xếp lịch", EditionStatus.Active => "Đang đá", EditionStatus.Finished => "Đã xong",
        _ => s.ToString(),
    };

    public static string Entity(string type) => type switch
    {
        nameof(Competition) => "Giải", nameof(CompetitionEdition) => "Mùa giải", nameof(Season) => "Mùa", nameof(Club) => "CLB", "Player" => "Cầu thủ",
        nameof(Fixture) => "Trận",
        _ => type,
    };

    public static string Action(string action) => action switch
    {
        "Create" => "Tạo", "Update" => "Sửa", "Delete" => "Xoá", "Archive" => "Lưu trữ", "Restore" => "Khôi phục",
        "AddClub" => "Thêm CLB", "SetSeed" => "Đổi hạt giống", "RemoveClub" => "Bỏ CLB",
        "FederationSetup" => "Dựng liên đoàn", "SetImportedContractLength" => "Đổi số trận hợp đồng nhập",
        "Retire" => "Giải nghệ", "SetAttributes" => "Sửa chỉ số", "SetVisibility" => "Ẩn/hiện thị trường",
        "SetPenalties" => "Nhập tỷ số penalty", ClubAdminService.SetControllerAction => "Đổi CLB của tôi (Human/BOT)",
        SeasonScheduleService.ScheduleAction => "Ghi lịch mùa", "StartSeason" => "Bắt đầu mùa", SeasonScheduleService.DrawAction => "Bốc thăm vòng loại trực tiếp",
        EconomyAdminService.SetAction => "Đổi tham số kinh tế", EconomyAdminService.TopUpAction => "Bù ngân sách đầu mùa",
        _ => action,
    };

    /// <summary>Nhãn khung ngày theo lịch PO 06/11: QN3, LP2, PO-1, 1/8-2, TK-1, BK-2, CK.</summary>
    public static string Slot(CalendarSlot s) => s.Stage switch
    {
        null => $"QN{s.Round}", FixtureStage.LeaguePhase => $"LP{s.Round}", FixtureStage.KnockoutPlayoff => $"PO-{s.Leg}",
        FixtureStage.RoundOf64 => s.Leg is { } l64 ? $"1/32-{l64}" : "1/32", FixtureStage.RoundOf32 => s.Leg is { } l32 ? $"1/16-{l32}" : "1/16",
        FixtureStage.RoundOf16 => s.Leg is { } l16 ? $"1/8-{l16}" : "1/8",
        FixtureStage.QuarterFinal => s.Leg is { } lq ? $"TK-{lq}" : "TK", FixtureStage.SemiFinal => s.Leg is { } ls ? $"BK-{ls}" : "BK",
        FixtureStage.Final => "CK", _ => $"{s.Stage}-{s.Leg}",
    };

    public static string Stage(FixtureStage s) => s switch
    {
        FixtureStage.League => "Quốc nội", FixtureStage.LeaguePhase => "League phase", FixtureStage.KnockoutPlayoff => "Play-off",
        FixtureStage.RoundOf64 => "Vòng 1/32", FixtureStage.RoundOf32 => "Vòng 1/16", FixtureStage.RoundOf16 => "Vòng 1/8",
        FixtureStage.QuarterFinal => "Tứ kết", FixtureStage.SemiFinal => "Bán kết", FixtureStage.Final => "Chung kết", _ => s.ToString(),
    };

    /// <summary>Loại khung ngày từ Matchday.Stage/Leg: "Quốc nội", "League phase", "Tứ kết lượt 1", "Chung kết".</summary>
    public static string SlotKind(FixtureStage? stage, int? leg) => stage is { } s ? Stage(s) + (leg is { } l ? $" lượt {l}" : "") : Stage(FixtureStage.League);

    public static string Error(CompetitionError e) => e switch
    {
        CompetitionError.SeasonNotFound => "Không tìm thấy mùa", CompetitionError.SeasonNotPlanned => "Mùa đã bắt đầu hoặc đã kết thúc",
        CompetitionError.SeasonNotScheduled => "Mùa chưa ghi lịch (hoặc còn giải chưa xếp lịch)",
        CompetitionError.AnotherSeasonActive => "Đang có mùa khác chưa kết thúc: chỉ chạy 1 mùa một lúc",
        _ => e.ToString(),
    };

    /// <summary>Thay đổi giữa 2 ảnh chụp JSON của nhật ký, mỗi dòng "khoá: trước → sau" (chỉ có sau = tạo, chỉ có trước = xoá).</summary>
    public static IEnumerable<string> Diff(string? before, string? after, Func<string, string> label)
    {
        var (b, a) = (Flatten(before), Flatten(after));
        foreach (var k in b.Keys.Union(a.Keys))
        {
            var (x, y) = (b.GetValueOrDefault(k), a.GetValueOrDefault(k));
            if (x == y) continue;
            string V(string? v) => v is null ? "—" : label(v);
            yield return before is null ? $"{k}: {V(y)}" : after is null ? $"{k}: {V(x)}" : $"{k}: {V(x)} → {V(y)}";
        }
    }

    public static string? JsonName(string? json)
    {
        if (json is null) return null;
        using var d = JsonDocument.Parse(json);
        return d.RootElement.ValueKind == JsonValueKind.Object && d.RootElement.TryGetProperty("Name", out var n) && n.ValueKind == JsonValueKind.String
            ? n.GetString() : null;
    }

    static Dictionary<string, string?> Flatten(string? json)
    {
        var d = new Dictionary<string, string?>();
        if (json is null) return d;
        using var doc = JsonDocument.Parse(json);
        void Walk(JsonElement e, string path)
        {
            switch (e.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var p in e.EnumerateObject()) Walk(p.Value, path == "" ? p.Name : $"{path}.{p.Name}");
                    break;
                case JsonValueKind.Array:
                    var i = 0;
                    foreach (var x in e.EnumerateArray()) Walk(x, $"{path}[{i++}]");
                    break;
                case JsonValueKind.Null: d[path] = null; break;
                case JsonValueKind.String: d[path] = e.GetString(); break;
                default: d[path] = e.GetRawText(); break;
            }
        }
        Walk(doc.RootElement, "");
        return d;
    }

    public static string Tier(Competition c) => c.Type switch
    {
        CompetitionType.ContinentalCup => $"C{c.Tier}",
        CompetitionType.DomesticCup => "cúp",
        _ => $"{c.Tier}",
    };

    /// <summary>Tóm tắt luật một dòng; cúp kèm vòng loại trực tiếp đầu tiên và số suất (quốc nội + CLB lẻ + suất thêm theo OVR).</summary>
    public static string Rules(Competition c, IReadOnlyList<Competition> all)
    {
        var r = c.Rules;
        string Name(Guid? id) => all.FirstOrDefault(x => x.Id == id)?.Name ?? "?";
        if (c.Type == CompetitionType.DomesticLeague)
        {
            List<string> parts = [$"{r.Legs} lượt"];
            if (r.PromotionPlaces > 0) parts.Add($"lên {r.PromotionPlaces} → {Name(c.PromotesToId)}");
            if (r.RelegationPlaces > 0) parts.Add($"xuống {r.RelegationPlaces} → {Name(c.RelegatesToId)}");
            if (r.ContinentalPlaces > 0) parts.Add($"C1 {r.ContinentalPlaces}");
            if (r.SecondaryContinentalPlaces > 0) parts.Add($"C2 {r.SecondaryContinentalPlaces}");
            return string.Join(" · ", parts);
        }
        if (c.Type == CompetitionType.DomesticCup)
            return CompetitionAdminService.Validate(c.Type, r).Count > 0 ? "Luật chưa hợp lệ" : "Knockout 1 lượt · hạng 1+2 cùng nước";
        if (CompetitionAdminService.Validate(c.Type, r).Count > 0) return "Luật chưa hợp lệ";
        var (domestic, loose) = CompetitionAdminService.CupEntrants(c, all);
        var extra = Math.Max(0, r.LeaguePhaseTeams!.Value - domestic - loose);
        return $"{Cup(r)} · suất: quốc nội {domestic} + CLB lẻ {loose}" + (extra > 0 ? $" + {extra} theo OVR" : "");
    }

    /// <summary>"24 đội × 8 trận · 1–4 vào thẳng · 5–12 play-off · từ tứ kết (21 trận loại trực tiếp)"; luật đã hợp lệ.</summary>
    public static string Cup(CompetitionRules r)
    {
        var (d, p) = (r.DirectKnockoutPlaces!.Value, r.PlayoffPlaces!.Value);
        var stage = CompetitionAdminService.KnockoutStageName(CompetitionAdminService.KnockoutTeams(r)!.Value).ToLowerInvariant();
        return $"{r.LeaguePhaseTeams} đội × {r.LeaguePhaseMatches} trận" + (d > 0 ? $" · 1–{d} vào thẳng" : "") + (p > d ? $" · {d + 1}–{p} play-off" : "")
            + $" · từ {stage} ({CompetitionAdminService.KnockoutMatches(r)} trận loại trực tiếp)";
    }
}
