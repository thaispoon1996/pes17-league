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
var authOff = b.Configuration["PES17_WEB_AUTH"] == "off";   // PO trải nghiệm thử: bỏ đăng nhập (chỉ nghe Tailscale + localhost)

b.Services.AddRazorComponents().AddInteractiveServerComponents();
b.Services.AddDbContextFactory<ClubsDbContext>(o => o.UseNpgsql(cs, x => x.MigrationsHistoryTable("__EFMigrationsHistory", ClubsDbContext.Schema)));
b.Services.AddSingleton<IOptionFileStore>(new FileSystemOptionFileStore(storeDir));
b.Services.AddSingleton(TimeProvider.System);
b.Services.AddSingleton(new MatchJobOptions(
    Path.GetFullPath(b.Configuration["Match:Script"]!), Path.Combine(storeDir, "..", "run"), b.Configuration["Match:StreamUrl"]!,
    Path.Combine(storeDir, "..", "recordings"), Path.Combine(storeDir, "..", "highlights"), b.Configuration.GetValue("Match:MinFreeDiskGb", 20),
    Path.Combine(storeDir, "..", "stats"), Path.Combine(storeDir, "..", "live")));
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
b.Services.AddHttpClient("news", c => c.Timeout = TimeSpan.FromSeconds(45));
b.Services.AddSingleton<IArticleWriter>(sp => new DeepSeekArticleWriter(
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("news"),
    b.Configuration["DEEPSEEK_API_KEY"], b.Configuration["News:Model"] ?? "deepseek-flash"));
b.Services.AddSingleton<IFrameGrabber, DockerFrameGrabber>();
b.Services.AddSingleton<MatchJobService>();
b.Services.AddHostedService<RoundReelService>();
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

app.UseStaticFiles();
Directory.CreateDirectory(logos.Dir);
app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(logos.Dir), RequestPath = LogoStore.RequestPath });
Directory.CreateDirectory(boards.Dir);
app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(boards.Dir), RequestPath = ScoreboardStore.RequestPath });
Directory.CreateDirectory(news.Dir);
app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(news.Dir), RequestPath = NewsStore.RequestPath });
Directory.CreateDirectory(faces.Dir);
app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(faces.Dir), RequestPath = FaceStore.RequestPath });
app.UseAuthentication();
app.Use(async (ctx, next) =>
{
    if (ctx.User.HasClaim(c => c.Type == CurrentUser.ManagerClaim) && CurrentUser.AdminOnly(ctx.Request.Path))
        ctx.Response.Redirect("/");   // HLV không vào trang admin (đợt 2)
    else if (authOff || ctx.User.Identity?.IsAuthenticated == true || ctx.Request.Path.StartsWithSegments("/login") || ctx.Request.Path.Value is "/app.css" or "/manifest.webmanifest" || ctx.Request.Path.StartsWithSegments("/icon")) await next();
    else ctx.Response.Redirect("/login");
});
app.UseAntiforgery();

const string LoginHtml = """
<!doctype html><html lang="vi"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover">
<title>PES League</title><link rel="stylesheet" href="/app.css"><link rel="manifest" href="/manifest.webmanifest"><link rel="apple-touch-icon" href="/icon-180.png"><meta name="apple-mobile-web-app-capable" content="yes"></head>
<body><form method="post" action="/login" class="login"><h1>PES League</h1>{0}
<input name="username" placeholder="Tài khoản HLV (admin bỏ trống)" autocomplete="username" autocapitalize="none">
<input type="password" name="password" placeholder="Mật khẩu" autocomplete="current-password" autofocus><button>Đăng nhập</button></form></body></html>
""";
// Tắt đăng nhập (PO thử) vẫn mở /login cho HLV (đợt 2): khách không đăng nhập = admin, HLV đăng nhập = chỉ CLB của mình
app.MapGet("/login", () => Results.Content(string.Format(LoginHtml, ""), "text/html; charset=utf-8"));
app.MapPost("/login", async (HttpContext ctx, IDbContextFactory<ClubsDbContext> dbs) =>
{
    var form = await ctx.Request.ReadFormAsync();
    var user = form["username"].ToString().Trim();
    List<Claim> claims;
    if (user is "" or "admin")
    {
        if (!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(form["password"].ToString()), System.Text.Encoding.UTF8.GetBytes(password)))
            return Results.Content(string.Format(LoginHtml, "<p class=err>Sai mật khẩu</p>"), "text/html; charset=utf-8");
        claims = [new(ClaimTypes.Name, "admin"), new(ClaimTypes.Role, CurrentUser.AdminRole)];
    }
    else
    {
        // HLV (lộ trình FM đợt 2, PO ok 12/11): tài khoản admin tạo ở /admin/hlv
        await using var db = await dbs.CreateDbContextAsync();
        var m = await new ManagerService(db, TimeProvider.System).VerifyAsync(user, form["password"].ToString());
        if (m is null) return Results.Content(string.Format(LoginHtml, "<p class=err>Sai tài khoản hoặc mật khẩu</p>"), "text/html; charset=utf-8");
        claims = [new(ClaimTypes.Name, m.Username), new(ClaimTypes.GivenName, m.DisplayName), new(CurrentUser.ManagerClaim, m.Id.ToString())];
    }
    await ctx.SignInAsync(new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)),
        new AuthenticationProperties { IsPersistent = true });
    return Results.Redirect("/");
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
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();
