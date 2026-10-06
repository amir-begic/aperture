using Funk.Web.Auth;
using Funk.Web.Components;
using Funk.Web.Configuration;
using Funk.Web.Data;
using Funk.Web.Features.Attachments;
using Funk.Web.Features.Invoices;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Radzen;

if (args is ["hash-password", ..])
{
    return HashPasswordCommand.Run();
}

var builder = WebApplication.CreateBuilder(args);

// Data
var paths = FunkPaths.From(builder.Configuration, builder.Environment);
builder.Services.AddSingleton(paths);
// A factory rather than a scoped DbContext: a Blazor Server circuit lives as long as the
// browser tab, so pages create a short-lived context per operation instead.
builder.Services.AddDbContextFactory<FunkDbContext>(options => options.UseSqlite(paths.ConnectionString));
builder.Services.AddSingleton<AttachmentStore>();
builder.Services.AddSingleton<InvoiceService>();

// Free for individuals and businesses under USD 1M annual revenue (questpdf.com/license).
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

// Auth: one user, cookie login, everything requires sign-in unless marked [AllowAnonymous].
builder.Services.AddOptions<AuthOptions>()
    .Bind(builder.Configuration.GetSection(AuthOptions.SectionName))
    .Validate(
        o => !string.IsNullOrWhiteSpace(o.Username) && CredentialValidator.IsWellFormedHash(o.PasswordHash),
        "Login is not configured. Run `dotnet run --project src/Funk.Web -- hash-password` and follow the instructions.")
    .ValidateOnStart();
builder.Services.AddSingleton<CredentialValidator>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "funk.auth";
        options.LoginPath = "/login";
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;
    });
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
builder.Services.AddCascadingAuthenticationState();

builder.Services.AddSingleton(TimeProvider.System);

// UI
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddRadzenComponents();
builder.Services.AddRadzenCookieThemeService(options =>
{
    options.Name = "funk.theme";
    options.Duration = TimeSpan.FromDays(365);
});

var app = builder.Build();

await app.InitializeDatabaseAsync();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
// No HTTPS redirection or HSTS: on the LAN the app is plain HTTP, and HTTPS
// is terminated in front of it by Tailscale (see phase 6).
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

// English texts with Swiss formats: dates as 31.12.2026, Monday as first day of the week.
app.UseRequestLocalization("en-CH");

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets().AllowAnonymous();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapAttachmentEndpoints();
app.MapInvoiceEndpoints();

app.MapPost("/logout", async (HttpContext context, IAntiforgery antiforgery) =>
{
    // Checked explicitly: the antiforgery middleware skips requests without a form content type.
    if (!await antiforgery.IsRequestValidAsync(context))
    {
        return Results.BadRequest();
    }

    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.LocalRedirect("~/login");
});

app.Run();
return 0;
