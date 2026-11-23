using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Pes17League.Clubs.Application;
using Pes17League.Clubs.Infrastructure;
using Microsoft.Extensions.Http;
using Pes17League.Web;
using Pes17League.Web.Components;

// Web vận hành alpha (PO duyệt 02/11): vòng đấu + đá trận, BXH, lịch, CLB, chuyển nhượng. 1 mật khẩu admin (PES17_WEB_PASSWORD).
var b = WebApplication.CreateBuilder(args);
string Env(string name) => b.Configuration[name] ?? throw new InvalidOperationException($"Thiếu {name}");
var cs = Env("PES17_DB");
var storeDir = Env("PES17_STORE");
var password = Env("PES17_WEB_PASSWORD");
// Tài khoản quản trị phụ (PO 18/11): PES17_WEB_ADMINS="ten:matkhau;ten2:matkhau2" trong web.env — ngoài tài khoản admin chung
var extraAdmins = (b.Configuration["PES17_WEB_ADMINS"] ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .Select(x => x.Split(':', 2)).Where(x => x.Length == 2 && x[0] != "")
    .ToDictionary(x => x[0].ToLowerInvariant(), x => x[1]);
static bool SameSecret(string a, string b) => System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(a), System.Text.Encoding.UTF8.GetBytes(b));
var authOff = b.Configuration["PES17_WEB_AUTH"] == "off";   // PO trải nghiệm thử: bỏ đăng nhập (chỉ nghe Tailscale + localhost)

b.Services.AddRazorComponents().AddInteractiveServerComponents();
b.Services.AddDbContextFactory<ClubsDbContext>(o => o.UseNpgsql(cs, x => x.MigrationsHistoryTable("__EFMigrationsHistory", ClubsDbContext.Schema)));
b.Services.AddSingleton<IOptionFileStore>(new FileSystemOptionFileStore(storeDir));
b.Services.AddSingleton(new Pes17GameFormations(Path.GetFullPath(Path.Combine(storeDir, "..", "pesdb", "formations.json"))));   // sơ đồ game PES17 (PO 21/11)
b.Services.AddSingleton(TimeProvider.System);
// Khoá "đang đá" của bộ chạy trận (tools/match17/pesmatch17.py, sync.sh đọc). Bộ chạy là tiến trình con của web → web vừa khởi động
// thì khoá còn sót là khoá chết (restart giữa trận), xoá luôn (PO 22/11).
var matchLock = Path.GetFullPath(Path.Combine(storeDir, "..", "pes17-deploy", "match.lock"));
if (File.Exists(matchLock)) { Console.WriteLine($"Xoá khoá trận sót: {File.ReadAllText(matchLock).Trim()}"); File.Delete(matchLock); }
b.Services.AddSingleton(new GameSettingsStore(Path.Combine(Path.GetDirectoryName(matchLock)!, "game-settings.json")));   // độ khó + đồ hoạ máy game (PO 22/11)
b.Services.AddSingleton(new MatchJobOptions(
    Path.GetFullPath(b.Configuration["Match:Script"]!), Path.Combine(storeDir, "..", "run"), b.Configuration["Match:StreamUrl"]!,
    Path.Combine(storeDir, "..", "recordings"), Path.Combine(storeDir, "..", "highlights"), b.Configuration.GetValue("Match:MinFreeDiskGb", 20),
    Path.Combine(storeDir, "..", "stats"), Path.Combine(storeDir, "..", "live"), Path.Combine(storeDir, "..", "logos"), matchLock,
    Path.Combine(storeDir, "..", "graphics")));
