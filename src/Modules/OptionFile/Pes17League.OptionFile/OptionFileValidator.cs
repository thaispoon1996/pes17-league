namespace Pes17League.OptionFile;

public sealed record ValidationIssue(int Club, string Code, string Message, int? PlayerId = null);

/// <summary>Kiểm bất biến trước khi xuất OF (docs/of-module-design.md mục 5). Có lỗi thì không xuất file.</summary>
public static class OptionFileValidator
{
    public static IReadOnlyList<ValidationIssue> Validate(OptionFileImage image, IEnumerable<int> clubs, SquadRulesOptions? options = null)
    {
        var opt = options ?? new();
        var rules = new SquadRules(image, opt);
        var issues = new List<ValidationIssue>();
        var owner = new Dictionary<int, int>();
        foreach (var c in clubs)
        {
            var squad = image.GetSquad(c);
            if (image.SquadHasGaps(c)) issues.Add(new(c, "SQUAD_GAP", "Có slot trống giữa squad"));
            if (squad.Players.Count < ClubSquad.Starters) issues.Add(new(c, "TOO_FEW_PLAYERS", $"Chỉ có {squad.Players.Count} cầu thủ"));
            if (rules.Goalkeepers(squad) < opt.MinGoalkeepers) issues.Add(new(c, "TOO_FEW_GK", $"Dưới {opt.MinGoalkeepers} thủ môn"));
            var numbers = new HashSet<int>();
            foreach (var p in squad.Players)
            {
                if (!OptionFileImage.IsValidPlayerId(p.PlayerId)) { issues.Add(new(c, "BAD_PLAYER_ID", $"ID {p.PlayerId} không hợp lệ", p.PlayerId)); continue; }
                if (owner.TryGetValue(p.PlayerId, out var o))
                    issues.Add(new(c, o == c ? "DUPLICATE_PLAYER" : "PLAYER_IN_TWO_CLUBS", $"Cầu thủ {p.PlayerId} đã có ở CLB {o}", p.PlayerId));
                else owner[p.PlayerId] = c;
                if (p.ShirtNumber is < 1 or > 99) issues.Add(new(c, "BAD_SHIRT_NUMBER", $"Số áo {p.ShirtNumber}", p.PlayerId));
                else if (!numbers.Add(p.ShirtNumber)) issues.Add(new(c, "DUPLICATE_SHIRT_NUMBER", $"Trùng số áo {p.ShirtNumber}", p.PlayerId));
                if (image.GetPlayerName(p.PlayerId).Length > OptionFileImage.MaxNameLength)
                    issues.Add(new(c, "NAME_TOO_LONG", "Tên quá 15 ký tự", p.PlayerId));
            }
        }
        return issues;
    }
}
