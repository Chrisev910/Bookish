using System.Data;
using System.Data.Common;
using Nelknet.LibSQL.Data;

namespace FantasyBooks.Data;

/// <summary>
/// Wraps <see cref="LibSQLConnection"/> so EF Core's SQLite provider never sees Turso's
/// <c>AuthToken</c> keyword, and so commands/transactions report this connection (not the inner one).
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
        _inner = new LibSQLConnection($"Data Source={httpsDataSource.Trim()};AuthToken={authToken.Trim()}");
    }

    internal LibSQLConnection Inner => _inner;

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
        new TursoEfTransaction(_inner.BeginTransaction(isolationLevel), this);

    protected override DbCommand CreateDbCommand()
    {
        var cmd = _inner.CreateCommand();
        // LibSQL sometimes returns commands with a null Connection; EF then NREs on execute.
        if (cmd.Connection is null)
            cmd.Connection = _inner;
        return new TursoEfCommand(cmd, this);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _inner.Dispose();
        base.Dispose(disposing);
    }

    public override ValueTask DisposeAsync() => _inner.DisposeAsync();
}

internal sealed class TursoEfTransaction(DbTransaction inner, TursoEfConnection owner) : DbTransaction
{
    private bool _disposed;

    protected override DbConnection DbConnection => owner;

    public override IsolationLevel IsolationLevel => inner.IsolationLevel;

    internal DbTransaction Inner => inner;

    public override void Commit() => inner.Commit();

    public override void Rollback() => inner.Rollback();

    public override Task CommitAsync(CancellationToken cancellationToken = default) =>
        inner.CommitAsync(cancellationToken);

    public override Task RollbackAsync(CancellationToken cancellationToken = default) =>
        inner.RollbackAsync(cancellationToken);

    protected override void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            inner.Dispose();
            _disposed = true;
        }

        base.Dispose(disposing);
    }
}

internal sealed class TursoEfCommand(DbCommand inner, TursoEfConnection owner) : DbCommand
{
    private TursoEfTransaction? _transaction;

    public override string CommandText
    {
        get => inner.CommandText;
        set => inner.CommandText = value;
    }

    public override int CommandTimeout
    {
        get => inner.CommandTimeout;
        set => inner.CommandTimeout = value;
    }

    public override CommandType CommandType
    {
        get => inner.CommandType;
        set => inner.CommandType = value;
    }

    public override bool DesignTimeVisible
    {
        get => inner.DesignTimeVisible;
        set => inner.DesignTimeVisible = value;
    }

    public override UpdateRowSource UpdatedRowSource
    {
        get => inner.UpdatedRowSource;
        set => inner.UpdatedRowSource = value;
    }

    protected override DbConnection? DbConnection
    {
        get => owner;
        set
        {
            if (value is null || ReferenceEquals(value, owner))
                return;
            throw new InvalidOperationException("TursoEfCommand is bound to its owner connection.");
        }
    }

    protected override DbParameterCollection DbParameterCollection => inner.Parameters;

    protected override DbTransaction? DbTransaction
    {
        get => _transaction;
        set
        {
            _transaction = value as TursoEfTransaction;
            if (value is null)
            {
                inner.Transaction = null;
                return;
            }

            if (_transaction is null)
                throw new InvalidOperationException("Transaction must be created from the Turso connection.");

            // Enlist the inner command on the inner LibSQL transaction.
            inner.Transaction = _transaction.Inner;
        }
    }

    public override void Cancel() => inner.Cancel();

    public override int ExecuteNonQuery()
    {
        EnsureInnerConnection();
        return inner.ExecuteNonQuery();
    }

    public override object? ExecuteScalar()
    {
        EnsureInnerConnection();
        return inner.ExecuteScalar();
    }

    public override void Prepare()
    {
        EnsureInnerConnection();
        inner.Prepare();
    }

    protected override DbParameter CreateDbParameter() => inner.CreateParameter();

    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
    {
        EnsureInnerConnection();
        return inner.ExecuteReader(behavior);
    }

    public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
    {
        EnsureInnerConnection();
        return inner.ExecuteNonQueryAsync(cancellationToken);
    }

    public override Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken)
    {
        EnsureInnerConnection();
        return inner.ExecuteScalarAsync(cancellationToken);
    }

    protected override Task<DbDataReader> ExecuteDbDataReaderAsync(
        CommandBehavior behavior,
        CancellationToken cancellationToken)
    {
        EnsureInnerConnection();
        return inner.ExecuteReaderAsync(behavior, cancellationToken);
    }

    private void EnsureInnerConnection()
    {
        if (inner.Connection is null)
            inner.Connection = owner.Inner;

        if (owner.State != ConnectionState.Open)
            owner.Open();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            inner.Dispose();
        base.Dispose(disposing);
    }
}
