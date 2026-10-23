using Aperture.Web.Auth;
using Aperture.Web.Components;
using Aperture.Web.Configuration;
using Aperture.Web.Data;
using Aperture.Web.Features.Attachments;
using Aperture.Web.Features.Backup;
using Aperture.Web.Features.Calendar;
using Aperture.Web.Features.Finance;
using Aperture.Web.Features.Invoices;
using Aperture.Web.Features.Notifications;
using Aperture.Web.Features.Quotes;
using Aperture.Web.Features.Updates;
using Aperture.Web.Installation;
using Aperture.Web.Localization;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Radzen;

// install, update, backup, restore, uninstall: the program installs and maintains itself (see Commands).
if (await Commands.TryRunAsync(args) is { } exitCode)
{
    return exitCode;
}

var builder = WebApplication.CreateBuilder(args);

// An installed copy reads its settings from its home's aperture.json (written by the install command): port, data
// folder, update feed. Environment variables and arguments still win. A development copy uses appsettings only.
var home = ApertureHome.Resolve(ApertureHome.FromArguments(args));
builder.Services.AddSingleton(home);
builder.Services.AddSingleton(Platform.Current());
if (!builder.Environment.IsDevelopment())
{
    builder.Configuration.AddJsonFile(home.SettingsFile, optional: true, reloadOnChange: false);
    builder.Configuration.AddEnvironmentVariables();
    builder.Configuration.AddCommandLine(args);
}

// On Windows Aperture runs without a console window, so it logs to a file (aperture.json's Aperture:LogFile).
if (builder.Configuration["Aperture:LogFile"] is { Length: > 0 } logFile)
{
    builder.Logging.AddProvider(new FileLoggerProvider(logFile));
}

// Data
var paths = AperturePaths.From(builder.Configuration, builder.Environment);
builder.Services.AddSingleton(paths);
// A factory rather than a scoped DbContext: a Blazor Server circuit lives as long as the
// browser tab, so pages create a short-lived context per operation instead.
builder.Services.AddDbContextFactory<ApertureDbContext>(options => options.UseSqlite(paths.ConnectionString));
builder.Services.AddSingleton<AttachmentStore>();
builder.Services.AddSingleton<QuoteService>();
builder.Services.AddSingleton<InvoiceService>();
builder.Services.AddSingleton<TaxExport>();
builder.Services.AddSingleton<BackupDownload>();

// Updates (Settings → Updates): from the public release feed the install command configures; off without one.
var updateOptions = builder.Configuration.GetSection("Updates").Get<UpdateOptions>() ?? new UpdateOptions();
builder.Services.AddSingleton(updateOptions);
builder.Services.AddHttpClient(nameof(ReleaseFeed), ReleaseFeed.Configure);
builder.Services.AddSingleton<UpdateService>();
builder.Services.AddHostedService<UpdateCheckWorker>();

// Notifications in the browser (Settings → Notifications), sent through the browser's push service.
builder.Services.AddHttpClient(nameof(PushService), http => http.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddSingleton<PushService>();
builder.Services.AddSingleton<PushReminders>();
builder.Services.AddHostedService<PushReminderWorker>();

// Free for individuals and businesses under USD 1M annual revenue (questpdf.com/license).
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

// Auth: one user, cookie login, everything requires sign-in unless marked [AllowAnonymous]. The first visit creates
// the login (see LoginStore).
builder.Services.AddSingleton<LoginStore>();
builder.Services.AddSingleton<CredentialValidator>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "aperture.auth";
        options.LoginPath = "/login";
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;
    });
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
builder.Services.AddCascadingAuthenticationState();

builder.Services.AddSingleton(TimeProvider.System);

// Language: English or German, chosen in Settings (AppLanguage); texts in Localization/Texts.de.resx.
builder.Services.AddLocalization();
builder.Services.AddSingleton<AppLanguage>();

// Cookie and antiforgery keys live in the data directory: stable across updates and part of the backup.
builder.Services.AddDataProtection()
    .SetApplicationName("Aperture")
    .PersistKeysToFileSystem(new DirectoryInfo(paths.KeysDirectory));

// UI
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddRadzenComponents();

var app = builder.Build();

await app.InitializeDatabaseAsync();
await app.Services.GetRequiredService<AppLanguage>().LoadAsync();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
// No HTTPS redirection or HSTS: Aperture is plain HTTP on localhost, which browsers treat as secure.
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

// The language from Settings, or the browser's until one is chosen; Swiss formats in either (see Languages).
app.UseRequestLocalization(options =>
{
    options.SupportedCultures = options.SupportedUICultures = [.. Languages.Cultures];
    options.SetDefaultCulture(Language.English.Culture().Name);
    options.RequestCultureProviders = [new AppLanguageCultureProvider()];
});

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets().AllowAnonymous();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// For the update script: is the new version up and can it reach the database?
app.MapGet("/health", async (IDbContextFactory<ApertureDbContext> dbFactory) =>
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Database.CanConnectAsync() ? Results.Text("ok") : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    })
    .AllowAnonymous();

app.MapAttachmentEndpoints();
app.MapQuoteEndpoints();
app.MapInvoiceEndpoints();
app.MapTaxExportEndpoints();
app.MapShootCalendarEndpoints();
app.MapBackupDownloadEndpoints();

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

// Launchers made by an older version are brought up to date (on Windows they opened the browser without starting
// Aperture), in the background once Aperture is up.
if (!app.Environment.IsDevelopment())
{
    app.Lifetime.ApplicationStarted.Register(() => _ = RefreshLauncherAsync(app.Services));
}

app.Run();
return 0;

static async Task RefreshLauncherAsync(IServiceProvider services)
{
    try
    {
        await services.GetRequiredService<Platform>().RefreshLauncherAsync(services.GetRequiredService<ApertureHome>());
    }
    catch (Exception e) when (e is InstallationException or IOException or UnauthorizedAccessException)
    {
        services.GetRequiredService<ILogger<Program>>().LogWarning(e, "Couldn't update the Aperture shortcut");
    }
}
