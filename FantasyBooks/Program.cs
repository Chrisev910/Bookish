using System.Globalization;
using FantasyBooks.Data;
using FantasyBooks.Options;
using FantasyBooks.Services;
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

StripeConfiguration.ApiKey = ResolveStripeSecretKey(builder.Configuration);
var portEnv = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(portEnv))
    builder.WebHost.UseUrls($"http://0.0.0.0:{portEnv}");

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
    {
        var sk = Environment.GetEnvironmentVariable("Stripe__SecretKey")
            ?? Environment.GetEnvironmentVariable("STRIPE_SECRET_KEY")
            ?? Environment.GetEnvironmentVariable("STRIPE__SECRET_KEY");
        if (!string.IsNullOrWhiteSpace(sk))
            opts.SecretKey = sk.Trim();
    }

    if (string.IsNullOrWhiteSpace(opts.PublishableKey))
    {
        var pk = Environment.GetEnvironmentVariable("Stripe__PublishableKey")
            ?? Environment.GetEnvironmentVariable("STRIPE_PUBLISHABLE_KEY")
            ?? Environment.GetEnvironmentVariable("STRIPE__PUBLISHABLE_KEY");
        if (!string.IsNullOrWhiteSpace(pk))
            opts.PublishableKey = pk.Trim();
    }
});
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.Cookie.Name = ".FantasyBooks.Session";
    options.IdleTimeout = TimeSpan.FromHours(2);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CartService>();
builder.Services.AddScoped<TikTokIntegrationService>();
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

using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<LibraryContext>();
    context.Database.EnsureCreated();
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
        var secret =
            Environment.GetEnvironmentVariable("Stripe__SecretKey")
            ?? Environment.GetEnvironmentVariable("STRIPE_SECRET_KEY")
            ?? Environment.GetEnvironmentVariable("STRIPE__SECRET_KEY");
        if (!string.IsNullOrWhiteSpace(secret))
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["Stripe:SecretKey"] = secret.Trim() });
    }

    if (string.IsNullOrWhiteSpace(config["Stripe:PublishableKey"]))
    {
        var pk =
            Environment.GetEnvironmentVariable("Stripe__PublishableKey")
            ?? Environment.GetEnvironmentVariable("STRIPE_PUBLISHABLE_KEY")
            ?? Environment.GetEnvironmentVariable("STRIPE__PUBLISHABLE_KEY");
        if (!string.IsNullOrWhiteSpace(pk))
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["Stripe:PublishableKey"] = pk.Trim() });
    }
}

static string ResolveStripeSecretKey(ConfigurationManager config)
{
    var fromConfig = config["Stripe:SecretKey"];
    if (!string.IsNullOrWhiteSpace(fromConfig))
        return fromConfig.Trim();

    var fromEnv = Environment.GetEnvironmentVariable("Stripe__SecretKey")
        ?? Environment.GetEnvironmentVariable("STRIPE_SECRET_KEY")
        ?? Environment.GetEnvironmentVariable("STRIPE__SECRET_KEY");
    return string.IsNullOrWhiteSpace(fromEnv) ? string.Empty : fromEnv.Trim();
}