b.Services.AddSingleton(new HighlightStore(Path.GetFullPath(Path.Combine(storeDir, "..", "highlights"))));
b.Services.AddSingleton(new RoundReelStore(Path.GetFullPath(Path.Combine(storeDir, "..", "reels"))));
b.Services.AddSingleton(new LiveStore(Path.GetFullPath(Path.Combine(storeDir, "..", "live"))));
var logos = new LogoStore(Path.GetFullPath(Path.Combine(storeDir, "..", "logos")));
b.Services.AddSingleton(logos);
var boards = new ScoreboardStore(Path.GetFullPath(Path.Combine(storeDir, "..", "scoreboards")));
b.Services.AddSingleton(boards);
var news = new NewsStore(Path.GetFullPath(Path.Combine(storeDir, "..", "news")));
b.Services.AddSingleton(news);
var faces = new FaceStore(Path.GetFullPath(Path.Combine(storeDir, "..", "faces")));
b.Services.AddSingleton(faces);
var kits = new KitStore(Path.GetFullPath(Path.Combine(storeDir, "..", "kits")));
b.Services.AddSingleton(kits);
var graphics = new GraphicsStore(Path.GetFullPath(Path.Combine(storeDir, "..", "graphics")),   // đồ hoạ trong game (PO 23/11, /admin/do-hoa)
    Path.GetFullPath(Path.Combine(Path.GetDirectoryName(b.Configuration["Match:Script"]!)!, "..", "pes17db", "compe.py")),
    Path.GetDirectoryName(matchLock)!);
b.Services.AddSingleton(graphics);
b.Services.AddHttpClient("news", c => c.Timeout = TimeSpan.FromSeconds(45));
b.Services.AddSingleton<IArticleWriter>(sp => new DeepSeekArticleWriter(
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("news"),
    b.Configuration["DEEPSEEK_API_KEY"], b.Configuration["News:Model"] ?? "deepseek-flash"));
b.Services.AddSingleton<IFrameGrabber, DockerFrameGrabber>();
b.Services.AddSingleton<MatchJobService>();
b.Services.AddHostedService<RoundReelService>();
b.Services.AddSingleton<FriendlyHub>();
b.Services.AddSingleton<MailHub>();
b.Services.AddSingleton(new FriendlyMachineOptions(b.Configuration["Friendly:Container"] ?? "", b.Configuration["Friendly:PlayUrl"] ?? "",
    TimeSpan.FromMinutes(b.Configuration.GetValue("Friendly:MaxMinutes", 45))));
b.Services.AddSingleton<StreamPass>();
b.Services.AddHostedService<FriendlyWorker>();
// PO 17/11: hạn đá vòng ban đêm (League:NightBotDays "Mon,Wed,Fri,Sun", League:NightBotHourVn 23); rỗng = tắt.
// PO 22/11: TẮT (chỉ chơi career, giải online tạm hoãn) — hạn đêm giao trận của HLV cho trợ lý + đếm vắng, 5 lần mất CLB.
b.Services.AddSingleton(new NightBotOptions(
    (b.Configuration["League:NightBotDays"] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(d => Enum.Parse<DayOfWeek>(d.Length == 3 ? d switch { "Mon" => "Monday", "Tue" => "Tuesday", "Wed" => "Wednesday", "Thu" => "Thursday", "Fri" => "Friday", "Sat" => "Saturday", _ => "Sunday" } : d, true)).ToHashSet(),
    b.Configuration.GetValue("League:NightBotHourVn", 23)));
b.Services.AddHostedService<NightBotService>();
b.Services.AddScoped<LeagueQueries>();
b.Services.AddScoped<CurrentUser>();
b.Services.AddCascadingAuthenticationState();
b.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(storeDir, "..", "web-keys")));
b.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o => { o.LoginPath = "/login"; o.ExpireTimeSpan = TimeSpan.FromDays(30); o.SlidingExpiration = true; });
b.Services.AddAuthorization();

var app = b.Build();
await using (var db = await app.Services.GetRequiredService<IDbContextFactory<ClubsDbContext>>().CreateDbContextAsync())
    await db.Database.MigrateAsync();

