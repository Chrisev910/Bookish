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

var resolvedStripeSecretKey = StripeSecretResolver.ResolveSecretKey(builder.Configuration);
StripeConfiguration.ApiKey = resolvedStripeSecretKey;
var portEnv = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(portEnv))
    builder.WebHost.UseUrls($"http://0.0.0.0:{portEnv}");

var dataProtectionKeysDir = Path.Combine(builder.Environment.ContentRootPath, "dp-keys");
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
    if (!builder.Environment.IsDevelopment())
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
});
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CartService>();
builder.Services.AddScoped<TikTokIntegrationService>();
builder.Services.AddAntiforgery(options =>
{
    if (!builder.Environment.IsDevelopment())
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
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

if (!builder.Environment.IsDevelopment())
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
    });
}

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

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseForwardedHeaders();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

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
