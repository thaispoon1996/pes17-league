using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace Pes17League.Web;

/// <summary>Người đang dùng web. 4 vai trò (PO 17/11, trang /vai-tro):
/// <list type="bullet">
/// <item>Khách vãng lai: chưa đăng nhập — chỉ xem (trực tiếp, giao hữu, giải, chuyển nhượng, tin), được đăng ký tài khoản.</item>
/// <item>Người chơi (có tài khoản, chưa có CLB): + giao hữu xếp hạng cá nhân (Elo) + nộp đơn đá giải.</item>
/// <item>HLV (được giao CLB): + quản lý CLB mình, đá giải, giao hữu thách đấu bằng CLB mình.</item>
/// <item>Admin (chỉ PO, mật khẩu admin): Ban tổ chức — giải, cài đặt game, luật, thưởng/phạt, điều hành. KHÔNG cầm CLB, KHÔNG đá,
/// không vào trang người chơi (trừ hòm thư = hòm Ban tổ chức). Tài khoản HLV không bao giờ là admin.</item>
/// </list></summary>
public sealed class CurrentUser(AuthenticationStateProvider auth)
{
    public const string ManagerClaim = "pid", AdminRole = "admin";
    private ClaimsPrincipal? _user;
    private bool _outside;

    private async Task<ClaimsPrincipal> UserAsync()
    {
        if (_user is not null) return _user;
        try { _user = (await auth.GetAuthenticationStateAsync()).User; }
        catch (InvalidOperationException) { _user = new ClaimsPrincipal(); _outside = true; }   // ngoài Blazor (việc nền/endpoint): coi như admin
        return _user;
    }

    public static bool IsAdmin(ClaimsPrincipal u) =>
        u.IsInRole(AdminRole) || (u.Identity?.IsAuthenticated == true && u.Identity.Name == "admin" && !u.HasClaim(c => c.Type == ManagerClaim));

    public async Task<bool> IsAdminAsync() { var u = await UserAsync(); return _outside || IsAdmin(u); }

    public async Task<bool> IsGuestAsync() { var u = await UserAsync(); return !_outside && u.Identity?.IsAuthenticated != true; }

    /// <summary>Id HLV; null = admin hoặc khách.</summary>
    public async Task<Guid?> ManagerIdAsync() =>
        Guid.TryParse((await UserAsync()).FindFirstValue(ManagerClaim), out var id) ? id : null;

    /// <summary>Khoá tìm "CLB của tôi" (phân công Human theo ParticipantId): HLV = id; admin/khách = Guid.Empty (không có CLB).
    /// PO 17/11: admin = Ban tổ chức, KHÔNG có CLB → như khách (Guid.Empty). Chỉ việc nền (ngoài Blazor) giữ null.</summary>
    public async Task<Guid?> ParticipantKeyAsync() =>
        await ManagerIdAsync() ?? (_outside ? null : Guid.Empty);

    public async Task<string?> UsernameAsync() => (await UserAsync()).Identity?.Name;

    public async Task<string> NameAsync() => (await UserAsync()).FindFirstValue(ClaimTypes.GivenName) ?? (await UserAsync()).Identity?.Name ?? "khách";

    /// <summary>Đường dẫn chỉ admin: khu quản trị + ẩn/hiện cầu thủ. (/dieu-khien = cài bàn phím/tay cầm của người chơi, không phải trang admin.)</summary>
    /// <summary>Trang chỉ cho người đã đăng nhập (PO 16/11, phân quyền): quản lý CLB, đá giải, giao hữu, đăng ký đá giải.
    /// Khách (chưa đăng nhập) chỉ xem trang công khai (vòng đấu, lịch, BXH, tin, hồ sơ CLB/cầu thủ/người chơi…).</summary>
    public static readonly string[] PlayerPages = ["/hop-thu", "/thu", "/chuyen-nhuong", "/de-nghi", "/tai-chinh", "/tap-luyen", "/hoc-vien",
        "/ban-lanh-dao", "/dieu-khien", "/chuan-bi", "/vao-tran", "/dang-ky-giai"];

    /// <summary>Trang người chơi admin vẫn vào được: hòm thư (admin = hòm Ban tổ chức).</summary>
    public static readonly string[] AdminAllowedPlayerPages = ["/thu"];

    /// <summary>PO 17/11: admin chỉ điều hành, không quản lý CLB / đá giải / giao hữu / đăng ký → trang người chơi (trừ hòm thư) không cho admin.</summary>
    public static bool PlayerOnlyNotAdmin(PathString path) =>
        PlayerOnly(path) && !AdminAllowedPlayerPages.Any(p => path.StartsWithSegments(p));

    /// <summary>PO 17/11: sảnh /giao-huu ai cũng xem được (khách, admin chỉ xem); phòng trận /giao-huu/{id} chỉ người chơi.</summary>
    public static bool PlayerOnly(PathString path) =>
        path.Value?.TrimEnd('/') is "/clb" || PlayerPages.Any(p => path.StartsWithSegments(p))
        || (path.StartsWithSegments("/giao-huu") && path.Value?.TrimEnd('/') != "/giao-huu");

    public static bool AdminOnly(PathString path) =>
        path.StartsWithSegments("/admin") || path.StartsWithSegments("/an-hien");
}
