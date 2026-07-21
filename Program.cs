using System.Security.Claims;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PveWelcome.Components;
using PveWelcome.Data;
using PveWelcome.Models;
using PveWelcome.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// --- Data + users ---
var dbPath = builder.Configuration["Db:Path"] ?? "/data/pvewelcome.db";
builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite($"Data Source={dbPath}"));

// Persist data-protection keys next to the DB so auth/antiforgery survive restarts.
var keysDir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(dbPath)) ?? ".", "keys");
Directory.CreateDirectory(keysDir);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keysDir))
    .SetApplicationName("PveWelcome");
builder.Services.AddScoped<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<AiService>();
builder.Services.AddScoped<MonitorService>();
builder.Services.AddSingleton<LoginThrottle>();

// --- Config + services ---
builder.Services.AddSingleton<ConnectionConfig>();
builder.Services.AddSingleton<BrandResolver>();
builder.Services.AddSingleton<NotificationService>();
builder.Services.AddSingleton<PveDataService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<PveDataService>());

// --- API clients (base url + creds come from ConnectionConfig at runtime) ---
builder.Services.AddHttpClient<PveClient>(c => c.Timeout = TimeSpan.FromSeconds(15))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        // PVE uses a self-signed cert on the internal network.
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    });
builder.Services.AddHttpClient<NpmClient>(c => c.Timeout = TimeSpan.FromSeconds(15));
// external reachability checks for served domains (real end-to-end, via Cloudflare)
builder.Services.AddHttpClient("reach", c => c.Timeout = TimeSpan.FromSeconds(8))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
// outbound alert notifications (webhook / telegram)
builder.Services.AddHttpClient("notify", c => c.Timeout = TimeSpan.FromSeconds(10));

// --- Auth ---
builder.Services.AddHttpContextAccessor();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/login";
        o.AccessDeniedPath = "/login";
        o.ExpireTimeSpan = TimeSpan.FromDays(7);
        o.SlidingExpiration = true;
        o.Cookie.Name = "PveWelcome.Auth";
    });
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<AuthenticationStateProvider, HttpContextAuthStateProvider>();

var app = builder.Build();

// Seed admin + ensure db exists.
using (var scope = app.Services.CreateScope())
{
    var users = scope.ServiceProvider.GetRequiredService<UserService>();
    await users.InitAsync(
        app.Configuration["Admin:User"] ?? app.Configuration["ADMIN_USER"],
        app.Configuration["Admin:Password"] ?? app.Configuration["ADMIN_PASSWORD"]);
}
await app.Services.GetRequiredService<ConnectionConfig>().InitAsync(app.Configuration);
await app.Services.GetRequiredService<BrandResolver>().InitAsync(app.Configuration);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
// Behind Cloudflare/NPM which terminate TLS; no https redirect at the app.

// i18n: culture from cookie (de default, en optional).
var cultures = new[] { "de", "en" };
app.UseRequestLocalization(new Microsoft.AspNetCore.Builder.RequestLocalizationOptions()
    .SetDefaultCulture("de").AddSupportedCultures(cultures).AddSupportedUICultures(cultures));

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();

// Language switch: set the culture cookie + redirect back.
app.MapGet("/set-lang", (string c, string? r, HttpContext ctx) =>
{
    var culture = c == "en" ? "en" : "de";
    ctx.Response.Cookies.Append(
        Microsoft.AspNetCore.Localization.CookieRequestCultureProvider.DefaultCookieName,
        Microsoft.AspNetCore.Localization.CookieRequestCultureProvider.MakeCookieValue(new Microsoft.AspNetCore.Localization.RequestCulture(culture)),
        new CookieOptions { Path = "/", Expires = DateTimeOffset.UtcNow.AddYears(1) });
    return Results.Redirect(string.IsNullOrEmpty(r) ? "/" : r);
});

// --- Auth endpoints (form posts from the static-SSR login page) ---
app.MapPost("/auth/login", async (HttpContext ctx, UserService users, LoginThrottle throttle,
    [FromForm] string username, [FromForm] string password, [FromForm] string? totp, [FromForm] string? returnUrl) =>
{
    var key = (username ?? "").Trim().ToLowerInvariant();
    if (throttle.IsLocked(key)) return Results.Redirect("/login?error=locked");
    var u = await users.ValidateAsync(username ?? "", password ?? "");
    if (u is null) { throttle.Fail(key); return Results.Redirect("/login?error=1"); }
    if (!string.IsNullOrEmpty(u.TotpSecret) && !Totp.Verify(u.TotpSecret, totp))
    {
        throttle.Fail(key);
        return Results.Redirect("/login?error=2fa");
    }
    throttle.Reset(key);
    var claims = new List<Claim> { new(ClaimTypes.Name, u.Username), new(ClaimTypes.Role, u.Role) };
    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
    return Results.Redirect(string.IsNullOrWhiteSpace(returnUrl) ? "/dashboard" : returnUrl);
});

app.MapPost("/auth/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/");
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
