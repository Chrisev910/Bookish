using Microsoft.EntityFrameworkCore;

namespace FantasyBooks.Data;

public static class LibraryDbContextRegistration
{
    public static IServiceCollection AddLibraryDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        var (tursoUrl, tursoToken) = LibraryDatabase.ReadTursoEnv(configuration);
        var useTurso = !string.IsNullOrWhiteSpace(tursoUrl) && !string.IsNullOrWhiteSpace(tursoToken);

        services.AddSingleton(new LibraryDatabaseInfo
        {
            IsRemoteTurso = useTurso,
            Description = useTurso ? "Turso (remote)" : "SQLite (local file)",
        });

        if (useTurso)
        {
            var dataSource = LibraryDatabase.ToHttpsDataSource(tursoUrl!);
            var authToken = tursoToken!;

            services.AddDbContext<LibraryContext>((_, options) =>
            {
                // Must not put AuthToken in a string that EF/Microsoft.Data.Sqlite parses.
                var connection = new TursoEfConnection(dataSource, authToken);
                options.UseSqlite(connection, contextOwnsConnection: true);
            });
        }
        else
        {
            var local = configuration.GetConnectionString("Library") ?? "Data Source=library.db";
            services.AddDbContext<LibraryContext>(options => options.UseSqlite(local));
        }

        return services;
    }
}
