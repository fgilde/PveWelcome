using System.Security.Claims;
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
builder.Services.AddScoped<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
builder.Services.AddScoped<UserService>();

// --- Options ---
builder.Services.Configure<PveOptions>(builder.Configuration.GetSection("Pve"));
builder.Services.Configure<NpmOptions>(builder.Configuration.GetSection("Npm"));
builder.Services.AddSingleton<BrandResolver>();

// --- API clients ---
builder.Services.AddHttpClient<PveClient>((sp, c) =>
{
    var o = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<PveOptions>>().Value;
    if (!string.IsNullOrWhiteSpace(o.BaseUrl)) c.BaseAddress = new Uri(o.BaseUrl);
    if (!string.IsNullOrWhiteSpace(o.ApiToken)) c.DefaultRequestHeaders.Authorization = PveClient.BuildAuth(o.ApiToken);
    c.Timeout = TimeSpan.FromSeconds(15);
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    // PVE uses a self-signed cert on the internal network.
    ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
});
builder.Services.AddHttpClient<NpmClient>(c => c.Timeout = TimeSpan.FromSeconds(15));

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

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
// Behind Cloudflare/NPM which terminate TLS; no https redirect at the app.

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();

// --- Auth endpoints (form posts from the static-SSR login page) ---
app.MapPost("/auth/login", async (HttpContext ctx, UserService users,
    [FromForm] string username, [FromForm] string password, [FromForm] string? returnUrl) =>
{
    var u = await users.ValidateAsync(username ?? "", password ?? "");
    if (u is null) return Results.Redirect("/login?error=1");
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
