using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Infrastructure;

namespace Pes17League.Clubs.Application;

/// <summary>
/// Đội huyền thoại PES17 (PO 20/11): 5 CLB tên "… Classics" (European / Pes / We / Myclub / World Classics).
/// Cầu thủ của các đội này không vào thị trường chuyển nhượng (cả career lẫn giải online): ẩn khỏi chợ, không mua/mượn/đề nghị/thanh lý,
/// đội huyền thoại cũng không mua ai. Nhận diện theo tên — không đổi cấu trúc DB.
/// </summary>
public static class LegendClubs
{
    public const string Suffix = " Classics";

    public static bool IsName(string? clubName) => clubName is not null && clubName.EndsWith(Suffix, StringComparison.Ordinal);

    public static IQueryable<Guid> Ids(ClubsDbContext db) => db.Clubs.Where(c => c.Name.EndsWith(Suffix)).Select(c => c.Id);

    /// <summary>Cầu thủ đang thuộc một đội huyền thoại.</summary>
    public static Task<bool> HasPlayerAsync(ClubsDbContext db, Guid playerId, CancellationToken ct = default) =>
        db.SquadMemberships.AnyAsync(m => m.PlayerId == playerId && m.LeftAt == null && Ids(db).Contains(m.ClubId), ct);

    public const string Message = "Cầu thủ huyền thoại không được chuyển nhượng.";
}
