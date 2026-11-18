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

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT COALESCE(MAX(Version), 0) FROM SchemaMigrations;";
            var version = Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
            if (version < 2)
            {
                using var transaction = connection.BeginTransaction();
                command.Transaction = transaction;
                command.CommandText = """
                    CREATE TABLE Appointments (
                        Id TEXT NOT NULL PRIMARY KEY,
                        WorkspaceId TEXT NOT NULL,
                        PatientId TEXT NOT NULL REFERENCES Patients (Id),
                        StartsAt TEXT NOT NULL,
                        EndsAt TEXT NOT NULL,
                        IsCancelled INTEGER NOT NULL DEFAULT 0 CHECK (IsCancelled IN (0, 1)),
                        CreatedAt TEXT NOT NULL,
                        UpdatedAt TEXT NOT NULL,
                        CHECK (EndsAt > StartsAt)
                    );
                    CREATE INDEX IX_Appointments_WorkspaceId_StartsAt ON Appointments (WorkspaceId, StartsAt);
                    INSERT INTO SchemaMigrations (Version, AppliedAt) VALUES (2, $appliedAt);
                    """;
                command.Parameters.Clear();
                command.Parameters.AddWithValue("$appliedAt", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
                await command.ExecuteNonQueryAsync();
                transaction.Commit();
            }
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT COALESCE(MAX(Version), 0) FROM SchemaMigrations;";
            var version = Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
            if (version < 3)
            {
                using var transaction = connection.BeginTransaction();
                command.Transaction = transaction;
                command.CommandText = """
                    CREATE TABLE Sessions (
                        Id TEXT NOT NULL PRIMARY KEY,
                        WorkspaceId TEXT NOT NULL,
                        PatientId TEXT NOT NULL REFERENCES Patients (Id),
                        SessionDate TEXT NOT NULL,
                        Note TEXT NOT NULL,
                        CreatedAt TEXT NOT NULL,
                        UpdatedAt TEXT NOT NULL
                    );
                    CREATE INDEX IX_Sessions_WorkspaceId_PatientId_SessionDate
                        ON Sessions (WorkspaceId, PatientId, SessionDate DESC);
                    CREATE TABLE SessionVersions (
                        SessionId TEXT NOT NULL REFERENCES Sessions (Id),
                        Version INTEGER NOT NULL,
                        SessionDate TEXT NOT NULL,
                        Note TEXT NOT NULL,
                        ChangedAt TEXT NOT NULL,
                        ChangedBy TEXT NOT NULL,
                        PRIMARY KEY (SessionId, Version)
                    );
                    CREATE TABLE PatientTasks (
                        Id TEXT NOT NULL PRIMARY KEY,
                        WorkspaceId TEXT NOT NULL,
                        PatientId TEXT NOT NULL REFERENCES Patients (Id),
                        Description TEXT NOT NULL,
                        DueDate TEXT NULL,
                        IsCompleted INTEGER NOT NULL DEFAULT 0 CHECK (IsCompleted IN (0, 1)),
                        CreatedAt TEXT NOT NULL,
                        UpdatedAt TEXT NOT NULL
                    );
                    CREATE INDEX IX_PatientTasks_WorkspaceId_PatientId_Status_DueDate
                        ON PatientTasks (WorkspaceId, PatientId, IsCompleted, DueDate);
                    INSERT INTO SchemaMigrations (Version, AppliedAt) VALUES (3, $appliedAt);
                    """;
                command.Parameters.Clear();
                command.Parameters.AddWithValue("$appliedAt", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
                await command.ExecuteNonQueryAsync();
                transaction.Commit();
            }
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT COALESCE(MAX(Version), 0) FROM SchemaMigrations;";
            var version = Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
            if (version < 4)
            {
                using var transaction = connection.BeginTransaction();
                command.Transaction = transaction;
                command.CommandText = """
                    ALTER TABLE Appointments ADD COLUMN AmountCents INTEGER NOT NULL DEFAULT 0 CHECK (AmountCents >= 0);
                    ALTER TABLE Appointments ADD COLUMN Modality TEXT NOT NULL DEFAULT 'in_person' CHECK (Modality IN ('in_person', 'online'));
                    ALTER TABLE Appointments ADD COLUMN Status TEXT NOT NULL DEFAULT 'scheduled'
                        CHECK (Status IN ('scheduled', 'confirmed', 'attended', 'missed', 'cancelled'));
                    ALTER TABLE Appointments ADD COLUMN Notes TEXT NOT NULL DEFAULT '';
                    UPDATE Appointments SET Status = 'cancelled' WHERE IsCancelled = 1;
                    INSERT INTO SchemaMigrations (Version, AppliedAt) VALUES (4, $appliedAt);
                    """;
                command.Parameters.AddWithValue("$appliedAt", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
                await command.ExecuteNonQueryAsync();
                transaction.Commit();
            }
        }

        await ImportLegacyJsonAsync(connection);
    }

    public async Task<IReadOnlyList<AppointmentRecord>> ListAppointmentsAsync(string workspaceId, bool includeCancelled)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT a.Id, a.WorkspaceId, a.PatientId, p.Name, a.StartsAt, a.EndsAt, a.IsCancelled,
                a.AmountCents, a.Modality, a.Status, a.Notes, a.CreatedAt, a.UpdatedAt
            FROM Appointments a
            INNER JOIN Patients p ON p.Id = a.PatientId AND p.WorkspaceId = a.WorkspaceId
            WHERE a.WorkspaceId = $workspaceId AND ($includeCancelled = 1 OR a.IsCancelled = 0)
            ORDER BY a.StartsAt;
            """;
        command.Parameters.AddWithValue("$workspaceId", workspaceId);
        command.Parameters.AddWithValue("$includeCancelled", includeCancelled ? 1 : 0);

        var appointments = new List<AppointmentRecord>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            appointments.Add(ReadAppointment(reader));
        return appointments;
    }

    public async Task<AppointmentRecord?> FindNextAppointmentAsync(string workspaceId, Guid patientId, DateTimeOffset from)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT a.Id, a.WorkspaceId, a.PatientId, p.Name, a.StartsAt, a.EndsAt, a.IsCancelled,
                a.AmountCents, a.Modality, a.Status, a.Notes, a.CreatedAt, a.UpdatedAt
            FROM Appointments a
            INNER JOIN Patients p ON p.Id = a.PatientId AND p.WorkspaceId = a.WorkspaceId
            WHERE a.WorkspaceId = $workspaceId AND a.PatientId = $patientId
                AND a.IsCancelled = 0 AND a.StartsAt >= $from
            ORDER BY a.StartsAt
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$workspaceId", workspaceId);
        command.Parameters.AddWithValue("$patientId", patientId.ToString("D"));
        command.Parameters.AddWithValue("$from", ToDatabaseTime(from));
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadAppointment(reader) : null;
    }

    public async Task<IReadOnlyList<SessionRecord>?> ListSessionsAsync(string workspaceId, Guid patientId)
    {
        await using var connection = await OpenConnectionAsync();
        if (await FindAsync(connection, workspaceId, patientId) is null)
            return null;

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, WorkspaceId, PatientId, SessionDate, Note, CreatedAt, UpdatedAt
            FROM Sessions
            WHERE WorkspaceId = $workspaceId AND PatientId = $patientId
            ORDER BY SessionDate DESC, CreatedAt DESC;
            """;
        command.Parameters.AddWithValue("$workspaceId", workspaceId);
        command.Parameters.AddWithValue("$patientId", patientId.ToString("D"));

        var sessions = new List<SessionRecord>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            sessions.Add(ReadSession(reader));
        return sessions;
    }

    public async Task<SessionRecord?> AddSessionAsync(SessionRecord session, string changedBy)
    {
        await using var connection = await OpenConnectionAsync();
        using var transaction = connection.BeginTransaction();
        if (!await PatientExistsAsync(connection, transaction, session.WorkspaceId, session.PatientId))
            return null;

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO Sessions (Id, WorkspaceId, PatientId, SessionDate, Note, CreatedAt, UpdatedAt)
                VALUES ($id, $workspaceId, $patientId, $sessionDate, $note, $createdAt, $updatedAt);
                """;
            AddSessionParameters(command, session);
            await command.ExecuteNonQueryAsync();
        }

        await AddSessionVersionAsync(connection, transaction, session, 1, changedBy, session.CreatedAt);
        transaction.Commit();
        return session;
    }

    public async Task<SessionRecord?> UpdateSessionAsync(
        string workspaceId,
        Guid patientId,
        Guid sessionId,
        DateOnly sessionDate,
        string note,
        string changedBy)
    {
        await using var connection = await OpenConnectionAsync();
        using var transaction = connection.BeginTransaction();
        var existing = await FindSessionAsync(connection, transaction, workspaceId, patientId, sessionId);
        if (existing is null)
            return null;

        var updated = existing with
        {
            SessionDate = sessionDate,
            Note = note,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE Sessions
                SET SessionDate = $sessionDate, Note = $note, UpdatedAt = $updatedAt
                WHERE WorkspaceId = $workspaceId AND PatientId = $patientId AND Id = $id;
                """;
            command.Parameters.AddWithValue("$sessionDate", ToDatabaseDate(updated.SessionDate));
            command.Parameters.AddWithValue("$note", updated.Note);
            command.Parameters.AddWithValue("$updatedAt", ToDatabaseTime(updated.UpdatedAt));
            command.Parameters.AddWithValue("$workspaceId", workspaceId);
            command.Parameters.AddWithValue("$patientId", patientId.ToString("D"));
            command.Parameters.AddWithValue("$id", sessionId.ToString("D"));
            await command.ExecuteNonQueryAsync();
        }

        var version = await GetNextSessionVersionAsync(connection, transaction, sessionId);
        await AddSessionVersionAsync(connection, transaction, updated, version, changedBy, updated.UpdatedAt);
        transaction.Commit();
        return updated;
    }

    public async Task<IReadOnlyList<SessionVersionRecord>?> ListSessionVersionsAsync(
        string workspaceId,
        Guid patientId,
        Guid sessionId)
    {
        await using var connection = await OpenConnectionAsync();
        if (await FindSessionAsync(connection, null, workspaceId, patientId, sessionId) is null)
            return null;

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Version, SessionDate, Note, ChangedAt, ChangedBy
            FROM SessionVersions
            WHERE SessionId = $sessionId
            ORDER BY Version DESC;
            """;
        command.Parameters.AddWithValue("$sessionId", sessionId.ToString("D"));

        var versions = new List<SessionVersionRecord>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            versions.Add(ReadSessionVersion(reader));
        return versions;
    }

    public async Task<IReadOnlyList<PatientTaskRecord>?> ListPatientTasksAsync(
        string workspaceId,
        Guid patientId,
        bool includeCompleted)
    {
        await using var connection = await OpenConnectionAsync();
        if (await FindAsync(connection, workspaceId, patientId) is null)
            return null;

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, WorkspaceId, PatientId, Description, DueDate, IsCompleted, CreatedAt, UpdatedAt
            FROM PatientTasks
            WHERE WorkspaceId = $workspaceId AND PatientId = $patientId
                AND ($includeCompleted = 1 OR IsCompleted = 0)
            ORDER BY IsCompleted, DueDate IS NULL, DueDate, CreatedAt DESC;
            """;
        command.Parameters.AddWithValue("$workspaceId", workspaceId);
        command.Parameters.AddWithValue("$patientId", patientId.ToString("D"));
        command.Parameters.AddWithValue("$includeCompleted", includeCompleted ? 1 : 0);

        var tasks = new List<PatientTaskRecord>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            tasks.Add(ReadPatientTask(reader));
        return tasks;
    }

    public async Task<PatientTaskRecord?> AddPatientTaskAsync(PatientTaskRecord task)
    {
        await using var connection = await OpenConnectionAsync();
        using var transaction = connection.BeginTransaction();
        if (!await PatientExistsAsync(connection, transaction, task.WorkspaceId, task.PatientId))
            return null;

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO PatientTasks (Id, WorkspaceId, PatientId, Description, DueDate, IsCompleted, CreatedAt, UpdatedAt)
            VALUES ($id, $workspaceId, $patientId, $description, $dueDate, $isCompleted, $createdAt, $updatedAt);
            """;
        AddPatientTaskParameters(command, task);
        await command.ExecuteNonQueryAsync();
        transaction.Commit();
        return task;
    }

    public async Task<PatientTaskRecord?> UpdatePatientTaskAsync(
        string workspaceId,
        Guid patientId,
        Guid taskId,
        string description,
        DateOnly? dueDate,
        bool isCompleted)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE PatientTasks
            SET Description = $description, DueDate = $dueDate, IsCompleted = $isCompleted, UpdatedAt = $updatedAt
            WHERE WorkspaceId = $workspaceId AND PatientId = $patientId AND Id = $id;
            """;
        command.Parameters.AddWithValue("$description", description);
        command.Parameters.AddWithValue("$dueDate", (object?)dueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? DBNull.Value);
        command.Parameters.AddWithValue("$isCompleted", isCompleted ? 1 : 0);
        command.Parameters.AddWithValue("$updatedAt", ToDatabaseTime(DateTimeOffset.UtcNow));
        command.Parameters.AddWithValue("$workspaceId", workspaceId);
        command.Parameters.AddWithValue("$patientId", patientId.ToString("D"));
        command.Parameters.AddWithValue("$id", taskId.ToString("D"));
        return await command.ExecuteNonQueryAsync() == 0
            ? null
            : await FindPatientTaskAsync(connection, workspaceId, patientId, taskId);
    }

    public async Task<AppointmentSaveResult> AddAppointmentAsync(AppointmentRecord appointment)
    {
        await using var connection = await OpenConnectionAsync();
        using var transaction = connection.BeginTransaction();
        var patientAvailable = await IsActivePatientAsync(connection, transaction, appointment.WorkspaceId, appointment.PatientId);
        if (!patientAvailable)
            return new AppointmentSaveResult(AppointmentSaveStatus.PatientUnavailable, null);
        if (await HasConflictAsync(connection, transaction, appointment, null))
            return new AppointmentSaveResult(AppointmentSaveStatus.Conflict, null);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO Appointments
                (Id, WorkspaceId, PatientId, StartsAt, EndsAt, IsCancelled, AmountCents, Modality, Status, Notes, CreatedAt, UpdatedAt)
            VALUES
                ($id, $workspaceId, $patientId, $startsAt, $endsAt, 0, $amountCents, $modality, $status, $notes, $createdAt, $updatedAt);
            """;
        AddAppointmentParameters(command, appointment);
        await command.ExecuteNonQueryAsync();
        var saved = await FindAppointmentAsync(connection, transaction, appointment.WorkspaceId, appointment.Id);
        transaction.Commit();
        return new AppointmentSaveResult(AppointmentSaveStatus.Success, saved);
    }

    public async Task<AppointmentSaveResult> UpdateAppointmentAsync(
        string workspaceId, Guid id, Guid patientId, DateTimeOffset startsAt, DateTimeOffset endsAt,
        long amountCents, string modality, string status, string notes)
    {
        await using var connection = await OpenConnectionAsync();
        using var transaction = connection.BeginTransaction();
        var existing = await FindAppointmentAsync(connection, transaction, workspaceId, id);
        if (existing is null)
            return new AppointmentSaveResult(AppointmentSaveStatus.NotFound, null);
        if (!await IsActivePatientAsync(connection, transaction, workspaceId, patientId))
            return new AppointmentSaveResult(AppointmentSaveStatus.PatientUnavailable, null);

        var updated = existing with
        {
            PatientId = patientId,
            StartsAt = startsAt,
            EndsAt = endsAt,
            IsCancelled = status == "cancelled",
            AmountCents = amountCents,
            Modality = modality,
            Status = status,
            Notes = notes,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        if (!updated.IsCancelled && await HasConflictAsync(connection, transaction, updated, id))
            return new AppointmentSaveResult(AppointmentSaveStatus.Conflict, null);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE Appointments
            SET PatientId = $patientId, StartsAt = $startsAt, EndsAt = $endsAt,
                IsCancelled = $isCancelled, AmountCents = $amountCents, Modality = $modality,
                Status = $status, Notes = $notes, UpdatedAt = $updatedAt
            WHERE WorkspaceId = $workspaceId AND Id = $id;
            """;
        command.Parameters.AddWithValue("$patientId", patientId.ToString("D"));
        command.Parameters.AddWithValue("$startsAt", ToDatabaseTime(updated.StartsAt));
        command.Parameters.AddWithValue("$endsAt", ToDatabaseTime(updated.EndsAt));
        command.Parameters.AddWithValue("$isCancelled", updated.IsCancelled ? 1 : 0);
        command.Parameters.AddWithValue("$amountCents", updated.AmountCents);
        command.Parameters.AddWithValue("$modality", updated.Modality);
        command.Parameters.AddWithValue("$status", updated.Status);
        command.Parameters.AddWithValue("$notes", updated.Notes);
        command.Parameters.AddWithValue("$updatedAt", ToDatabaseTime(updated.UpdatedAt));
        command.Parameters.AddWithValue("$workspaceId", workspaceId);
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        await command.ExecuteNonQueryAsync();
        var saved = await FindAppointmentAsync(connection, transaction, workspaceId, id);
        transaction.Commit();
        return new AppointmentSaveResult(AppointmentSaveStatus.Success, saved);
    }

    public async Task<AppointmentRecord?> CancelAppointmentAsync(string workspaceId, Guid id)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Appointments
            SET IsCancelled = 1, Status = 'cancelled', UpdatedAt = $updatedAt
            WHERE WorkspaceId = $workspaceId AND Id = $id AND IsCancelled = 0;
            """;
        command.Parameters.AddWithValue("$updatedAt", ToDatabaseTime(DateTimeOffset.UtcNow));
        command.Parameters.AddWithValue("$workspaceId", workspaceId);
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        await command.ExecuteNonQueryAsync();

        await using var findCommand = connection.CreateCommand();
        findCommand.CommandText = """
            SELECT a.Id, a.WorkspaceId, a.PatientId, p.Name, a.StartsAt, a.EndsAt, a.IsCancelled,
                a.AmountCents, a.Modality, a.Status, a.Notes, a.CreatedAt, a.UpdatedAt
            FROM Appointments a
            INNER JOIN Patients p ON p.Id = a.PatientId AND p.WorkspaceId = a.WorkspaceId
            WHERE a.WorkspaceId = $workspaceId AND a.Id = $id;
            """;
        findCommand.Parameters.AddWithValue("$workspaceId", workspaceId);
        findCommand.Parameters.AddWithValue("$id", id.ToString("D"));
        await using var reader = await findCommand.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadAppointment(reader) : null;
    }

    public async Task<bool> DeleteAppointmentAsync(string workspaceId, Guid id)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Appointments WHERE WorkspaceId = $workspaceId AND Id = $id;";
        command.Parameters.AddWithValue("$workspaceId", workspaceId);
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        return await command.ExecuteNonQueryAsync() > 0;
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

    private static async Task<bool> IsActivePatientAsync(SqliteConnection connection, SqliteTransaction transaction, string workspaceId, Guid patientId)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM Patients WHERE WorkspaceId = $workspaceId AND Id = $patientId AND IsArchived = 0;";
        command.Parameters.AddWithValue("$workspaceId", workspaceId);
        command.Parameters.AddWithValue("$patientId", patientId.ToString("D"));
        return await command.ExecuteScalarAsync() is not null;
    }

    private static async Task<bool> PatientExistsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string workspaceId,
        Guid patientId)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM Patients WHERE WorkspaceId = $workspaceId AND Id = $patientId;";
        command.Parameters.AddWithValue("$workspaceId", workspaceId);
        command.Parameters.AddWithValue("$patientId", patientId.ToString("D"));
        return await command.ExecuteScalarAsync() is not null;
    }

    private static async Task<SessionRecord?> FindSessionAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string workspaceId,
        Guid patientId,
        Guid sessionId)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT Id, WorkspaceId, PatientId, SessionDate, Note, CreatedAt, UpdatedAt
            FROM Sessions
            WHERE WorkspaceId = $workspaceId AND PatientId = $patientId AND Id = $id;
            """;
        command.Parameters.AddWithValue("$workspaceId", workspaceId);
        command.Parameters.AddWithValue("$patientId", patientId.ToString("D"));
        command.Parameters.AddWithValue("$id", sessionId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadSession(reader) : null;
    }

    private static async Task<int> GetNextSessionVersionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid sessionId)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COALESCE(MAX(Version), 0) + 1 FROM SessionVersions WHERE SessionId = $sessionId;";
        command.Parameters.AddWithValue("$sessionId", sessionId.ToString("D"));
        return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static async Task AddSessionVersionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SessionRecord session,
        int version,
        string changedBy,
        DateTimeOffset changedAt)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO SessionVersions (SessionId, Version, SessionDate, Note, ChangedAt, ChangedBy)
            VALUES ($sessionId, $version, $sessionDate, $note, $changedAt, $changedBy);
            """;
        command.Parameters.AddWithValue("$sessionId", session.Id.ToString("D"));
        command.Parameters.AddWithValue("$version", version);
        command.Parameters.AddWithValue("$sessionDate", ToDatabaseDate(session.SessionDate));
        command.Parameters.AddWithValue("$note", session.Note);
        command.Parameters.AddWithValue("$changedAt", ToDatabaseTime(changedAt));
        command.Parameters.AddWithValue("$changedBy", changedBy);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<PatientTaskRecord?> FindPatientTaskAsync(
        SqliteConnection connection,
        string workspaceId,
        Guid patientId,
        Guid taskId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, WorkspaceId, PatientId, Description, DueDate, IsCompleted, CreatedAt, UpdatedAt
            FROM PatientTasks
            WHERE WorkspaceId = $workspaceId AND PatientId = $patientId AND Id = $id;
            """;
        command.Parameters.AddWithValue("$workspaceId", workspaceId);
        command.Parameters.AddWithValue("$patientId", patientId.ToString("D"));
        command.Parameters.AddWithValue("$id", taskId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadPatientTask(reader) : null;
    }

    private static void AddSessionParameters(SqliteCommand command, SessionRecord session)
    {
        command.Parameters.AddWithValue("$id", session.Id.ToString("D"));
        command.Parameters.AddWithValue("$workspaceId", session.WorkspaceId);
        command.Parameters.AddWithValue("$patientId", session.PatientId.ToString("D"));
        command.Parameters.AddWithValue("$sessionDate", ToDatabaseDate(session.SessionDate));
        command.Parameters.AddWithValue("$note", session.Note);
        command.Parameters.AddWithValue("$createdAt", ToDatabaseTime(session.CreatedAt));
        command.Parameters.AddWithValue("$updatedAt", ToDatabaseTime(session.UpdatedAt));
    }

    private static void AddPatientTaskParameters(SqliteCommand command, PatientTaskRecord task)
    {
        command.Parameters.AddWithValue("$id", task.Id.ToString("D"));
        command.Parameters.AddWithValue("$workspaceId", task.WorkspaceId);
        command.Parameters.AddWithValue("$patientId", task.PatientId.ToString("D"));
        command.Parameters.AddWithValue("$description", task.Description);
        command.Parameters.AddWithValue("$dueDate", (object?)task.DueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? DBNull.Value);
        command.Parameters.AddWithValue("$isCompleted", task.IsCompleted ? 1 : 0);
        command.Parameters.AddWithValue("$createdAt", ToDatabaseTime(task.CreatedAt));
        command.Parameters.AddWithValue("$updatedAt", ToDatabaseTime(task.UpdatedAt));
    }

    private static string ToDatabaseDate(DateOnly value) =>
        value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateOnly ReadDatabaseDate(string value) =>
        DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static SessionRecord ReadSession(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)),
        reader.GetString(1),
        Guid.Parse(reader.GetString(2)),
        ReadDatabaseDate(reader.GetString(3)),
        reader.GetString(4),
        DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));

    private static SessionVersionRecord ReadSessionVersion(SqliteDataReader reader) => new(
        reader.GetInt32(0),
        ReadDatabaseDate(reader.GetString(1)),
        reader.GetString(2),
        DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        reader.GetString(4));

    private static PatientTaskRecord ReadPatientTask(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)),
        reader.GetString(1),
        Guid.Parse(reader.GetString(2)),
        reader.GetString(3),
        reader.IsDBNull(4) ? null : ReadDatabaseDate(reader.GetString(4)),
        reader.GetInt64(5) != 0,
        DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        DateTimeOffset.Parse(reader.GetString(7), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));

    private static async Task<bool> HasConflictAsync(SqliteConnection connection, SqliteTransaction transaction, AppointmentRecord appointment, Guid? excludingId)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT 1 FROM Appointments
            WHERE WorkspaceId = $workspaceId
                AND IsCancelled = 0
                AND StartsAt < $endsAt
                AND EndsAt > $startsAt
                AND ($excludingId IS NULL OR Id <> $excludingId)
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$workspaceId", appointment.WorkspaceId);
        command.Parameters.AddWithValue("$startsAt", ToDatabaseTime(appointment.StartsAt));
        command.Parameters.AddWithValue("$endsAt", ToDatabaseTime(appointment.EndsAt));
        command.Parameters.AddWithValue("$excludingId", (object?)excludingId?.ToString("D") ?? DBNull.Value);
        return await command.ExecuteScalarAsync() is not null;
    }

    private static async Task<AppointmentRecord?> FindAppointmentAsync(SqliteConnection connection, SqliteTransaction transaction, string workspaceId, Guid id)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT a.Id, a.WorkspaceId, a.PatientId, p.Name, a.StartsAt, a.EndsAt, a.IsCancelled,
                a.AmountCents, a.Modality, a.Status, a.Notes, a.CreatedAt, a.UpdatedAt
            FROM Appointments a
            INNER JOIN Patients p ON p.Id = a.PatientId AND p.WorkspaceId = a.WorkspaceId
            WHERE a.WorkspaceId = $workspaceId AND a.Id = $id;
            """;
        command.Parameters.AddWithValue("$workspaceId", workspaceId);
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadAppointment(reader) : null;
    }

    private static void AddAppointmentParameters(SqliteCommand command, AppointmentRecord appointment)
    {
        command.Parameters.AddWithValue("$id", appointment.Id.ToString("D"));
        command.Parameters.AddWithValue("$workspaceId", appointment.WorkspaceId);
        command.Parameters.AddWithValue("$patientId", appointment.PatientId.ToString("D"));
        command.Parameters.AddWithValue("$startsAt", ToDatabaseTime(appointment.StartsAt));
        command.Parameters.AddWithValue("$endsAt", ToDatabaseTime(appointment.EndsAt));
        command.Parameters.AddWithValue("$amountCents", appointment.AmountCents);
        command.Parameters.AddWithValue("$modality", appointment.Modality);
        command.Parameters.AddWithValue("$status", appointment.Status);
        command.Parameters.AddWithValue("$notes", appointment.Notes);
        command.Parameters.AddWithValue("$createdAt", ToDatabaseTime(appointment.CreatedAt));
        command.Parameters.AddWithValue("$updatedAt", ToDatabaseTime(appointment.UpdatedAt));
    }

    private static string ToDatabaseTime(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static AppointmentRecord ReadAppointment(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)),
        reader.GetString(1),
        Guid.Parse(reader.GetString(2)),
        reader.GetString(3),
        DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        reader.GetInt64(6) != 0,
        reader.GetInt64(7),
        reader.GetString(8),
        reader.GetString(9),
        reader.GetString(10),
        DateTimeOffset.Parse(reader.GetString(11), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        DateTimeOffset.Parse(reader.GetString(12), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));

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

public sealed record AppointmentRecord(
    Guid Id,
    string WorkspaceId,
    Guid PatientId,
    string PatientName,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    bool IsCancelled,
    long AmountCents,
    string Modality,
    string Status,
    string Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public enum AppointmentSaveStatus
{
    Success,
    Conflict,
    PatientUnavailable,
    NotFound
}

public sealed record AppointmentSaveResult(AppointmentSaveStatus Status, AppointmentRecord? Appointment);

public sealed record SessionRecord(
    Guid Id,
    string WorkspaceId,
    Guid PatientId,
    DateOnly SessionDate,
    string Note,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record SessionVersionRecord(
    int Version,
    DateOnly SessionDate,
    string Note,
    DateTimeOffset ChangedAt,
    string ChangedBy);

public sealed record PatientTaskRecord(
    Guid Id,
    string WorkspaceId,
    Guid PatientId,
    string Description,
    DateOnly? DueDate,
    bool IsCompleted,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
