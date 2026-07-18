using System.Globalization;
using FantasyBooks.Data;
using FantasyBooks.Options;
using FantasyBooks.Services;
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

builder.Services.AddDbContext<LibraryContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Library")));

builder.Services.Configure<StripeOptions>(builder.Configuration.GetSection(StripeOptions.SectionName));
builder.Services.PostConfigure<StripeOptions>(opts =>
{
    if (string.IsNullOrWhiteSpace(opts.SecretKey))
        opts.SecretKey = StripeSecretResolver.ResolveSecretKey(builder.Configuration);

    if (string.IsNullOrWhiteSpace(opts.PublishableKey))
        opts.PublishableKey = StripeSecretResolver.ResolvePublishableKey(builder.Configuration);
});
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.Cookie.Name = ".FantasyBooks.Session";
    options.IdleTimeout = TimeSpan.FromHours(2);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
});
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CartService>();
builder.Services.AddScoped<TikTokIntegrationService>();
builder.Services.AddAntiforgery(options =>
{
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
});

builder.Services.AddRazorPages();
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
    var context = scope.ServiceProvider.GetRequiredService<LibraryContext>();
    context.Database.EnsureCreated();
    await context.Database.ExecuteSqlRawAsync("PRAGMA busy_timeout = 5000;");
    await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode = WAL;");
    await LibrarySchemaPatch.ApplyAsync(context);
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