// bộ tách nền MediaPipe tự phục vụ (lib/mediapipe): .tflite, .mjs không có sẵn trong bảng kiểu tệp
var staticTypes = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
staticTypes.Mappings[".tflite"] = "application/octet-stream";
staticTypes.Mappings[".mjs"] = "text/javascript";
// CSS/JS của app (không có ?v=) → no-cache: trình duyệt luôn hỏi lại (304 nếu không đổi). Không có header này Safari giữ bản cũ
// theo heuristic → trang mới đi với CSS cũ (PO 20/11, ô Cúp trang chủ career mất style). lib/ (thư viện ngoài) giữ cache mặc định.
app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = staticTypes,
    OnPrepareResponse = r =>
    {
        var path = r.Context.Request.Path;
        if (!path.StartsWithSegments("/lib") && (path.Value!.EndsWith(".css") || path.Value.EndsWith(".js") || path.Value.EndsWith(".webmanifest")))
            r.Context.Response.Headers.CacheControl = "no-cache";
    }
});
Directory.CreateDirectory(logos.Dir);
app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(logos.Dir), RequestPath = LogoStore.RequestPath });
Directory.CreateDirectory(boards.Dir);
app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(boards.Dir), RequestPath = ScoreboardStore.RequestPath });
Directory.CreateDirectory(news.Dir);
app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(news.Dir), RequestPath = NewsStore.RequestPath });
Directory.CreateDirectory(faces.Dir);
app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(faces.Dir), RequestPath = FaceStore.RequestPath });
Directory.CreateDirectory(graphics.Dir);
app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(graphics.Dir), RequestPath = GraphicsStore.RequestPath });
Directory.CreateDirectory(kits.Dir);
// áo đấu: URL có ?v= theo giờ sửa file → cho trình duyệt giữ 30 ngày, mở lại bảng chi tiết không phải hỏi lại máy chủ
app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(kits.Dir), RequestPath = KitStore.RequestPath,
    OnPrepareResponse = r => r.Context.Response.Headers.CacheControl = "public, max-age=2592000, immutable" });
app.UseAuthentication();
app.Use(async (ctx, next) =>
{
    // Khu quản trị chỉ cho admin (PO 16/11): HLV về trang chủ; khách (kể cả khi tắt đăng nhập) phải đăng nhập admin
    if (CurrentUser.AdminOnly(ctx.Request.Path) && !CurrentUser.IsAdmin(ctx.User))
        ctx.Response.Redirect(ctx.User.Identity?.IsAuthenticated == true ? "/" : "/login?ve=" + Uri.EscapeDataString(ctx.Request.Path + ctx.Request.QueryString));
    else if (CurrentUser.PlayerOnlyNotAdmin(ctx.Request.Path) && CurrentUser.IsAdmin(ctx.User))
        ctx.Response.Redirect("/admin");   // PO 17/11: admin = Ban tổ chức, không quản lý CLB / đá / giao hữu
    else if (CurrentUser.PlayerOnly(ctx.Request.Path) && ctx.User.Identity?.IsAuthenticated != true)
        ctx.Response.Redirect("/login?ve=" + Uri.EscapeDataString(ctx.Request.Path + ctx.Request.QueryString));   // khách vào trang người chơi → đăng nhập
    else if (authOff || ctx.User.Identity?.IsAuthenticated == true || ctx.Request.Path.StartsWithSegments("/login") || ctx.Request.Path.StartsWithSegments("/dang-ky") || ctx.Request.Path.Value is "/app.css" or "/theme.css" or "/manifest.webmanifest" || ctx.Request.Path.StartsWithSegments("/icon")) await next();
    else ctx.Response.Redirect("/login");
});
app.UseAntiforgery();

