using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Infrastructure;

namespace Pes17League.Clubs.Application;

public sealed record SanctionView(Sanction S, string Club, string? Manager, string? Edition);

/// <summary>Thưởng / phạt của Ban tổ chức (PO duyệt 17/11, docs/domain-sanctions-design.md). Fine/Bonus → bút toán sổ CLB (huỷ = bút toán đảo);
/// PointsDeduction → BXH trừ điểm khi tính; Ban → N trận Scheduled tới của CLB giao trợ lý đá thay (không tính vắng, HLV không huỷ được);
/// Warning → ghi nhận. Mọi quyết định: thư hệ thống tới HLV + nhật ký admin; công khai ở /ky-luat. Không có mức trần (PO).</summary>
public sealed class SanctionService(ClubsDbContext db, TimeProvider clock)
{
    public async Task<Sanction> IssueAsync(SanctionKind kind, Guid clubId, long amount, string reason, string actor,
        Guid? editionId = null, Guid? refFixtureId = null, CancellationToken ct = default)
    {
        reason = reason.Trim();
        if (reason.Length < 3) throw new AdminRuleException("Cần ghi lý do (công khai)");
        if (kind == SanctionKind.Warning) amount = 0;
        else if (amount <= 0) throw new AdminRuleException(kind switch
        {
            SanctionKind.Ban => "Số trận cấm phải > 0", SanctionKind.PointsDeduction => "Số điểm trừ phải > 0", _ => "Số tiền phải > 0",
        });
        var club = await db.Clubs.SingleOrDefaultAsync(c => c.Id == clubId, ct) ?? throw new AdminRuleException("Không có CLB");
        var season = await db.Seasons.Where(s => s.Status == SeasonStatus.Active).OrderByDescending(s => s.Number).FirstOrDefaultAsync(ct)
            ?? throw new AdminRuleException("Chưa có mùa đang diễn ra");
        if (editionId is not null && !await db.EditionClubs.AnyAsync(x => x.EditionId == editionId && x.ClubId == clubId, ct))
            throw new AdminRuleException("CLB không đá giải này");
        var manager = await db.TeamAssignments.Where(a => a.ClubId == clubId && a.To == null && a.ControllerType == ControllerType.Human && a.ParticipantId != null)
            .Select(a => a.ParticipantId).FirstOrDefaultAsync(ct);
        if (kind == SanctionKind.Ban && manager is null) throw new AdminRuleException("CLB không có HLV người để cấm chỉ đạo");

        var now = clock.GetUtcNow();
        var s = new Sanction
        {
            Kind = kind, SeasonId = season.Id, ClubId = clubId, ManagerId = manager, EditionId = kind == SanctionKind.PointsDeduction ? editionId : null,
            Amount = amount, Reason = reason, RefFixtureId = refFixtureId, At = now, Actor = actor,
        };
        switch (kind)
        {
            case SanctionKind.Fine: Book(s, -amount); break;
            case SanctionKind.Bonus: Book(s, amount); break;
            case SanctionKind.Ban:
                var next = await (from f in db.Fixtures join m in db.Matchdays on f.MatchdayId equals m.Id
                                  where m.SeasonId == season.Id && f.Status == FixtureStatus.Scheduled && (f.HomeClubId == clubId || f.AwayClubId == clubId)
                                  orderby m.Number select f).Take((int)Math.Min(amount, 100)).ToListAsync(ct);
                if (next.Count == 0) throw new AdminRuleException("CLB không còn trận nào chưa đá trong mùa");
                foreach (var f in next) f.AssistantPlays = true;
                s.BannedFixtureIds = string.Join(',', next.Select(f => f.Id));
                break;
        }
        db.Sanctions.Add(s);
        Audit(s, "Issue", actor);
        await db.SaveChangesAsync(ct);
        await new MailService(db, clock).SystemAsync(manager, $"Ban tổ chức: {SanctionRules.Name(kind)} — {club.Name}",
            $"{Describe(s)}\nLý do: {reason}\nKhiếu nại: trả lời thư này (mẫu \"Khiếu nại BTC\") trong {SanctionRules.AppealWindow.TotalHours:0} giờ.", fixtureId: refFixtureId, ct: ct);
        return s;
    }

