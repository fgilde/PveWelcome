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

var dbPath = builder.Configuration["Db:Path"] ?? "/data/pvewelcome.db";
builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite($"Data Source={dbPath}"));

var keysDir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(dbPath)) ?? ".", "keys");
Directory.CreateDirectory(keysDir);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keysDir))
    .SetApplicationName("PveWelcome");
builder.Services.AddScoped<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<AiService>();
builder.Services.AddScoped<ScriptService>();
builder.Services.AddScoped<MonitorService>();
builder.Services.AddSingleton<LoginThrottle>();

builder.Services.AddSingleton<ConnectionConfig>();
builder.Services.AddSingleton<BrandResolver>();
builder.Services.AddSingleton<LandingService>();
builder.Services.AddSingleton<NotificationService>();
builder.Services.AddSingleton<PveDataService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<PveDataService>());

builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

builder.Services.AddHttpClient<PveClient>(c => c.Timeout = TimeSpan.FromSeconds(15))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    });
builder.Services.AddHttpClient<NpmClient>(c => c.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddHttpClient("reach", c => c.Timeout = TimeSpan.FromSeconds(8))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddHttpClient("notify", c => c.Timeout = TimeSpan.FromSeconds(10));

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

using (var scope = app.Services.CreateScope())
{
    var users = scope.ServiceProvider.GetRequiredService<UserService>();
    await users.InitAsync(
        app.Configuration["Admin:User"] ?? app.Configuration["ADMIN_USER"],
        app.Configuration["Admin:Password"] ?? app.Configuration["ADMIN_PASSWORD"]);
}
await app.Services.GetRequiredService<ConnectionConfig>().InitAsync(app.Configuration);
await app.Services.GetRequiredService<BrandResolver>().InitAsync(app.Configuration);
await app.Services.GetRequiredService<LandingService>().InitAsync();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

var cultures = new[] { "de", "en" };
app.UseRequestLocalization(new Microsoft.AspNetCore.Builder.RequestLocalizationOptions()
    .SetDefaultCulture("de").AddSupportedCultures(cultures).AddSupportedUICultures(cultures));

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();

app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/mcp"))
    {
        var token = ctx.RequestServices.GetRequiredService<PveWelcome.Services.ConnectionConfig>().Current.McpToken;
        var presented = ctx.Request.Query["key"].ToString();
        var authz = ctx.Request.Headers.Authorization.ToString();
        if (presented.Length == 0 && authz.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            presented = authz["Bearer ".Length..].Trim();
        if (string.IsNullOrEmpty(token) || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(presented), System.Text.Encoding.UTF8.GetBytes(token)))
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await ctx.Response.WriteAsync("Unauthorized: MCP token required (?key= or Authorization: Bearer).");
            return;
        }
    }
    await next();
});
app.MapMcp("/mcp");

app.MapGet("/set-lang", async (string c, string? r, PveWelcome.Services.ConnectionConfig conn) =>
{
    await conn.SetLanguageAsync(c);
    return Results.Redirect(string.IsNullOrEmpty(r) ? "/" : r);
});

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
