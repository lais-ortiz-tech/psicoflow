using System.Text.Json;

namespace PsicoFlow.Api;

public sealed class PatientStore
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public PatientStore(string filePath)
    {
        _filePath = Path.GetFullPath(filePath);
    }

    public async Task<IReadOnlyList<PatientRecord>> ListAsync(string workspaceId)
    {
        await _gate.WaitAsync();
        try
        {
            return (await ReadAsync()).Where(patient => patient.WorkspaceId == workspaceId).ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PatientRecord?> FindAsync(string workspaceId, Guid id)
    {
        await _gate.WaitAsync();
        try
        {
            return (await ReadAsync()).FirstOrDefault(patient => patient.WorkspaceId == workspaceId && patient.Id == id);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task AddAsync(PatientRecord patient)
    {
        await _gate.WaitAsync();
        try
        {
            var patients = await ReadAsync();
            patients.Add(patient);
            await WriteAsync(patients);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PatientRecord?> UpdateAsync(string workspaceId, Guid id, string name, string? contact)
    {
        await _gate.WaitAsync();
        try
        {
            var patients = await ReadAsync();
            var patient = patients.FirstOrDefault(item => item.WorkspaceId == workspaceId && item.Id == id);
            if (patient is null)
                return null;
            if (patient.IsArchived)
                return patient;
            patient.Name = name;
            patient.Contact = contact;
            patient.UpdatedAt = DateTimeOffset.UtcNow;
            await WriteAsync(patients);
            return patient;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PatientRecord?> ArchiveAsync(string workspaceId, Guid id)
    {
        await _gate.WaitAsync();
        try
        {
            var patients = await ReadAsync();
            var patient = patients.FirstOrDefault(item => item.WorkspaceId == workspaceId && item.Id == id);
            if (patient is null)
                return null;
            if (!patient.IsArchived)
            {
                patient.IsArchived = true;
                patient.UpdatedAt = DateTimeOffset.UtcNow;
                await WriteAsync(patients);
            }
            return patient;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<PatientRecord>> ReadAsync()
    {
        if (!File.Exists(_filePath))
            return [];
        await using var stream = File.OpenRead(_filePath);
        return await JsonSerializer.DeserializeAsync<List<PatientRecord>>(stream, JsonOptions) ?? [];
    }

    private async Task WriteAsync(List<PatientRecord> patients)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        var temporaryPath = $"{_filePath}.{Guid.NewGuid():N}.tmp";
        await using (var stream = File.Create(temporaryPath))
            await JsonSerializer.SerializeAsync(stream, patients, JsonOptions);
        File.Move(temporaryPath, _filePath, overwrite: true);
    }
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
