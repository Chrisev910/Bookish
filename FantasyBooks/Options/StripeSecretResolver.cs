using Microsoft.Extensions.Configuration;

namespace FantasyBooks.Options;

/// <summary>
/// Resolves Stripe keys for production hosts (Render, Docker). Environment variables and secret files
/// are checked before configuration so empty JSON placeholders cannot hide real host secrets.
/// </summary>
public static class StripeSecretResolver
{
    private static readonly string[] SecretKeyEnvVarNames =
    [
        "Stripe__SecretKey",
        "STRIPE_SECRET_KEY",
        "STRIPE__SECRET_KEY",
    ];

    private static readonly string[] SecretKeyFileEnvVarNames =
    [
        "STRIPE_SECRET_KEY_FILE",
        "Stripe__SecretKey__File",
    ];

    private static readonly string[] PublishableKeyEnvVarNames =
    [
        "Stripe__PublishableKey",
        "STRIPE_PUBLISHABLE_KEY",
        "STRIPE__PUBLISHABLE_KEY",
    ];

    public static string? ReadSecretKeyFromEnvAndFile()
    {
        foreach (var name in SecretKeyEnvVarNames)
        {
            var v = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(v))
                return v.Trim();
        }

        foreach (var envName in SecretKeyFileEnvVarNames)
        {
            var path = Environment.GetEnvironmentVariable(envName);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                continue;
            try
            {
                var text = File.ReadAllText(path).Trim();
                if (!string.IsNullOrWhiteSpace(text))
                    return text;
            }
            catch
            {
                // Misconfigured path or permissions; fall through.
            }
        }

        return null;
    }

    public static string ResolveSecretKey(IConfiguration configuration)
    {
        var fromEnv = ReadSecretKeyFromEnvAndFile();
        if (!string.IsNullOrWhiteSpace(fromEnv))
            return fromEnv;

        var fromConfig = configuration["Stripe:SecretKey"];
        return string.IsNullOrWhiteSpace(fromConfig) ? string.Empty : fromConfig.Trim();
    }

    public static string? ReadPublishableKeyFromEnv()
    {
        foreach (var name in PublishableKeyEnvVarNames)
        {
            var v = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(v))
                return v.Trim();
        }

        return null;
    }

    public static string ResolvePublishableKey(IConfiguration configuration)
    {
        var fromEnv = ReadPublishableKeyFromEnv();
        if (!string.IsNullOrWhiteSpace(fromEnv))
            return fromEnv;

        var fromConfig = configuration["Stripe:PublishableKey"];
        return string.IsNullOrWhiteSpace(fromConfig) ? string.Empty : fromConfig.Trim();
    }
}
