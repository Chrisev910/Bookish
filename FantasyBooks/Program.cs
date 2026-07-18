using System.Globalization;
using FantasyBooks.Data;
using FantasyBooks.Options;
using FantasyBooks.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using Stripe;

var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddJsonFile("appsettings.Development.local.json", optional: true, reloadOnChange: true);
}

ApplyStripeFromEnvironment(builder.Configuration);
ApplyPublicBaseUrlFromEnvironment(builder.Configuration);
ApplyAdminFromEnvironment(builder.Configuration);

var resolvedStripeSecretKey = StripeSecretResolver.ResolveSecretKey(builder.Configuration);
StripeConfiguration.ApiKey = resolvedStripeSecretKey;

var runningBehindProxy =
    !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RENDER"))
    || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PORT"));

var portEnv = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(portEnv))
    builder.WebHost.UseUrls($"http://0.0.0.0:{portEnv}");

var dataProtectionKeysDir = ResolveDataProtectionKeysDirectory(builder.Environment.ContentRootPath);
try
{
    Directory.CreateDirectory(dataProtectionKeysDir);
    builder.Services.AddDataProtection()
        .SetApplicationName("FantasyBooks")
        .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysDir));
}
catch
{
    // If the filesystem is read-only, keys stay ephemeral (antiforgery may break across restarts).
}

builder.Services.Configure<FormOptions>(options =>
{
    options.ValueLengthLimit = 10 * 1024 * 1024;
    options.MultipartBodyLengthLimit = 20 * 1024 * 1024;
});

builder.Services.AddLibraryDatabase(builder.Configuration);

builder.Services.Configure<StripeOptions>(builder.Configuration.GetSection(StripeOptions.SectionName));
builder.Services.PostConfigure<StripeOptions>(opts =>
{
    if (string.IsNullOrWhiteSpace(opts.SecretKey))
        opts.SecretKey = StripeSecretResolver.ResolveSecretKey(builder.Configuration);

    if (string.IsNullOrWhiteSpace(opts.PublishableKey))
        opts.PublishableKey = StripeSecretResolver.ResolvePublishableKey(builder.Configuration);
});

builder.Services.Configure<AdminOptions>(builder.Configuration.GetSection(AdminOptions.SectionName));

// Behind Render, Request.IsHttps can still be wrong when cookies are written. SameAsRequest
// avoids dropping Secure session/antiforgery cookies on the internal HTTP hop.
var cookieSecurePolicy = builder.Environment.IsDevelopment() || runningBehindProxy
    ? CookieSecurePolicy.SameAsRequest
    : CookieSecurePolicy.Always;

builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.Cookie.Name = ".FantasyBooks.Session";
    options.IdleTimeout = TimeSpan.FromHours(2);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = cookieSecurePolicy;
});
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient(ProductRemoteImageFetcher.HttpClientName, client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("BookishInkPaper/1.0 (+https://github.com/Chrisev910/Bookish)");
});
builder.Services.AddScoped<ProductRemoteImageFetcher>();
builder.Services.AddScoped<CartService>();
builder.Services.AddScoped<StripeCheckoutService>();
builder.Services.AddScoped<TikTokIntegrationService>();
builder.Services.AddAntiforgery(options =>
{
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = cookieSecurePolicy;
});

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Admin/Login";
        options.LogoutPath = "/Admin/Logout";
        options.AccessDeniedPath = "/Admin/Login";
        options.Cookie.Name = ".FantasyBooks.Admin";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = cookieSecurePolicy;
        options.ExpireTimeSpan = TimeSpan.FromDays(14);
        options.SlidingExpiration = true;
    });

builder.Services.AddAuthorization();

builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/Admin");
    options.Conventions.AllowAnonymousToPage("/Admin/Login");
    options.Conventions.AllowAnonymousToPage("/Admin/Logout");
});
builder.Services.AddControllersWithViews();

builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    var enGb = CultureInfo.GetCultureInfo("en-GB");
    options.DefaultRequestCulture = new RequestCulture(enGb);
    options.SupportedCultures = [enGb];
    options.SupportedUICultures = [enGb];
});

// Render (and similar hosts) terminate TLS and forward HTTP to the container.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
        | ForwardedHeaders.XForwardedProto
        | ForwardedHeaders.XForwardedHost;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
    options.RequireHeaderSymmetry = false;
});

var app = builder.Build();

if (string.IsNullOrWhiteSpace(resolvedStripeSecretKey) && app.Environment.IsProduction())
{
    var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("FantasyBooks.Stripe");
    logger.LogWarning(
        "Stripe secret key is missing. Set Stripe__SecretKey or STRIPE_SECRET_KEY (or STRIPE_SECRET_KEY_FILE) on the host, then redeploy.");
}

using (var scope = app.Services.CreateScope())
{
    var dbInfo = scope.ServiceProvider.GetRequiredService<LibraryDatabaseInfo>();
    var context = scope.ServiceProvider.GetRequiredService<LibraryContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("FantasyBooks.Database");
    logger.LogInformation("Library database: {Description}", dbInfo.Description);

    context.Database.EnsureCreated();
    if (!dbInfo.IsRemoteTurso)
    {
        await context.Database.ExecuteSqlRawAsync("PRAGMA busy_timeout = 5000;");
        await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode = WAL;");
    }

    await LibrarySchemaPatch.ApplyAsync(context, logger: logger);
    SeedData.Initialize(context);
}

