using System.Globalization;
using Microsoft.Data.Sqlite;

namespace PsicoFlow.Api;

public sealed class FinancialStore
{
    private readonly string _connectionString;

    public FinancialStore(string databasePath)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true
        }.ToString();
    }

    public async Task InitializeAsync()
    {
        var databasePath = new SqliteConnectionStringBuilder(_connectionString).DataSource;
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS FinancialEntries (
                Id TEXT NOT NULL PRIMARY KEY,
                WorkspaceId TEXT NOT NULL,
                PatientId TEXT NULL REFERENCES Patients (Id),
                Description TEXT NOT NULL,
                Type TEXT NOT NULL CHECK (Type IN ('revenue', 'expense')),
                Status TEXT NOT NULL CHECK (Status IN ('paid', 'pending')),
                AmountCents INTEGER NOT NULL CHECK (AmountCents > 0),
                EntryDate TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_FinancialEntries_WorkspaceId_EntryDate
                ON FinancialEntries (WorkspaceId, EntryDate DESC);
            """;
        await command.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<FinancialEntryRecord>> ListAsync(string workspaceId, DateOnly from, DateOnly to)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT e.Id, e.WorkspaceId, e.PatientId, p.Name, e.Description, e.Type, e.Status,
                e.AmountCents, e.EntryDate, e.CreatedAt, e.UpdatedAt
            FROM FinancialEntries e
            LEFT JOIN Patients p ON p.Id = e.PatientId AND p.WorkspaceId = e.WorkspaceId
            WHERE e.WorkspaceId = $workspaceId AND e.EntryDate >= $from AND e.EntryDate < $to
            ORDER BY e.EntryDate DESC, e.CreatedAt DESC;
            """;
        command.Parameters.AddWithValue("$workspaceId", workspaceId);
        command.Parameters.AddWithValue("$from", ToDatabaseDate(from));
        command.Parameters.AddWithValue("$to", ToDatabaseDate(to));

        var entries = new List<FinancialEntryRecord>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            entries.Add(ReadEntry(reader));
        return entries;
    }

    public async Task<FinancialEntrySaveResult> SaveAsync(FinancialEntryRecord entry, bool isUpdate)
    {
        await using var connection = await OpenConnectionAsync();
        using var transaction = connection.BeginTransaction();

        if (entry.PatientId.HasValue && !await HasActivePatientAsync(connection, transaction, entry.WorkspaceId, entry.PatientId.Value))
            return new(FinancialEntrySaveStatus.PatientUnavailable, null);

        if (isUpdate && !await ExistsAsync(connection, transaction, entry.WorkspaceId, entry.Id))
            return new(FinancialEntrySaveStatus.NotFound, null);

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = isUpdate
                ? """
                  UPDATE FinancialEntries SET PatientId = $patientId, Description = $description,
                      Type = $type, Status = $status, AmountCents = $amountCents,
                      EntryDate = $entryDate, UpdatedAt = $updatedAt
                  WHERE WorkspaceId = $workspaceId AND Id = $id;
                  """
                : """
                  INSERT INTO FinancialEntries
                      (Id, WorkspaceId, PatientId, Description, Type, Status, AmountCents, EntryDate, CreatedAt, UpdatedAt)
                  VALUES
                      ($id, $workspaceId, $patientId, $description, $type, $status, $amountCents, $entryDate, $createdAt, $updatedAt);
                  """;
            AddParameters(command, entry, !isUpdate);
            await command.ExecuteNonQueryAsync();
        }

        var saved = await FindAsync(connection, transaction, entry.WorkspaceId, entry.Id);
        transaction.Commit();
        return new(FinancialEntrySaveStatus.Success, saved);
    }

    public async Task<bool> DeleteAsync(string workspaceId, Guid id)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM FinancialEntries WHERE WorkspaceId = $workspaceId AND Id = $id;";
        command.Parameters.AddWithValue("$workspaceId", workspaceId);
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        return await command.ExecuteNonQueryAsync() > 0;
    }

    private async Task<SqliteConnection> OpenConnectionAsync()
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static async Task<bool> HasActivePatientAsync(
        SqliteConnection connection, SqliteTransaction transaction, string workspaceId, Guid patientId)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT 1 FROM Patients
            WHERE WorkspaceId = $workspaceId AND Id = $patientId AND IsArchived = 0;
            """;
        command.Parameters.AddWithValue("$workspaceId", workspaceId);
        command.Parameters.AddWithValue("$patientId", patientId.ToString("D"));
        return await command.ExecuteScalarAsync() is not null;
    }

    private static async Task<bool> ExistsAsync(
        SqliteConnection connection, SqliteTransaction transaction, string workspaceId, Guid id)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM FinancialEntries WHERE WorkspaceId = $workspaceId AND Id = $id;";
        command.Parameters.AddWithValue("$workspaceId", workspaceId);
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        return await command.ExecuteScalarAsync() is not null;
    }

    private static async Task<FinancialEntryRecord?> FindAsync(
        SqliteConnection connection, SqliteTransaction transaction, string workspaceId, Guid id)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT e.Id, e.WorkspaceId, e.PatientId, p.Name, e.Description, e.Type, e.Status,
                e.AmountCents, e.EntryDate, e.CreatedAt, e.UpdatedAt
            FROM FinancialEntries e
            LEFT JOIN Patients p ON p.Id = e.PatientId AND p.WorkspaceId = e.WorkspaceId
            WHERE e.WorkspaceId = $workspaceId AND e.Id = $id;
            """;
        command.Parameters.AddWithValue("$workspaceId", workspaceId);
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadEntry(reader) : null;
    }

    private static void AddParameters(SqliteCommand command, FinancialEntryRecord entry, bool includeCreatedAt)
    {
        command.Parameters.AddWithValue("$id", entry.Id.ToString("D"));
        command.Parameters.AddWithValue("$workspaceId", entry.WorkspaceId);
        command.Parameters.AddWithValue("$patientId", (object?)entry.PatientId?.ToString("D") ?? DBNull.Value);
        command.Parameters.AddWithValue("$description", entry.Description);
        command.Parameters.AddWithValue("$type", entry.Type);
        command.Parameters.AddWithValue("$status", entry.Status);
        command.Parameters.AddWithValue("$amountCents", entry.AmountCents);
        command.Parameters.AddWithValue("$entryDate", ToDatabaseDate(entry.EntryDate));
        if (includeCreatedAt)
            command.Parameters.AddWithValue("$createdAt", ToDatabaseTime(entry.CreatedAt));
        command.Parameters.AddWithValue("$updatedAt", ToDatabaseTime(entry.UpdatedAt));
    }

    private static FinancialEntryRecord ReadEntry(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)),
        reader.GetString(1),
        reader.IsDBNull(2) ? null : Guid.Parse(reader.GetString(2)),
        reader.IsDBNull(3) ? null : reader.GetString(3),
        reader.GetString(4),
        reader.GetString(5),
        reader.GetString(6),
        reader.GetInt64(7),
        DateOnly.ParseExact(reader.GetString(8), "yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTimeOffset.Parse(reader.GetString(9), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        DateTimeOffset.Parse(reader.GetString(10), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));

    private static string ToDatabaseDate(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string ToDatabaseTime(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}

public sealed record FinancialEntryRecord(
    Guid Id,
    string WorkspaceId,
    Guid? PatientId,
    string? PatientName,
    string Description,
    string Type,
    string Status,
    long AmountCents,
    DateOnly EntryDate,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public enum FinancialEntrySaveStatus
{
    Success,
    PatientUnavailable,
    NotFound
}

public sealed record FinancialEntrySaveResult(FinancialEntrySaveStatus Status, FinancialEntryRecord? Entry);
