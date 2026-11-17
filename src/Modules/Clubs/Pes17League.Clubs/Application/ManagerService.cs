using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Infrastructure;

namespace Pes17League.Clubs.Application;

/// <summary>1 HLV trên trang admin: CLB đang cầm (null = chưa giao).</summary>
public sealed record ManagerRow(Guid Id, string Username, string DisplayName, bool Disabled, DateTimeOffset CreatedAt, DateTimeOffset? LastSeenAt, Guid? ClubId);

/// <summary>Tài khoản HLV (lộ trình FM đợt 2, PO ok 12/11): admin tạo tài khoản, giao CLB; mật khẩu sinh ngẫu nhiên, chỉ hiện 1 lần.</summary>
public sealed partial class ManagerService(ClubsDbContext db, TimeProvider clock)
{
    public const string CreateAction = "CreateManager", ResetAction = "ResetManagerPassword", DisableAction = "SetManagerDisabled";
    private const int Iterations = 100_000;

    [GeneratedRegex("^[a-z0-9_.-]{3,40}$")] private static partial Regex UsernameRx();

    public async Task<List<ManagerRow>> ListAsync(CancellationToken ct = default)
    {
        var clubs = await db.TeamAssignments.Where(a => a.To == null && a.ControllerType == ControllerType.Human && a.ParticipantId != null)
            .Select(a => new { a.ParticipantId, a.ClubId }).ToListAsync(ct);
        return (await db.Managers.OrderBy(m => m.Username).ToListAsync(ct))
            .Select(m => new ManagerRow(m.Id, m.Username, m.DisplayName, m.Disabled, m.CreatedAt, m.LastSeenAt, clubs.FirstOrDefault(c => c.ParticipantId == m.Id)?.ClubId))
            .ToList();
    }

    /// <returns>Mật khẩu vừa sinh (chỉ hiện 1 lần).</returns>
    public async Task<string> CreateAsync(string username, string displayName, string actor, CancellationToken ct = default)
    {
        username = username.Trim().ToLowerInvariant();
        displayName = displayName.Trim();
        if (!UsernameRx().IsMatch(username) || username == "admin") throw new AdminRuleException("Tên đăng nhập 3–40 ký tự a-z, 0-9, . _ - (không dùng \"admin\")");
        if (displayName.Length is 0 or > 60) throw new AdminRuleException("Tên hiển thị 1–60 ký tự");
        if (await db.Managers.AnyAsync(m => m.Username == username, ct)) throw new AdminRuleException($"Đã có tài khoản {username}");
        var password = NewPassword();
        var m = new Manager { Username = username, DisplayName = displayName, PasswordHash = Hash(password), CreatedAt = clock.GetUtcNow() };
        db.Managers.Add(m);
        Audit(m.Id, CreateAction, null, new { username, displayName }, actor);
        await db.SaveChangesAsync(ct);
        return password;
    }

    public async Task<string> ResetPasswordAsync(Guid id, string actor, CancellationToken ct = default)
    {
        var m = await db.Managers.FindAsync([id], ct) ?? throw new AdminRuleException("Không tìm thấy HLV");
        var password = NewPassword();
        m.PasswordHash = Hash(password);
        Audit(id, ResetAction, null, null, actor);
        await db.SaveChangesAsync(ct);
        return password;
    }

    /// <summary>Khoá tài khoản = không đăng nhập được; CLB đang cầm về BOT (trợ lý).</summary>
    public async Task SetDisabledAsync(Guid id, bool disabled, string actor, CancellationToken ct = default)
    {
        var m = await db.Managers.FindAsync([id], ct) ?? throw new AdminRuleException("Không tìm thấy HLV");
        if (m.Disabled == disabled) return;
        if (disabled) await new ClubAdminService(db, clock).SetHumanClubAsync(null, actor, id, ct);
        m.Disabled = disabled;
        Audit(id, DisableAction, new { Disabled = !disabled }, new { Disabled = disabled }, actor);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Đăng nhập: đúng mật khẩu + chưa khoá → ghi LastSeenAt.</summary>
    public async Task<Manager?> VerifyAsync(string username, string password, CancellationToken ct = default)
    {
        username = username.Trim().ToLowerInvariant();
        var m = await db.Managers.SingleOrDefaultAsync(x => x.Username == username, ct);
        if (m is null || m.Disabled || !Check(m.PasswordHash, password)) return null;
        m.LastSeenAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return m;
    }

    public async Task TouchAsync(Guid id, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        await db.Managers.Where(m => m.Id == id && (m.LastSeenAt == null || m.LastSeenAt < now.AddMinutes(-10)))
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.LastSeenAt, now), ct);
    }

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        return $"v1.{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public static bool Check(string stored, string password)
    {
        var p = stored.Split('.');
        if (p.Length != 4 || p[0] != "v1" || !int.TryParse(p[1], out var it)) return false;
        var hash = Convert.FromBase64String(p[3]);
        return CryptographicOperations.FixedTimeEquals(Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(p[2]), it, HashAlgorithmName.SHA256, hash.Length), hash);
    }

    private static string NewPassword()
    {
        const string chars = "abcdefghjkmnpqrstuvwxyz23456789";
        return string.Create(10, 0, (s, _) => { for (var i = 0; i < s.Length; i++) s[i] = chars[RandomNumberGenerator.GetInt32(chars.Length)]; });
    }

    private void Audit(Guid id, string action, object? before, object? after, string actor) =>
        db.AdminAudit.Add(new AdminAuditEntry { At = clock.GetUtcNow(), Actor = actor, EntityType = nameof(Manager), EntityId = id, Action = action,
            Before = before is null ? null : JsonSerializer.Serialize(before), After = after is null ? null : JsonSerializer.Serialize(after) });
}
