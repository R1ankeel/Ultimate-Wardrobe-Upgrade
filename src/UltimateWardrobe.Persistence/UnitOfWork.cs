using Microsoft.Data.Sqlite;

namespace UltimateWardrobe.Persistence;

/// <summary>
/// Transaction boundary over a <see cref="ProjectDatabase"/>'s single long-lived connection
/// (Phase 4 Sprint 4.2.1 / roadmap section 6.4 unit-of-work). Repositories receive a
/// <c>UnitOfWork</c> and bind their commands to <see cref="Transaction"/>; when no transaction is
/// active the connection is in auto-commit and <see cref="Transaction"/> is null.
///
/// The connection is owned by <see cref="ProjectDatabase"/>, so the <c>UnitOfWork</c> never closes
/// it - disposal only rolls back an in-flight transaction if the caller forgot to commit.
/// </summary>
public sealed class UnitOfWork
{
    private readonly SqliteConnection _connection;
    private SqliteTransaction? _transaction;

    public UnitOfWork(SqliteConnection connection)
    {
        _connection = connection;
    }

    public SqliteConnection Connection => _connection;

    public SqliteTransaction? Transaction => _transaction;

    /// <summary>
    /// Begins a transaction and re-issues <c>PRAGMA defer_foreign_keys=ON</c> right after
    /// <c>BEGIN</c> (plan 4.3.1 implementation note): <c>defer_foreign_keys</c> is a
    /// TRANSACTION-level pragma that SQLite resets to OFF after every commit/rollback, so it must be
    /// re-applied at the start of EACH new transaction on the long-lived connection, not once at
    /// open. This keeps FK checks deferred within the transaction (upsert-only Save ordering needs it).
    /// </summary>
    public async Task BeginAsync(CancellationToken cancellationToken)
    {
        if (_transaction is not null)
        {
            throw new InvalidOperationException("A transaction is already active.");
        }

        _transaction = (SqliteTransaction)await _connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await using var command = _connection.CreateCommand();
        command.Transaction = _transaction;
        command.CommandText = "PRAGMA defer_foreign_keys=ON;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        EnsureActive();
        await _transaction!.CommitAsync(cancellationToken).ConfigureAwait(false);
        await _transaction.DisposeAsync().ConfigureAwait(false);
        _transaction = null;
    }

    public async Task RollbackAsync(CancellationToken cancellationToken)
    {
        EnsureActive();
        await _transaction!.RollbackAsync(cancellationToken).ConfigureAwait(false);
        await _transaction.DisposeAsync().ConfigureAwait(false);
        _transaction = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_transaction is not null)
        {
            await RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private void EnsureActive()
    {
        if (_transaction is null)
        {
            throw new InvalidOperationException("No transaction is active.");
        }
    }
}
