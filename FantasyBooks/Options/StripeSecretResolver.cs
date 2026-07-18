using System.Text;
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
            var v = SanitizeKey(Environment.GetEnvironmentVariable(name));
            if (!string.IsNullOrWhiteSpace(v))
                return v;
        }

        foreach (var envName in SecretKeyFileEnvVarNames)
        {
            var path = Environment.GetEnvironmentVariable(envName);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                continue;
            try
            {
                var text = SanitizeKey(File.ReadAllText(path));
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

        return SanitizeKey(configuration["Stripe:SecretKey"]) ?? string.Empty;
    }

    public static string? ReadPublishableKeyFromEnv()
    {
        foreach (var name in PublishableKeyEnvVarNames)
        {
            var v = SanitizeKey(Environment.GetEnvironmentVariable(name));
            if (!string.IsNullOrWhiteSpace(v))
                return v;
        }

        return null;
    }

    public static string ResolvePublishableKey(IConfiguration configuration)
    {
        var fromEnv = ReadPublishableKeyFromEnv();
        if (!string.IsNullOrWhiteSpace(fromEnv))
            return fromEnv;

        return SanitizeKey(configuration["Stripe:PublishableKey"]) ?? string.Empty;
    }

    /// <summary>Safe one-line hint for logs/UI (never the full secret).</summary>
    public static string DescribeKeyPrefix(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return "(empty)";

        var cleaned = SanitizeKey(key) ?? key.Trim();
        if (cleaned.Length == 0)
            return "(empty)";

        var previewLen = Math.Min(12, cleaned.Length);
        var preview = cleaned[..previewLen];
        if (cleaned.StartsWith("pk_", StringComparison.Ordinal))
            return $"{preview}… (this is a publishable key — use the Secret key sk_… instead)";
        if (cleaned.StartsWith("whsec_", StringComparison.Ordinal))
            return $"{preview}… (this is a webhook secret — use the Secret key sk_… instead)";

        return $"{preview}…";
    }

    public static bool LooksLikeStripeSecret(string? secretKey)
    {
        var key = SanitizeKey(secretKey);
        if (string.IsNullOrEmpty(key))
            return false;

        return key.StartsWith("sk_test_", StringComparison.Ordinal)
            || key.StartsWith("sk_live_", StringComparison.Ordinal)
            || key.StartsWith("rk_test_", StringComparison.Ordinal)
            || key.StartsWith("rk_live_", StringComparison.Ordinal);
    }

    /// <summary>Strip quotes, Bearer prefix, and whitespace that Render/dashboard pastes often add.</summary>
    public static string? SanitizeKey(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var s = raw.Trim();

        // BOM
        if (s.Length > 0 && s[0] == '\uFEFF')
            s = s[1..].Trim();

        if ((s.StartsWith('"') && s.EndsWith('"')) || (s.StartsWith('\'') && s.EndsWith('\'')))
            s = s[1..^1].Trim();

        if (s.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            s = s["Bearer ".Length..].Trim();

        // Remove internal whitespace/newlines from accidental multi-line paste
        if (s.Any(char.IsWhiteSpace))
        {
            var sb = new StringBuilder(s.Length);
            foreach (var ch in s)
            {
                if (!char.IsWhiteSpace(ch))
                    sb.Append(ch);
            }
            s = sb.ToString();
        }

        return string.IsNullOrEmpty(s) ? null : s;
    }
}
