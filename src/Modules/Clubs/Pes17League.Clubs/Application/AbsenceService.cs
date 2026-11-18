using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Infrastructure;

namespace Pes17League.Clubs.Application;

public sealed record DeadlineReport(int Assisted, IReadOnlyList<string> Absent, IReadOnlyList<string> Warned, IReadOnlyList<string> Released);

/// <summary>Hạn đá vòng + vắng mặt (PO 17/11). Quá hạn: trận còn chờ HLV người → trợ lý đá thay (kết quả tính); HLV chưa đá, chưa giao trợ lý,
/// chưa Sẵn sàng (trận người vs người) = vắng. Vắng <see cref="AbsenceRules.WarnAt"/> vòng liên tiếp → thư cảnh cáo;
/// <see cref="AbsenceRules.ReleaseAt"/> vòng → CLB về BOT, báo Ban tổ chức + người đang chờ trong danh sách đăng ký đá giải.
/// Đá trận / giao trợ lý / Sẵn sàng → chuỗi vắng về 0 (<see cref="ResetAsync"/>, gọi khi ghi kết quả).</summary>
public sealed class AbsenceService(ClubsDbContext db, TimeProvider clock)
{
    public async Task<DeadlineReport> ApplyDeadlineAsync(Guid matchdayId, CancellationToken ct = default)
    {
        var fixtures = await db.Fixtures.Where(f => f.MatchdayId == matchdayId && f.Status == FixtureStatus.Scheduled && !f.AssistantPlays).ToListAsync(ct);
        var clubs = fixtures.SelectMany(f => new[] { f.HomeClubId, f.AwayClubId }).Distinct().ToList();
        var seats = await db.TeamAssignments.Where(a => clubs.Contains(a.ClubId) && a.To == null && a.ControllerType == ControllerType.Human && a.ParticipantId != null)
            .ToDictionaryAsync(a => a.ClubId, ct);
        var absent = new List<TeamAssignment>();
        var assisted = 0;
        foreach (var f in fixtures)
        {
            var home = seats.GetValueOrDefault(f.HomeClubId);
            var away = seats.GetValueOrDefault(f.AwayClubId);
            if (home is null && away is null) continue;
            f.AssistantPlays = true;
            assisted++;
            if (home is not null && f.HomeReadyAt is null) { f.HomeAbsent = true; absent.Add(home); }
            if (away is not null && f.AwayReadyAt is null) { f.AwayAbsent = true; absent.Add(away); }
        }
        foreach (var a in absent.Distinct()) a.MissedInRow++;
        await db.SaveChangesAsync(ct);

        var mail = new MailService(db, clock);
        var names = await db.Clubs.Where(c => clubs.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var md = await db.Matchdays.Where(m => m.Id == matchdayId).Select(m => m.Number).SingleAsync(ct);
        List<string> absentNames = [], warned = [], released = [];
        foreach (var a in absent.Distinct())
        {
            var club = names.GetValueOrDefault(a.ClubId, "?");
            absentNames.Add(club);
            if (a.MissedInRow >= AbsenceRules.ReleaseAt)
            {
                try
                {
                    await new ClubAdminService(db, clock).SetHumanClubAsync(null, "hệ thống (vắng mặt)", a.ParticipantId, ct);
                }
                catch (AdminRuleException) { continue; }   // CLB đang có trận đang đá → đêm hạn sau xử lý tiếp (chuỗi vắng vẫn ≥ ngưỡng)
                released.Add(club);
                await mail.SystemAsync(a.ParticipantId, $"Bạn không còn dẫn dắt {club}",
                    $"Bạn đã vắng {a.MissedInRow} vòng liên tiếp (tới vòng {md}) nên {club} được trả về BOT theo luật giải. " +
                    "Bạn vẫn có thể nộp lại đơn đăng ký đá giải để nhận CLB khác.", ct: ct);
                await mail.SystemToAdminAsync($"{club} về BOT do HLV vắng {a.MissedInRow} vòng",
                    $"HLV đã vắng {a.MissedInRow} vòng liên tiếp tới vòng {md}. {club} đã về BOT. Vào Quản trị → HLV để duyệt đơn trong danh sách chờ.", ct);
                foreach (var waiting in await db.LeagueApplications.Where(x => x.Status == LeagueApplicationStatus.Pending).Select(x => x.ManagerId).ToListAsync(ct))
                    await mail.SystemAsync(waiting, $"{club} vừa trống HLV",
                        $"{club} vừa về BOT. Đơn đăng ký đá giải của bạn vẫn đang chờ, Ban tổ chức sẽ xét giao CLB.", ct: ct);
            }
            else if (a.MissedInRow >= AbsenceRules.WarnAt && a.MissWarnedAt is null)
            {
                a.MissWarnedAt = clock.GetUtcNow();
                await db.SaveChangesAsync(ct);
                warned.Add(club);
                await mail.SystemAsync(a.ParticipantId, $"Cảnh cáo vắng mặt — {club}",
                    $"Bạn đã vắng {a.MissedInRow} vòng liên tiếp (trợ lý đá thay, kết quả vẫn tính). " +
                    $"Vắng tới {AbsenceRules.ReleaseAt} vòng liên tiếp, {club} sẽ về BOT và nhường cho người trong danh sách chờ. " +
                    "Không đá được thì vào trang trận bấm \"Để trợ lý đá trận này\" trước hạn (không tính vắng).", ct: ct);
                await mail.SystemToAdminAsync($"Cảnh cáo vắng: {club}", $"HLV {club} vắng {a.MissedInRow} vòng liên tiếp tới vòng {md}.", ct);
            }
        }
        return new DeadlineReport(assisted, absentNames, warned, released);
    }

    /// <summary>Ghi kết quả trận: bên HLV người không bị đánh dấu vắng (tự đá / giao trợ lý / đã Sẵn sàng) → chuỗi vắng về 0.</summary>
    public async Task ResetAsync(Fixture f, CancellationToken ct = default)
    {
        var present = new List<Guid>();
        if (!f.HomeAbsent) present.Add(f.HomeClubId);
        if (!f.AwayAbsent) present.Add(f.AwayClubId);
        foreach (var a in await db.TeamAssignments.Where(a => present.Contains(a.ClubId) && a.To == null && a.ControllerType == ControllerType.Human
                     && (a.MissedInRow > 0 || a.MissWarnedAt != null)).ToListAsync(ct))
            (a.MissedInRow, a.MissWarnedAt) = (0, null);
    }
}
