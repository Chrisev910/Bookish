using Microsoft.EntityFrameworkCore;
using Nelknet.LibSQL.Data;

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
            var connectionString = $"Data Source={dataSource};Auth Token={authToken}";

            services.AddDbContext<LibraryContext>((_, options) =>
            {
                var connection = new LibSQLConnection(connectionString);
                // EF Core opens/closes the connection per operation; owning it ensures Dispose on context dispose.
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