// Forwarded headers must run before anything that reads Scheme/Host (cookies, HTTPS redirection, Stripe URLs).
app.UseForwardedHeaders();

if (runningBehindProxy)
{
    app.Use(async (context, next) =>
    {
        ApplyForwardedRequestFields(context.Request);
        await next();
    });
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

// Behind Render, TLS is already terminated at the proxy. Redirecting HTTP→HTTPS here
// rewrites checkout POSTs and commonly breaks antiforgery validation.
if (!runningBehindProxy)
    app.UseHttpsRedirection();

app.UseRouting();

app.UseRequestLocalization();

app.UseSession();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapControllers();
app.MapRazorPages()
    .WithStaticAssets();

app.Run();

static void ApplyStripeFromEnvironment(ConfigurationManager config)
{
    if (string.IsNullOrWhiteSpace(config["Stripe:SecretKey"]))
    {
        var secret = StripeSecretResolver.ReadSecretKeyFromEnvAndFile();
        if (!string.IsNullOrWhiteSpace(secret))
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["Stripe:SecretKey"] = secret });
    }

    if (string.IsNullOrWhiteSpace(config["Stripe:PublishableKey"]))
    {
        var pk = StripeSecretResolver.ReadPublishableKeyFromEnv();
        if (!string.IsNullOrWhiteSpace(pk))
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["Stripe:PublishableKey"] = pk });
    }
}

static void ApplyPublicBaseUrlFromEnvironment(ConfigurationManager config)
{
    if (!string.IsNullOrWhiteSpace(config["App:PublicBaseUrl"]))
        return;

    var renderUrl = Environment.GetEnvironmentVariable("RENDER_EXTERNAL_URL")?.Trim();
    if (!string.IsNullOrWhiteSpace(renderUrl))
    {
        config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["App:PublicBaseUrl"] = renderUrl.TrimEnd('/'),
        });
    }
}

static void ApplyAdminFromEnvironment(ConfigurationManager config)
{
    var updates = new Dictionary<string, string?>();

    if (string.IsNullOrWhiteSpace(config["Admin:Username"]))
    {
        var user = Environment.GetEnvironmentVariable("ADMIN_USERNAME")
            ?? Environment.GetEnvironmentVariable("Admin__Username");
        if (!string.IsNullOrWhiteSpace(user))
            updates["Admin:Username"] = user.Trim();
    }

    if (string.IsNullOrWhiteSpace(config["Admin:Password"]))
    {
        var pass = Environment.GetEnvironmentVariable("ADMIN_PASSWORD")
            ?? Environment.GetEnvironmentVariable("Admin__Password");
        if (!string.IsNullOrWhiteSpace(pass))
            updates["Admin:Password"] = pass;
    }

    var tursoUrl = Environment.GetEnvironmentVariable("TURSO_DATABASE_URL")
        ?? Environment.GetEnvironmentVariable("Turso__DatabaseUrl");
    if (!string.IsNullOrWhiteSpace(tursoUrl) && string.IsNullOrWhiteSpace(config["Turso:DatabaseUrl"]))
        updates["Turso:DatabaseUrl"] = tursoUrl.Trim();

    var tursoToken = Environment.GetEnvironmentVariable("TURSO_AUTH_TOKEN")
        ?? Environment.GetEnvironmentVariable("Turso__AuthToken");
    if (!string.IsNullOrWhiteSpace(tursoToken) && string.IsNullOrWhiteSpace(config["Turso:AuthToken"]))
        updates["Turso:AuthToken"] = tursoToken.Trim();

    if (updates.Count > 0)
        config.AddInMemoryCollection(updates);
}

static string ResolveDataProtectionKeysDirectory(string contentRootPath)
{
    var configured = Environment.GetEnvironmentVariable("DATA_PROTECTION_KEYS_PATH")?.Trim();
    if (!string.IsNullOrWhiteSpace(configured))
        return configured;

    // Optional persistent disk mount used on Render.
    const string renderDataKeys = "/data/dp-keys";
    if (Directory.Exists("/data") || Directory.Exists(renderDataKeys))
        return renderDataKeys;

    return Path.Combine(contentRootPath, "dp-keys");
}

static void ApplyForwardedRequestFields(HttpRequest request)
{
    try
    {
        if (request.Headers.TryGetValue("X-Forwarded-Proto", out var forwardedProto))
        {
            var proto = forwardedProto.ToString().Split(',', 2)[0].Trim();
            if (!string.IsNullOrEmpty(proto))
                request.Scheme = proto;
        }

        if (request.Headers.TryGetValue("X-Forwarded-Host", out var forwardedHost))
        {
            var host = forwardedHost.ToString().Split(',', 2)[0].Trim();
            if (!string.IsNullOrEmpty(host))
                request.Host = HostString.FromUriComponent(host);
        }
    }
    catch
    {
        // Ignore malformed forwarded headers; PublicBaseUrl / RENDER_EXTERNAL_URL still apply.
    }
}