const string LoginHtml = """
<!doctype html><html lang="vi"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover">
<title>Đăng nhập · PES 17 League</title><link rel="stylesheet" href="/app.css"><link rel="stylesheet" href="/theme.css"><link rel="preconnect" href="https://fonts.gstatic.com" crossorigin><link href="https://fonts.googleapis.com/css2?family=Barlow+Condensed:wght@600;700;800&family=Be+Vietnam+Pro:wght@400;500;600;700;800&display=swap" rel="stylesheet"><meta name="theme-color" content="#0f131d"><link rel="manifest" href="/manifest.webmanifest"><link rel="icon" type="image/png" href="/icon-180.png?v=2"><link rel="apple-touch-icon" href="/icon-180.png?v=2"><meta name="apple-mobile-web-app-capable" content="yes"><meta name="apple-mobile-web-app-title" content="PES 17 Career"></head>
<body class="auth"><div class="bg-stadium" aria-hidden="true"><i></i><i></i><i></i></div><a class="auth-brand" href="/"><svg viewBox="0 0 32 36" aria-hidden="true"><path d="M16 1 30 6v11c0 9-6 15-14 18C8 32 2 26 2 17V6z" fill="var(--accent)"/><path d="M16 5 26 8.6V17c0 6.6-4.2 11.2-10 13.6C10.2 28.2 6 23.6 6 17V8.6z" fill="var(--brand)"/><path d="m16 10 1.9 3.9 4.3.6-3.1 3 .7 4.3-3.8-2-3.8 2 .7-4.3-3.1-3 4.3-.6z" fill="var(--accent)"/></svg><span>PES 17<b>League</b></span></a><form method="post" action="/login" class="login"><h1>Đăng nhập</h1><p class="auth-sub">Vào khu HLV để quản lý CLB và đá giải</p>{0}
<input type="hidden" name="ve" value="{1}"><label>Tài khoản<input name="username" placeholder="Tên đăng nhập (admin để trống)" autocomplete="username" autocapitalize="none"></label>
<label>Mật khẩu<input type="password" name="password" placeholder="Mật khẩu" autocomplete="current-password" autofocus></label><button class="primary">Đăng nhập</button>
<p class="login-alt">Chưa có tài khoản? <a href="/dang-ky">Đăng ký</a></p></form></body></html>
""";
// Người chơi tự đăng ký bằng mã mời (PO 16/11). {0} = lỗi, {1}..{3} = giá trị đã nhập (mã, tên đăng nhập, tên hiển thị)
const string SignupHtml = """
<!doctype html><html lang="vi"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover">
<title>Đăng ký · PES 17 League</title><link rel="stylesheet" href="/app.css"><link rel="stylesheet" href="/theme.css"><link rel="preconnect" href="https://fonts.gstatic.com" crossorigin><link href="https://fonts.googleapis.com/css2?family=Barlow+Condensed:wght@600;700;800&family=Be+Vietnam+Pro:wght@400;500;600;700;800&display=swap" rel="stylesheet"><meta name="theme-color" content="#0f131d"><link rel="manifest" href="/manifest.webmanifest"><link rel="icon" type="image/png" href="/icon-180.png?v=2"><link rel="apple-touch-icon" href="/icon-180.png?v=2"></head>
<body class="auth"><div class="bg-stadium" aria-hidden="true"><i></i><i></i><i></i></div><a class="auth-brand" href="/"><svg viewBox="0 0 32 36" aria-hidden="true"><path d="M16 1 30 6v11c0 9-6 15-14 18C8 32 2 26 2 17V6z" fill="var(--accent)"/><path d="M16 5 26 8.6V17c0 6.6-4.2 11.2-10 13.6C10.2 28.2 6 23.6 6 17V8.6z" fill="var(--brand)"/><path d="m16 10 1.9 3.9 4.3.6-3.1 3 .7 4.3-3.8-2-3.8 2 .7-4.3-3.1-3 4.3-.6z" fill="var(--accent)"/></svg><span>PES 17<b>League</b></span></a><form method="post" action="/dang-ky" class="login"><h1>Tạo tài khoản HLV</h1><p class="auth-sub">Đăng ký để nhận CLB và tham gia giải</p>{0}
<label>Mã mời<input name="code" placeholder="Mã mời (không bắt buộc)" value="{1}" autocapitalize="characters"></label>
<label>Tên đăng nhập<input name="username" placeholder="Tên đăng nhập (a-z, 0-9, . _ -)" value="{2}" autocomplete="username" autocapitalize="none" required></label>
<label>Tên hiển thị<input name="displayName" placeholder="Tên hiển thị" value="{3}" required></label>
<label>Mật khẩu<input type="password" name="password" placeholder="Mật khẩu (ít nhất 3 ký tự, vd 123)" autocomplete="new-password" required></label>
<label>Nhập lại mật khẩu<input type="password" name="password2" placeholder="Nhập lại mật khẩu" autocomplete="new-password" required></label><button class="primary">Tạo tài khoản</button>
<p class="login-alt">Đã có tài khoản? <a href="/login">Đăng nhập</a></p></form></body></html>
""";
const string NotFoundHtml = """
<!doctype html><html lang="vi"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover">
<title>Không tìm thấy trang · PES 17 League</title><link rel="stylesheet" href="/app.css"><link rel="stylesheet" href="/theme.css"><link href="https://fonts.googleapis.com/css2?family=Barlow+Condensed:wght@600;700;800&family=Be+Vietnam+Pro:wght@400;500;600;700;800&display=swap" rel="stylesheet"><meta name="theme-color" content="#0f131d"></head>
<body class="auth"><a class="auth-brand" href="/"><svg viewBox="0 0 32 36" aria-hidden="true"><path d="M16 1 30 6v11c0 9-6 15-14 18C8 32 2 26 2 17V6z" fill="var(--accent)"/><path d="M16 5 26 8.6V17c0 6.6-4.2 11.2-10 13.6C10.2 28.2 6 23.6 6 17V8.6z" fill="var(--brand)"/><path d="m16 10 1.9 3.9 4.3.6-3.1 3 .7 4.3-3.8-2-3.8 2 .7-4.3-3.1-3 4.3-.6z" fill="var(--accent)"/></svg><span>PES 17<b>League</b></span></a>
<div class="login nf"><b class="nf-code">404</b><h1>Không tìm thấy trang</h1><p class="auth-sub">Đường dẫn sai hoặc trang đã bị gỡ.</p><a class="btn primary" href="/">Về trang chủ</a><p class="login-alt"><a href="/lich">Lịch thi đấu</a> · <a href="/bxh">Bảng xếp hạng</a> · <a href="/tim-kiem">Tìm kiếm</a></p></div></body></html>
""";
// Tắt đăng nhập (PO thử): khách chỉ xem (16/11: không còn là admin); admin/HLV đăng nhập ở /login
app.MapGet("/login", (string? ve) => Results.Content(string.Format(LoginHtml, "", System.Net.WebUtility.HtmlEncode(ve ?? "")), "text/html; charset=utf-8"));
app.MapPost("/login", async (HttpContext ctx, IDbContextFactory<ClubsDbContext> dbs) =>
{
    var form = await ctx.Request.ReadFormAsync();
    var user = form["username"].ToString().Trim();
    List<Claim> claims;
    if (user is "" or "admin")
    {
        if (!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(form["password"].ToString()), System.Text.Encoding.UTF8.GetBytes(password)))
            return Results.Content(string.Format(LoginHtml, "<p class=err>Sai mật khẩu</p>", System.Net.WebUtility.HtmlEncode(form["ve"].ToString())), "text/html; charset=utf-8");
        claims = [new(ClaimTypes.Name, "admin"), new(ClaimTypes.Role, CurrentUser.AdminRole)];
    }
    else if (extraAdmins.TryGetValue(user.ToLowerInvariant(), out var adminPass))
    {
        if (!SameSecret(form["password"].ToString(), adminPass))
            return Results.Content(string.Format(LoginHtml, "<p class=err>Sai mật khẩu</p>", System.Net.WebUtility.HtmlEncode(form["ve"].ToString())), "text/html; charset=utf-8");
        claims = [new(ClaimTypes.Name, user.ToLowerInvariant()), new(ClaimTypes.Role, CurrentUser.AdminRole)];
    }
    else
    {
        // HLV (lộ trình FM đợt 2, PO ok 12/11): tài khoản admin tạo ở /admin/hlv
        await using var db = await dbs.CreateDbContextAsync();
        var m = await new ManagerService(db, TimeProvider.System).VerifyAsync(user, form["password"].ToString());
        if (m is null) return Results.Content(string.Format(LoginHtml, "<p class=err>Sai tài khoản hoặc mật khẩu</p>", System.Net.WebUtility.HtmlEncode(form["ve"].ToString())), "text/html; charset=utf-8");
        claims = [new(ClaimTypes.Name, m.Username), new(ClaimTypes.GivenName, m.DisplayName), new(CurrentUser.ManagerClaim, m.Id.ToString())];
    }
    await ctx.SignInAsync(new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)),
        new AuthenticationProperties { IsPersistent = true });
    var back = form["ve"].ToString();   // quay lại trang admin đã định vào (chỉ đường dẫn nội bộ)
    return Results.Redirect(back.StartsWith('/') && !back.StartsWith("//") && !back.Contains('\\') ? back : "/");
}).DisableAntiforgery();
var signupInvite = b.Configuration["PES17_SIGNUP_INVITE"] == "on";   // PO 16/11: mặc định đăng ký tự do để test; bật lại mã mời bắt buộc qua env
var signupTries = new System.Collections.Concurrent.ConcurrentDictionary<string, List<DateTimeOffset>>();   // IP → lần thử đăng ký (≤ 10/giờ, chống dò mã mời)
app.MapGet("/dang-ky", () => Results.Content(string.Format(SignupHtml, "", "", "", ""), "text/html; charset=utf-8"));
app.MapPost("/dang-ky", async (HttpContext ctx, IDbContextFactory<ClubsDbContext> dbs) =>
{
    var form = await ctx.Request.ReadFormAsync();
    string F(string k) => form[k].ToString();
    IResult Page(string err) => Results.Content(string.Format(SignupHtml, $"<p class=err>{System.Net.WebUtility.HtmlEncode(err)}</p>",
        System.Net.WebUtility.HtmlEncode(F("code")), System.Net.WebUtility.HtmlEncode(F("username")), System.Net.WebUtility.HtmlEncode(F("displayName"))), "text/html; charset=utf-8");
    var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "?";
    var now = DateTimeOffset.UtcNow;
    var tries = signupTries.GetOrAdd(ip, _ => []);
    lock (tries) { tries.RemoveAll(t => t < now.AddHours(-1)); if (tries.Count >= 10) return Page("Thử quá nhiều lần, chờ 1 giờ rồi thử lại"); tries.Add(now); }
    if (F("password") != F("password2")) return Page("Hai mật khẩu không khớp");
    if (F("username").Trim().ToLowerInvariant() is var un && (un == "admin" || extraAdmins.ContainsKey(un))) return Page("Tên đăng nhập này đã có người dùng");
    Pes17League.Clubs.Domain.Manager m;
    try
    {
        await using var db = await dbs.CreateDbContextAsync();
        m = await new ManagerService(db, TimeProvider.System).RegisterAsync(F("code"), F("username"), F("displayName"), F("password"), requireInvite: signupInvite);
    }
    catch (AdminRuleException e) { return Page(e.Message); }
    await ctx.SignInAsync(new ClaimsPrincipal(new ClaimsIdentity(
        [new(ClaimTypes.Name, m.Username), new(ClaimTypes.GivenName, m.DisplayName), new(CurrentUser.ManagerClaim, m.Id.ToString())], CookieAuthenticationDefaults.AuthenticationScheme)),
        new AuthenticationProperties { IsPersistent = true });
    return Results.Redirect("/su-nghiep");   // PO 23/11: tạo tài khoản xong vào thẳng career (chọn CLB), không rơi về trang người chơi kiểu cũ
}).DisableAntiforgery();
app.MapPost("/logout", async (HttpContext ctx) => { await ctx.SignOutAsync(); return Results.Redirect(authOff ? "/" : "/login"); }).DisableAntiforgery();

