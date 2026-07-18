using System.Data;
using System.Data.Common;
using Nelknet.LibSQL.Data;

namespace FantasyBooks.Data;

/// <summary>
/// Wraps <see cref="LibSQLConnection"/> so EF Core's SQLite provider never sees Turso's
/// <c>Auth Token</c> keyword (Microsoft.Data.Sqlite rejects it).
/// </summary>
public sealed class TursoEfConnection : DbConnection
{
    private readonly LibSQLConnection _inner;
    private readonly string _efConnectionString;

    public TursoEfConnection(string httpsDataSource, string authToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(httpsDataSource);
        ArgumentException.ThrowIfNullOrWhiteSpace(authToken);

        _efConnectionString = $"Data Source={httpsDataSource.Trim()}";
        // Nelknet accepts AuthToken (alias of Auth Token); keep it off the string EF inspects.
        _inner = new LibSQLConnection($"Data Source={httpsDataSource.Trim()};AuthToken={authToken.Trim()}");
    }

    public override string ConnectionString
    {
        get => _efConnectionString;
        set
        {
            if (!string.IsNullOrWhiteSpace(value)
                && !string.Equals(value.Trim(), _efConnectionString, StringComparison.OrdinalIgnoreCase))
            {
                throw new NotSupportedException("TursoEfConnection connection string is fixed at construction.");
            }
        }
    }

    public override string Database => _inner.Database;

    public override string DataSource => _inner.DataSource;

    public override string ServerVersion => _inner.ServerVersion;

    public override ConnectionState State => _inner.State;

    public override void ChangeDatabase(string databaseName) => _inner.ChangeDatabase(databaseName);

    public override void Close() => _inner.Close();

    public override void Open() => _inner.Open();

    public override Task OpenAsync(CancellationToken cancellationToken) => _inner.OpenAsync(cancellationToken);

    public override Task CloseAsync() => _inner.CloseAsync();

    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
        _inner.BeginTransaction(isolationLevel);

    protected override DbCommand CreateDbCommand() => _inner.CreateCommand();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _inner.Dispose();
        base.Dispose(disposing);
    }

    public override ValueTask DisposeAsync()
    {
        return _inner.DisposeAsync();
    }
}
