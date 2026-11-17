using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace Pes17League.Web;

/// <summary>Người đang dùng web (lộ trình FM đợt 2, PO ok 12/11): admin (mật khẩu chung, PO) hoặc HLV (<see cref="Pes17League.Clubs.Domain.Manager"/>).
/// Tắt đăng nhập (PES17_WEB_AUTH=off): khách = admin, HLV vẫn đăng nhập được. HLV chỉ thấy/sửa CLB của mình, không vào trang admin.</summary>
public sealed class CurrentUser(AuthenticationStateProvider auth)
{
    public const string ManagerClaim = "pid", AdminRole = "admin";
    private ClaimsPrincipal? _user;

    private async Task<ClaimsPrincipal> UserAsync()
    {
        if (_user is not null) return _user;
        try { _user = (await auth.GetAuthenticationStateAsync()).User; }
        catch (InvalidOperationException) { _user = new ClaimsPrincipal(); }   // ngoài Blazor (endpoint thường): coi như admin
        return _user;
    }

    /// <summary>Admin = không phải HLV (cookie cũ trước 12/11 chỉ có Name "admin", không có role).</summary>
    public async Task<bool> IsAdminAsync() => (await ManagerIdAsync()) is null;

    /// <summary>Id HLV; null = admin (CLB của admin = phân công Human có ParticipantId null).</summary>
    public async Task<Guid?> ManagerIdAsync() =>
        Guid.TryParse((await UserAsync()).FindFirstValue(ManagerClaim), out var id) ? id : null;

    public async Task<string> NameAsync() => (await UserAsync()).FindFirstValue(ClaimTypes.GivenName) ?? (await UserAsync()).Identity?.Name ?? "admin";

    /// <summary>Đường dẫn chỉ admin: trang admin, ẩn/hiện cầu thủ, bảng điều khiển máy trận.</summary>
    public static bool AdminOnly(PathString path) =>
        path.StartsWithSegments("/admin") || path.StartsWithSegments("/an-hien") || path.StartsWithSegments("/dieu-khien");
}
