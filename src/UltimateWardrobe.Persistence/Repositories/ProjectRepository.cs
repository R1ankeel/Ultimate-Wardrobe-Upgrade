using Microsoft.Data.Sqlite;
using UltimateWardrobe.Core.Domain;

namespace UltimateWardrobe.Persistence.Repositories;

/// <summary>
/// CRUD for the <c>Project</c> table (Phase 4 Sprint 4.2.1). Upsert is stable by domain <c>Id</c>
/// (issue 3 - no delete-then-reinsert) so an idempotent <c>SaveAsync</c> never orphans dependents.
/// A loaded <see cref="Project"/> has its stored fields but an untouched (empty) library/overhauls
/// graph - the repositories reconstruct those per-aggregate and <c>IProjectStore.LoadAsync</c>
/// (Sprint 4.3) assembles the full graph from them.
/// </summary>
public sealed class ProjectRepository
{
    private readonly UnitOfWork _uow;

    public ProjectRepository(UnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task UpsertAsync(Project project, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO Project (Id, Name, RootPath, SchemaVersion, CreatedAt, ModifiedAt)
            VALUES ($id, $name, $root, $schema, $created, $modified)
            ON CONFLICT(Id) DO UPDATE SET
              Name = excluded.Name,
              RootPath = excluded.RootPath,
              SchemaVersion = excluded.SchemaVersion,
              ModifiedAt = excluded.ModifiedAt;
            """;

        await using var command = _uow.Connection.CreateCommand();
        command.Transaction = _uow.Transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue("$id", project.Id.ToString());
        command.Parameters.AddWithValue("$name", project.Name);
        command.Parameters.AddWithValue("$root", project.RootPath);
        command.Parameters.AddWithValue("$schema", project.SchemaVersion);
        command.Parameters.AddWithValue("$created", RowCodecs.Utc(project.CreatedAt));
        command.Parameters.AddWithValue("$modified", RowCodecs.Utc(project.ModifiedAt));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<Project?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        const string sql = "SELECT Name, RootPath, SchemaVersion, CreatedAt, ModifiedAt FROM Project WHERE Id = $id;";

        await using var command = _uow.Connection.CreateCommand();
        command.Transaction = _uow.Transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue("$id", id.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new Project(
            id,
            RowCodecs.Text(reader["Name"]),
            RowCodecs.Text(reader["RootPath"]),
            Convert.ToInt32(reader["SchemaVersion"]))
        {
            CreatedAt = RowCodecs.DateTime(RowCodecs.Text(reader["CreatedAt"])),
            ModifiedAt = RowCodecs.DateTime(RowCodecs.Text(reader["ModifiedAt"])),
        };
    }
}
