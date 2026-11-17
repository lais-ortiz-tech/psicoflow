using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace PsicoFlow.Api;

public sealed class PatientStore
{
    private const string LegacyImportName = "patients-json-import";
    private readonly string _connectionString;
    private readonly string _legacyJsonFilePath;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public PatientStore(string databasePath, string legacyJsonFilePath)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true
        }.ToString();
        _legacyJsonFilePath = legacyJsonFilePath;
    }

    public async Task InitializeAsync()
    {
        var databasePath = new SqliteConnectionStringBuilder(_connectionString).DataSource;
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        await using var connection = await OpenConnectionAsync();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS SchemaMigrations (
                    Version INTEGER NOT NULL PRIMARY KEY,
                    AppliedAt TEXT NOT NULL
                );
                """;
            await command.ExecuteNonQueryAsync();
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT COALESCE(MAX(Version), 0) FROM SchemaMigrations;";
            var version = Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
            if (version < 1)
            {
                using var transaction = connection.BeginTransaction();
                command.Transaction = transaction;
                command.CommandText = """
                    CREATE TABLE Patients (
                        Id TEXT NOT NULL PRIMARY KEY,
                        WorkspaceId TEXT NOT NULL,
                        Name TEXT NOT NULL,
                        Contact TEXT NULL,
                        IsArchived INTEGER NOT NULL DEFAULT 0 CHECK (IsArchived IN (0, 1)),
                        CreatedAt TEXT NOT NULL,
                        UpdatedAt TEXT NOT NULL
                    );
                    CREATE INDEX IX_Patients_WorkspaceId_Name ON Patients (WorkspaceId, Name);
                    INSERT INTO SchemaMigrations (Version, AppliedAt) VALUES (1, $appliedAt);
                    """;
                command.Parameters.AddWithValue("$appliedAt", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
                await command.ExecuteNonQueryAsync();
                transaction.Commit();
            }
        }

        await ImportLegacyJsonAsync(connection);
    }

    public async Task<IReadOnlyList<PatientRecord>> ListAsync(string workspaceId)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, WorkspaceId, Name, Contact, IsArchived, CreatedAt, UpdatedAt
            FROM Patients
            WHERE WorkspaceId = $workspaceId;
            """;
        command.Parameters.AddWithValue("$workspaceId", workspaceId);

        var patients = new List<PatientRecord>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            patients.Add(ReadPatient(reader));
        }
        return patients;
    }

    public async Task<PatientRecord?> FindAsync(string workspaceId, Guid id)
    {
        await using var connection = await OpenConnectionAsync();
        return await FindAsync(connection, workspaceId, id);
    }

    public async Task AddAsync(PatientRecord patient)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Patients (Id, WorkspaceId, Name, Contact, IsArchived, CreatedAt, UpdatedAt)
            VALUES ($id, $workspaceId, $name, $contact, $isArchived, $createdAt, $updatedAt);
            """;
        AddPatientParameters(command, patient);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<PatientRecord?> UpdateAsync(string workspaceId, Guid id, string name, string? contact)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Patients
            SET Name = $name, Contact = $contact, UpdatedAt = $updatedAt
            WHERE WorkspaceId = $workspaceId AND Id = $id AND IsArchived = 0;
            """;
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$contact", (object?)contact ?? DBNull.Value);
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$workspaceId", workspaceId);
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        await command.ExecuteNonQueryAsync();
        return await FindAsync(connection, workspaceId, id);
    }

    public async Task<PatientRecord?> ArchiveAsync(string workspaceId, Guid id)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Patients
            SET IsArchived = 1, UpdatedAt = $updatedAt
            WHERE WorkspaceId = $workspaceId AND Id = $id AND IsArchived = 0;
            """;
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$workspaceId", workspaceId);
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        await command.ExecuteNonQueryAsync();
        return await FindAsync(connection, workspaceId, id);
    }

    private async Task ImportLegacyJsonAsync(SqliteConnection connection)
    {
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS DataMigrations (
                    Name TEXT NOT NULL PRIMARY KEY,
                    AppliedAt TEXT NOT NULL
                );
                """;
            await command.ExecuteNonQueryAsync();
        }

        if (!File.Exists(_legacyJsonFilePath))
            return;

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT 1 FROM DataMigrations WHERE Name = $name;";
            command.Parameters.AddWithValue("$name", LegacyImportName);
            if (await command.ExecuteScalarAsync() is not null)
                return;
        }

        await using var stream = File.OpenRead(_legacyJsonFilePath);
        var patients = await JsonSerializer.DeserializeAsync<List<PatientRecord>>(stream, JsonOptions) ?? [];
        using var transaction = connection.BeginTransaction();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT OR IGNORE INTO Patients (Id, WorkspaceId, Name, Contact, IsArchived, CreatedAt, UpdatedAt)
                VALUES ($id, $workspaceId, $name, $contact, $isArchived, $createdAt, $updatedAt);
                """;
            var id = command.Parameters.Add("$id", SqliteType.Text);
            var workspaceId = command.Parameters.Add("$workspaceId", SqliteType.Text);
            var name = command.Parameters.Add("$name", SqliteType.Text);
            var contact = command.Parameters.Add("$contact", SqliteType.Text);
            var isArchived = command.Parameters.Add("$isArchived", SqliteType.Integer);
            var createdAt = command.Parameters.Add("$createdAt", SqliteType.Text);
            var updatedAt = command.Parameters.Add("$updatedAt", SqliteType.Text);

            foreach (var patient in patients)
            {
                id.Value = patient.Id.ToString("D");
                workspaceId.Value = patient.WorkspaceId;
                name.Value = patient.Name;
                contact.Value = (object?)patient.Contact ?? DBNull.Value;
                isArchived.Value = patient.IsArchived ? 1 : 0;
                createdAt.Value = patient.CreatedAt.ToString("O", CultureInfo.InvariantCulture);
                updatedAt.Value = patient.UpdatedAt.ToString("O", CultureInfo.InvariantCulture);
                await command.ExecuteNonQueryAsync();
            }
        }

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO DataMigrations (Name, AppliedAt) VALUES ($name, $appliedAt);";
            command.Parameters.AddWithValue("$name", LegacyImportName);
            command.Parameters.AddWithValue("$appliedAt", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            await command.ExecuteNonQueryAsync();
        }
        transaction.Commit();
    }

    private async Task<SqliteConnection> OpenConnectionAsync()
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static async Task<PatientRecord?> FindAsync(SqliteConnection connection, string workspaceId, Guid id)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, WorkspaceId, Name, Contact, IsArchived, CreatedAt, UpdatedAt
            FROM Patients
            WHERE WorkspaceId = $workspaceId AND Id = $id;
            """;
        command.Parameters.AddWithValue("$workspaceId", workspaceId);
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadPatient(reader) : null;
    }

    private static void AddPatientParameters(SqliteCommand command, PatientRecord patient)
    {
        command.Parameters.AddWithValue("$id", patient.Id.ToString("D"));
        command.Parameters.AddWithValue("$workspaceId", patient.WorkspaceId);
        command.Parameters.AddWithValue("$name", patient.Name);
        command.Parameters.AddWithValue("$contact", (object?)patient.Contact ?? DBNull.Value);
        command.Parameters.AddWithValue("$isArchived", patient.IsArchived ? 1 : 0);
        command.Parameters.AddWithValue("$createdAt", patient.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$updatedAt", patient.UpdatedAt.ToString("O", CultureInfo.InvariantCulture));
    }

    private static PatientRecord ReadPatient(SqliteDataReader reader) => new()
    {
        Id = Guid.Parse(reader.GetString(0)),
        WorkspaceId = reader.GetString(1),
        Name = reader.GetString(2),
        Contact = reader.IsDBNull(3) ? null : reader.GetString(3),
        IsArchived = reader.GetInt64(4) != 0,
        CreatedAt = DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        UpdatedAt = DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
    };
}

public sealed class PatientRecord
{
    public Guid Id { get; set; }
    public string WorkspaceId { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Contact { get; set; }
    public bool IsArchived { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
