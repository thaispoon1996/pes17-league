using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Infrastructure;

namespace Pes17League.Clubs.Application;

/// <summary>
/// Đội hình riêng của 1 trận (PO 23/11: "tách riêng màn hình đội chính và màn hình chỉnh đội hình trước khi vào trận").
/// Trang CLB → Chiến thuật sửa đội hình chính (DB như cũ). Trang Chuẩn bị trận sửa bản nháp của trận, lưu ở kho file
/// <c>match-lineups/{trận}-{CLB}.json</c>, không đụng đội hình chính. Chưa sửa gì = dùng đội hình chính.
/// Cách làm: mở giao dịch, chép bản nháp đè lên dòng DB của CLB, chạy đúng dịch vụ cũ (LineupService/TacticsService, xuất đội hình PES17),
/// chụp lại kết quả rồi HUỶ giao dịch — đội hình chính không đổi, mọi luật cũ (ô thủ môn, vai…) vẫn áp.
/// </summary>
public sealed class MatchLineupService(IOptionFileStore store)
{
    /// <summary>Ô của từng cầu thủ (SlotOrder: 0–10 đá chính) + chiến thuật (bản sao ClubTactics; null = như game).</summary>
    public sealed record Draft(Dictionary<Guid, int> Slots, ClubTactics? Tactics);

    static string PathOf(Guid fixtureId, Guid clubId) => $"match-lineups/{fixtureId:N}-{clubId:N}.json";

    public async Task<Draft?> GetAsync(Guid fixtureId, Guid clubId, CancellationToken ct = default)
    {
        try { return JsonSerializer.Deserialize<Draft>(await store.ReadAsync(PathOf(fixtureId, clubId), ct)); }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException or JsonException) { return null; }
    }

    public Task SaveAsync(Guid fixtureId, Guid clubId, Draft? d, CancellationToken ct = default) =>
        store.SaveAsync(PathOf(fixtureId, clubId), JsonSerializer.SerializeToUtf8Bytes(d), ct);

    /// <summary>Bỏ bản nháp → trận dùng lại đội hình chính.</summary>
    public Task ResetAsync(Guid fixtureId, Guid clubId, CancellationToken ct = default) => SaveAsync(fixtureId, clubId, null, ct);

    public static async Task<Draft> CaptureAsync(ClubsDbContext db, Guid clubId, CancellationToken ct = default)
    {
        var slots = await db.SquadMemberships.Where(m => m.ClubId == clubId && m.LeftAt == null).ToDictionaryAsync(m => m.PlayerId, m => m.SlotOrder, ct);
        var t = await db.Tactics.AsNoTracking().FirstOrDefaultAsync(x => x.ClubId == clubId, ct);
        return new(slots, t);
    }

    /// <summary>Chép bản nháp lên DB (gọi trong giao dịch sẽ huỷ). Cầu thủ mới về sau khi nháp → cuối dự bị; người đã đi → bỏ.</summary>
    public static async Task ApplyAsync(ClubsDbContext db, Guid clubId, Draft d, CancellationToken ct = default)
    {
        var ms = await db.SquadMemberships.Where(m => m.ClubId == clubId && m.LeftAt == null).ToListAsync(ct);
        // Chỉ mục (CLB, ô) duy nhất chỉ tính người chưa rời + ô 0–31 → tạm đánh dấu rời đội rồi xếp lại (như LineupService).
        foreach (var m in ms) m.LeftAt = m.JoinedAt;
        await db.SaveChangesAsync(ct);
        var used = new HashSet<int>();
        var rest = new List<SquadMembership>();
        foreach (var m in ms.OrderBy(m => d.Slots.TryGetValue(m.PlayerId, out var s) ? s : int.MaxValue))
            if (d.Slots.TryGetValue(m.PlayerId, out var s) && s is >= 0 and <= 31 && used.Add(s)) m.SlotOrder = s;
            else rest.Add(m);
        var free = Enumerable.Range(11, 21).Concat(Enumerable.Range(0, 11)).Where(x => !used.Contains(x)).GetEnumerator();
        foreach (var m in rest) m.SlotOrder = free.MoveNext() ? free.Current : m.SlotOrder;
        foreach (var m in ms) m.LeftAt = null;
        await db.SaveChangesAsync(ct);
        var row = await db.Tactics.FirstOrDefaultAsync(x => x.ClubId == clubId, ct);
        if (d.Tactics is null) { if (row is not null) db.Tactics.Remove(row); }
        else if (row is null) db.Tactics.Add(JsonSerializer.Deserialize<ClubTactics>(JsonSerializer.Serialize(d.Tactics))!);
        else db.Entry(row).CurrentValues.SetValues(d.Tactics);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Chạy <paramref name="op"/> trên đội hình của trận (bản nháp, chưa có thì đội hình chính) rồi huỷ mọi thay đổi trên DB.
    /// <paramref name="save"/> = lưu kết quả thành bản nháp mới (sửa); false = chỉ đọc.
    /// </summary>
    public async Task<T> EditAsync<T>(ClubsDbContext db, Guid fixtureId, Guid clubId, Func<ClubsDbContext, Task<T>> op, bool save, CancellationToken ct = default)
    {
        var d = await GetAsync(fixtureId, clubId, ct);
        if (d is null && !save) return await op(db);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            if (d is not null) await ApplyAsync(db, clubId, d, ct);
            var r = await op(db);
            if (save) await SaveAsync(fixtureId, clubId, await CaptureAsync(db, clubId, ct), ct);
            return r;
        }
        finally
        {
            await tx.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
        }
    }

    /// <summary>Chép bản nháp của các CLB trong trận lên DB (gọi trong giao dịch sẽ huỷ). Trả true nếu có bản nháp nào.</summary>
    public async Task<bool> OverlayAsync(ClubsDbContext db, Guid fixtureId, IEnumerable<Guid> clubIds, CancellationToken ct = default)
    {
        var any = false;
        foreach (var c in clubIds)
            if (await GetAsync(fixtureId, c, ct) is { } d)
            {
                await ApplyAsync(db, c, d, ct);
                any = true;
            }
        return any;
    }
}