// PO 12/11: bỏ video highlight trận (đã xoá file); còn ảnh {fixture}.jpg làm ảnh thẻ bàn thắng
app.MapGet("/highlight/{id:guid}/anh", (Guid id, HighlightStore h) =>
    h.HasPoster(id) ? Results.File(h.PosterPathFor(id), "image/jpeg") : Results.NotFound());
// Clip tổng hợp bàn thắng của vòng (PO 12/11)
app.MapGet(RoundReelStore.RequestPath + "/{key}", (string key, RoundReelStore r) =>
    r.PathFor(key, ".mp4") is { } p && File.Exists(p) ? Results.File(p, "video/mp4", enableRangeProcessing: true) : Results.NotFound());
app.MapGet(RoundReelStore.RequestPath + "/{key}/doc", (string key, RoundReelStore r) =>
    r.PathFor(key, "-doc.mp4") is { } p && File.Exists(p) ? Results.File(p, "video/mp4", enableRangeProcessing: true) : Results.NotFound());
app.MapGet(RoundReelStore.RequestPath + "/{key}/anh", (string key, RoundReelStore r) =>
    r.PathFor(key, ".jpg") is { } p && File.Exists(p) ? Results.File(p, "image/jpeg") : Results.NotFound());
// Xem trực tiếp: playlist đổi mỗi 2 s → không cache; đoạn .ts không đổi nhưng chỉ sống ~12 s
app.MapGet(LiveStore.RequestPath + "/{file}", (string file, LiveStore l, HttpContext ctx) =>
{
    if (l.PathFor(file) is not { } path || !File.Exists(path)) return Results.NotFound();
    var playlist = file.EndsWith(".m3u8");
    ctx.Response.Headers.CacheControl = playlist ? "no-store" : "no-cache";
    return Results.File(path, playlist ? "application/vnd.apple.mpegurl" : "video/mp2t");
});
// Ảnh HLV career (PO 19/11): mặt đã căn + ảnh lưu niệm — chỉ chủ hồ sơ (admin xem mọi hồ sơ)
static Guid? CoachViewer(HttpContext ctx) => CurrentUser.IsAdmin(ctx.User) ? null
    : Guid.TryParse(ctx.User.FindFirstValue(CurrentUser.ManagerClaim), out var g) ? g : Guid.Empty;
app.MapGet("/hlv/anh/mat/{id:guid}", async (Guid id, HttpContext ctx, IDbContextFactory<ClubsDbContext> dbs) =>
{
    await using var db = await dbs.CreateDbContextAsync();
    var img = await new CoachService(db, TimeProvider.System).FaceAsync(CoachViewer(ctx), id);
    ctx.Response.Headers.CacheControl = "private, max-age=86400";
    return img is null ? Results.NotFound() : Results.Bytes(img, CoachUi.ImageType(img));
});
app.MapGet("/hlv/anh/{id:guid}", async (Guid id, HttpContext ctx, IDbContextFactory<ClubsDbContext> dbs) =>
{
    await using var db = await dbs.CreateDbContextAsync();
    var img = await new CoachService(db, TimeProvider.System).PhotoAsync(CoachViewer(ctx), id);
    ctx.Response.Headers.CacheControl = "private, max-age=604800";
    return img is null ? Results.NotFound() : Results.Bytes(img, "image/jpeg");
});
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
// Đường dẫn không tồn tại (UI 18/11): trang 404 tiếng Việt thay vì trang trắng của trình duyệt
app.MapFallback(() => Results.Content(NotFoundHtml, "text/html; charset=utf-8", statusCode: 404));
app.Run();
