using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Domain;
using Pes17League.Clubs.Infrastructure;

namespace Pes17League.Clubs.Application;

public sealed record RealValueRow(Guid PlayerId, long Value, string? Ref);

public sealed record RealValueImportResult(int Set, int Cleared, int UnknownPlayers, int NoAttributes);

/// <summary>
/// Nhập giá thật (PO 22/11, phương án C; nguồn tools/tmvalues/match.py): thay toàn bộ <see cref="FootballPlayer.RealValue"/> bằng file mới,
/// ghi kèm tuổi + chỉ số tổng hiện tại (mùa mới nhất có chỉ số) để giá về sau đi theo tuổi/chỉ số. Giá chỉ đổi từ mùa
/// <see cref="PriceModelOptions.MarketFromSeason"/> (lúc bắt đầu mùa tính lại giá); mùa đang đá giữ giá cũ.
/// </summary>
public sealed class RealValueService(ClubsDbContext db, TimeProvider clock)
{
    public async Task<RealValueImportResult> ImportAsync(IReadOnlyList<RealValueRow> rows, string actor, bool dry = false, CancellationToken ct = default)
    {
        var seasonId = await db.PlayerAttributes.Join(db.Seasons, a => a.SeasonId, s => s.Id, (a, s) => s)
            .OrderByDescending(s => s.Number).Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct)
            ?? throw new AdminRuleException("Chưa có chỉ số cầu thủ mùa nào");
        var current = await new EconomyService(db, clock).CurrentAttributesAsync(seasonId, null, ct);
        var players = await db.Players.ToDictionaryAsync(p => p.Id, ct);
        var model = new PriceModel();
        var wanted = rows.Where(r => r.Value > 0).GroupBy(r => r.PlayerId).ToDictionary(g => g.Key, g => g.First());
        int set = 0, cleared = 0, noAttr = 0;
        foreach (var p in players.Values)
        {
            if (wanted.TryGetValue(p.Id, out var r) && current.TryGetValue(p.Id, out var values))
            {
                p.RealValue = r.Value;
                p.RealValueAge = values.GetValueOrDefault("AGE", 27);
                p.RealValueOverall = model.Overall(p.Position, values);
                p.RealValueRef = r.Ref is { Length: > 40 } s ? s[..40] : r.Ref;
                set++;
                continue;
            }
            if (wanted.ContainsKey(p.Id)) noAttr++;
            if (p.RealValue is null) continue;
            p.RealValue = null; p.RealValueAge = null; p.RealValueOverall = null; p.RealValueRef = null;
            cleared++;
        }
        var result = new RealValueImportResult(set, cleared, wanted.Keys.Count(id => !players.ContainsKey(id)), noAttr);
        if (dry) return result;
        db.AdminAudit.Add(new AdminAuditEntry
        {
            At = clock.GetUtcNow(), Actor = actor, EntityType = nameof(FootballPlayer), EntityId = seasonId, Action = "ImportRealValues",
            After = System.Text.Json.JsonSerializer.Serialize(result),
        });
        await db.SaveChangesAsync(ct);
        return result;
    }
}
