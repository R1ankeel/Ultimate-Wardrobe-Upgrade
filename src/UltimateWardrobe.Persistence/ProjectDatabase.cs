using Microsoft.Data.Sqlite;

namespace UltimateWardrobe.Persistence;

/// <summary>
/// Opens and owns the single long-lived <see cref="SqliteConnection"/> for one <c>project.db</c>
/// (Phase 4 Sprint 4.0.3). Bootstrap responsibilities only: create/ensure the file, apply the
/// connection-level pragmas every consumer needs, and detect whether the database is empty
/// (no user tables yet, so migrations must create the schema - Sprint 4.1).
///
/// Pragmas (Phase 4 plan - issue 3 and the migration plan):
/// - <c>PRAGMA journal_mode=WAL</c> - crash safety plus concurrent reader/writer (persistent DB
///   property; set once at open).
/// - <c>PRAGMA foreign_keys=ON</c> - connection-level, MUST be set on every connection. SQLite does
///   not enforce <c>REFERENCES</c> by default; without it the FK tests and the delete-ordering
///   guarantees are meaningless. <c>foreign_keys</c> stays ON for the connection's lifetime.
///
/// The transaction-level <c>PRAGMA defer_foreign_keys=ON</c> is NOT set here - that pragma is reset
/// to OFF after every commit/rollback and must therefore be re-issued at the start of each
/// transaction by <c>UnitOfWork</c> (Sprint 4.2 / SaveAsync, Sprint 4.3.1 implementation note).
/// </summary>
public sealed class ProjectDatabase : IAsyncDisposable
{
    private const string WAL = "wal";
    private const string ForeignKeysOn = "1";

    private readonly string _dbPath;
    private readonly SqliteConnection _connection;

    private ProjectDatabase(string dbPath, SqliteConnection connection)
    {
        _dbPath = dbPath;
        _connection = connection;
    }

    /// <summary>The absolute path of the opened <c>project.db</c> file.</summary>
    public string DbPath => _dbPath;

    /// <summary>The single owner connection for this database. Consumers must not dispose it.</summary>
    public SqliteConnection Connection => _connection;

    /// <summary>
    /// True when the database has no user tables yet (a fresh file or an empty one) and therefore
    /// needs the migration set to build the schema (Sprint 4.1).
    /// </summary>
    public bool IsEmpty { get; private set; }

    /// <summary>
    /// Opens (creating if necessary) the database at <paramref name="path"/>, applies the
    /// connection-level pragmas, and detects emptiness. A missing parent directory is created; an
    /// unopenable/locked file or a corruption that prevents opening throws
    /// <see cref="ProjectStoreException"/>.
    /// </summary>
    public static async Task<ProjectDatabase> OpenAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Database path must not be empty.", nameof(path));
        }

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Pooling=False is intentional: ProjectDatabase owns exactly one long-lived connection for
        // the database's lifetime, so ADO.NET pooling would only keep the native -wal/-shm file
        // handles open after Dispose (leaving the .db locked on Windows). Disabling it makes
        // DisposeAsync truly close the file so temp/test databases can be cleaned up.
        var connection = new SqliteConnection($"Data Source={fullPath};Pooling=False");

        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await ApplyPragmasAsync(connection, cancellationToken).ConfigureAwait(false);

            var database = new ProjectDatabase(fullPath, connection);
            // Emptiness is snapshotted BEFORE migration so a brand-new DB reads as empty (the
            // bootstrap signal), even though M001 immediately builds the schema on the same open.
            database.IsEmpty = await HasNoUserTablesAsync(connection, cancellationToken).ConfigureAwait(false);

            // Sprint 4.1: run pending migrations (fresh DB -> M001_Initial; existing -> missing
            // versions only; newer DB -> fail-fast). No-op when already current.
            await Migrations.Migrator.CreateDefault()
                .MigrateAsync(connection, fullPath, cancellationToken)
                .ConfigureAwait(false);

            return database;
        }
        catch (ProjectStoreException)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        catch (SqliteException ex)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw new ProjectStoreException($"Unable to open project database '{fullPath}'.", ex);
        }
    }

    /// <summary>Closes and disposes the owned connection.</summary>
    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync().ConfigureAwait(false);
    }

    private static async Task ApplyPragmasAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await ExecuteScalarAsync<string?>(connection, "PRAGMA journal_mode=WAL;", cancellationToken).ConfigureAwait(false);
        await ExecuteNonQueryAsync(connection, $"PRAGMA foreign_keys={ForeignKeysOn};", cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> HasNoUserTablesAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        const string sql = "SELECT count(*) FROM sqlite_master WHERE type='table';";
        var count = Convert.ToInt32(await ExecuteScalarAsync<object>(connection, sql, cancellationToken).ConfigureAwait(false));
        return count == 0;
    }

    private static async Task<T?> ExecuteScalarAsync<T>(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is null ? default : (T)result;
    }

    private static async Task ExecuteNonQueryAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
