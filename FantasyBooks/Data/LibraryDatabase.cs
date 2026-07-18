namespace FantasyBooks.Data;

/// <summary>Resolves whether the library DB is local SQLite or remote Turso.</summary>
public sealed class LibraryDatabaseInfo
{
    public required bool IsRemoteTurso { get; init; }
    public required string Description { get; init; }
}

public static class LibraryDatabase
{
    public static (string? Url, string? AuthToken) ReadTursoEnv(IConfiguration? configuration = null)
    {
        var url = FirstNonEmpty(
            configuration?["Turso:DatabaseUrl"],
            configuration?["TURSO_DATABASE_URL"],
            Environment.GetEnvironmentVariable("TURSO_DATABASE_URL"),
            Environment.GetEnvironmentVariable("Turso__DatabaseUrl"));

        var token = FirstNonEmpty(
            configuration?["Turso:AuthToken"],
            configuration?["TURSO_AUTH_TOKEN"],
            Environment.GetEnvironmentVariable("TURSO_AUTH_TOKEN"),
            Environment.GetEnvironmentVariable("Turso__AuthToken"));

        return (url, token);
    }

    public static bool IsTursoConfigured(IConfiguration? configuration = null)
    {
        var (url, token) = ReadTursoEnv(configuration);
        return !string.IsNullOrWhiteSpace(url) && !string.IsNullOrWhiteSpace(token);
    }

    /// <summary>libsql://… → https://… for HTTP / Nelknet remote connections.</summary>
    public static string ToHttpsDataSource(string tursoUrl)
    {
        var url = tursoUrl.Trim().TrimEnd('/');
        if (url.StartsWith("libsql://", StringComparison.OrdinalIgnoreCase))
            return "https://" + url["libsql://".Length..];
        return url;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var v in values)
        {
            if (!string.IsNullOrWhiteSpace(v))
                return v.Trim();
        }

        return null;
    }
}