    public async Task RevokeAsync(Guid id, string reason, string actor, CancellationToken ct = default)
    {
        var s = await db.Sanctions.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new AdminRuleException("Không có quyết định");
        if (!s.Active) throw new AdminRuleException("Quyết định đã huỷ");
        s.RevokedAt = clock.GetUtcNow();
        s.RevokeReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        switch (s.Kind)
        {
            case SanctionKind.Fine: Book(s, s.Amount, "SanctionRevoke"); break;
            case SanctionKind.Bonus: Book(s, -s.Amount, "SanctionRevoke"); break;
            case SanctionKind.Ban:   // trận chưa đá trả lại HLV tự đá
                var ids = s.BannedFixtures.ToList();
                foreach (var f in await db.Fixtures.Where(f => ids.Contains(f.Id) && f.Status == FixtureStatus.Scheduled && !f.HomeAbsent && !f.AwayAbsent).ToListAsync(ct))
                    f.AssistantPlays = false;
                break;
        }
        Audit(s, "Revoke", actor);
        await db.SaveChangesAsync(ct);
        var club = await db.Clubs.Where(c => c.Id == s.ClubId).Select(c => c.Name).FirstOrDefaultAsync(ct);
        await new MailService(db, clock).SystemAsync(s.ManagerId, $"Ban tổ chức huỷ quyết định: {SanctionRules.Name(s.Kind)} — {club}",
            $"Đã huỷ: {Describe(s)}{(s.RevokeReason is null ? "" : $"\nLý do huỷ: {s.RevokeReason}")}", ct: ct);
    }

    /// <summary>Quyết định của mùa (mới nhất trước); null = mùa đang diễn ra.</summary>
    public async Task<IReadOnlyList<SanctionView>> ListAsync(Guid? seasonId = null, CancellationToken ct = default)
    {
        seasonId ??= await db.Seasons.Where(s => s.Status == SeasonStatus.Active).OrderByDescending(s => s.Number).Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct);
        if (seasonId is null) return [];
        return await (from s in db.Sanctions
                      where s.SeasonId == seasonId
                      join c in db.Clubs on s.ClubId equals c.Id
                      join m in db.Managers on s.ManagerId equals m.Id into ms from m in ms.DefaultIfEmpty()
                      join e in db.Editions on s.EditionId equals e.Id into es from e in es.DefaultIfEmpty()
                      join comp in db.Competitions on e.CompetitionId equals comp.Id into cs from comp in cs.DefaultIfEmpty()
                      orderby s.At descending
                      select new SanctionView(s, c.Name, m == null ? null : m.DisplayName, comp == null ? null : comp.Name)).ToListAsync(ct);
    }

    /// <summary>Điểm bị trừ còn hiệu lực theo (giải, CLB); giải null = BXH mùa.</summary>
    public static async Task<Dictionary<(Guid? Edition, Guid Club), int>> DeductionsAsync(ClubsDbContext db, CancellationToken ct = default) =>
        (await db.Sanctions.Where(s => s.Kind == SanctionKind.PointsDeduction && s.RevokedAt == null && s.ClubId != null)
            .Select(s => new { s.EditionId, Club = s.ClubId!.Value, s.Amount }).ToListAsync(ct))
        .GroupBy(x => (x.EditionId, x.Club)).ToDictionary(g => g.Key, g => (int)g.Sum(x => x.Amount));

    public static IReadOnlyDictionary<Guid, int> For(Dictionary<(Guid? Edition, Guid Club), int> all, Guid? edition) =>
        all.Where(x => x.Key.Edition == edition).ToDictionary(x => x.Key.Club, x => x.Value);

    /// <summary>Trận bị cấm chỉ đạo (Ban còn hiệu lực) — HLV không tự đá / không huỷ trợ lý.</summary>
    public static async Task<bool> IsBannedAsync(ClubsDbContext db, Guid fixtureId, CancellationToken ct = default)
    {
        var key = fixtureId.ToString();
        return await db.Sanctions.AnyAsync(s => s.Kind == SanctionKind.Ban && s.RevokedAt == null && s.BannedFixtureIds!.Contains(key), ct);
    }

    public static string Describe(Sanction s) => s.Kind switch
    {
        SanctionKind.Fine => $"Phạt {s.Amount:N0} tiền",
        SanctionKind.Bonus => $"Thưởng {s.Amount:N0} tiền",
        SanctionKind.PointsDeduction => $"Trừ {s.Amount} điểm BXH",
        SanctionKind.Ban => $"Cấm chỉ đạo {s.BannedFixtures.Count} trận tới (trợ lý đá thay)",
        _ => "Cảnh cáo",
    };

    private void Book(Sanction s, long amount, string refType = "Sanction") =>
        db.Ledger.Add(new ClubLedgerEntry { ClubId = s.ClubId!.Value, Amount = amount, Reason = LedgerReason.Sanction, RefType = refType, RefId = s.Id, At = clock.GetUtcNow() });

    private void Audit(Sanction s, string action, string actor) =>
        db.AdminAudit.Add(new AdminAuditEntry { At = clock.GetUtcNow(), Actor = actor, EntityType = nameof(Sanction), EntityId = s.Id, Action = action,
            After = JsonSerializer.Serialize(new { s.Kind, s.ClubId, s.ManagerId, s.EditionId, s.Amount, s.Reason, s.RevokedAt, s.RevokeReason }) });
}
