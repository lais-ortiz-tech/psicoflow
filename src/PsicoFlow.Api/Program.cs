using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using PsicoFlow.Api;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "PsicoFlow.Session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();
var contentRoot = builder.Environment.ContentRootPath;
var databasePath = Path.GetFullPath(builder.Configuration["Storage:DatabasePath"] ?? "data/psicoflow.db", contentRoot);
var legacyJsonFilePath = Path.GetFullPath(
    builder.Configuration["Storage:LegacyFilePath"] ?? builder.Configuration["Storage:FilePath"] ?? "data/patients.json",
    contentRoot);
builder.Services.AddSingleton(new PatientStore(databasePath, legacyJsonFilePath));
builder.Services.AddSingleton(new FinancialStore(databasePath));

var app = builder.Build();
await app.Services.GetRequiredService<PatientStore>().InitializeAsync();
await app.Services.GetRequiredService<FinancialStore>().InitializeAsync();

app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await context.Response.WriteAsJsonAsync(new { title = "Ocorreu um erro inesperado." });
}));
app.UseAuthentication();
app.UseAuthorization();

app.MapPost("/api/auth/login", async (LoginRequest request, HttpContext context, IConfiguration configuration) =>
{
    var configuredEmail = configuration["DemoAccount:Email"] ?? string.Empty;
    var configuredPassword = configuration["DemoAccount:Password"] ?? string.Empty;
    var emailMatches = FixedTimeEquals(configuredEmail.Trim(), request.Email.Trim());
    var passwordMatches = FixedTimeEquals(configuredPassword, request.Password);

    if (!emailMatches || !passwordMatches || string.IsNullOrWhiteSpace(configuredEmail) || string.IsNullOrWhiteSpace(configuredPassword))
        return Results.Unauthorized();

    var workspaceId = configuration["DemoAccount:WorkspaceId"] ?? "";
    if (string.IsNullOrWhiteSpace(workspaceId))
        return Results.Problem("A conta de demonstração não está configurada.", statusCode: StatusCodes.Status500InternalServerError);

    var displayName = configuration["DemoAccount:DisplayName"] ?? configuredEmail;
    var claims = new[]
    {
        new Claim(ClaimTypes.NameIdentifier, configuredEmail.Trim().ToLowerInvariant()),
        new Claim(ClaimTypes.Name, displayName),
        new Claim("workspace_id", workspaceId)
    };
    await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
        new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));

    return Results.Ok(new UserResponse(displayName, configuredEmail));
}).AllowAnonymous();

app.MapPost("/api/auth/logout", async (HttpContext context) =>
{
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.NoContent();
}).RequireAuthorization();

app.MapGet("/api/auth/me", (ClaimsPrincipal user) =>
    Results.Ok(new UserResponse(user.Identity?.Name ?? "", user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "")))
    .RequireAuthorization();

var patients = app.MapGroup("/api/patients").RequireAuthorization();

patients.MapGet("", async (HttpContext context, PatientStore store, string? search, bool? includeArchived) =>
{
    var workspaceId = GetWorkspaceId(context.User);
    var records = await store.ListAsync(workspaceId);
    var query = records.Where(patient => includeArchived == true || !patient.IsArchived);
    if (!string.IsNullOrWhiteSpace(search))
        query = query.Where(patient => patient.Name.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase) || (patient.Contact?.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase) ?? false));
    return Results.Ok(query.OrderBy(patient => patient.Name).Select(ToResponse));
});

patients.MapGet("/{id:guid}", async (Guid id, HttpContext context, PatientStore store) =>
{
    var record = await store.FindAsync(GetWorkspaceId(context.User), id);
    return record is null ? Results.NotFound() : Results.Ok(ToResponse(record));
});

patients.MapPost("", async (PatientInput input, HttpContext context, PatientStore store) =>
{
    var errors = Validate(input);
    if (errors.Count > 0)
        return Results.ValidationProblem(errors);

    var now = DateTimeOffset.UtcNow;
    var record = new PatientRecord
    {
        Id = Guid.NewGuid(),
        WorkspaceId = GetWorkspaceId(context.User),
        Name = input.Name.Trim(),
        Contact = NormalizeContact(input.Contact),
        CreatedAt = now,
        UpdatedAt = now
    };
    await store.AddAsync(record);
    return Results.Created($"/api/patients/{record.Id}", ToResponse(record));
});

patients.MapPut("/{id:guid}", async (Guid id, PatientInput input, HttpContext context, PatientStore store) =>
{
    var errors = Validate(input);
    if (errors.Count > 0)
        return Results.ValidationProblem(errors);

    var updated = await store.UpdateAsync(GetWorkspaceId(context.User), id, input.Name.Trim(), NormalizeContact(input.Contact));
    if (updated is null)
        return Results.NotFound();
    if (updated.IsArchived)
        return Results.Conflict(new { title = "Pacientes arquivados não podem ser editados." });
    return Results.Ok(ToResponse(updated));
});

patients.MapPost("/{id:guid}/archive", async (Guid id, HttpContext context, PatientStore store) =>
{
    var archived = await store.ArchiveAsync(GetWorkspaceId(context.User), id);
    return archived is null ? Results.NotFound() : Results.Ok(ToResponse(archived));
});

patients.MapGet("/{patientId:guid}/appointments/next", async (Guid patientId, HttpContext context, PatientStore store) =>
{
    var workspaceId = GetWorkspaceId(context.User);
    if (await store.FindAsync(workspaceId, patientId) is null)
        return Results.NotFound();

    var appointment = await store.FindNextAppointmentAsync(workspaceId, patientId, DateTimeOffset.UtcNow);
    return appointment is null
        ? Results.NoContent()
        : Results.Ok(ToAppointmentResponse(appointment));
});

var sessions = patients.MapGroup("/{patientId:guid}/sessions");

sessions.MapGet("", async (Guid patientId, HttpContext context, PatientStore store) =>
{
    var records = await store.ListSessionsAsync(GetWorkspaceId(context.User), patientId);
    return records is null ? Results.NotFound() : Results.Ok(records.Select(ToSessionResponse));
});

sessions.MapPost("", async (Guid patientId, SessionInput input, HttpContext context, PatientStore store) =>
{
    var errors = ValidateSession(input);
    if (errors.Count > 0)
        return Results.ValidationProblem(errors);

    var now = DateTimeOffset.UtcNow;
    var session = new SessionRecord(
        Guid.NewGuid(), GetWorkspaceId(context.User), patientId,
        input.SessionDate, input.Note.Trim(), now, now);
    var saved = await store.AddSessionAsync(session, GetActorName(context.User));
    return saved is null
        ? Results.NotFound()
        : Results.Created($"/api/patients/{patientId}/sessions/{saved.Id}", ToSessionResponse(saved));
});

sessions.MapPut("/{sessionId:guid}", async (Guid patientId, Guid sessionId, SessionInput input, HttpContext context, PatientStore store) =>
{
    var errors = ValidateSession(input);
    if (errors.Count > 0)
        return Results.ValidationProblem(errors);

    var updated = await store.UpdateSessionAsync(
        GetWorkspaceId(context.User), patientId, sessionId,
        input.SessionDate, input.Note.Trim(), GetActorName(context.User));
    return updated is null ? Results.NotFound() : Results.Ok(ToSessionResponse(updated));
});

sessions.MapGet("/{sessionId:guid}/versions", async (Guid patientId, Guid sessionId, HttpContext context, PatientStore store) =>
{
    var versions = await store.ListSessionVersionsAsync(GetWorkspaceId(context.User), patientId, sessionId);
    return versions is null ? Results.NotFound() : Results.Ok(versions.Select(ToSessionVersionResponse));
});

var patientTasks = patients.MapGroup("/{patientId:guid}/tasks");

patientTasks.MapGet("", async (Guid patientId, HttpContext context, PatientStore store, bool? includeCompleted) =>
{
    var records = await store.ListPatientTasksAsync(GetWorkspaceId(context.User), patientId, includeCompleted == true);
    return records is null ? Results.NotFound() : Results.Ok(records.Select(ToPatientTaskResponse));
});

patientTasks.MapPost("", async (Guid patientId, PatientTaskInput input, HttpContext context, PatientStore store) =>
{
    var errors = ValidatePatientTask(input);
    if (errors.Count > 0)
        return Results.ValidationProblem(errors);

    var now = DateTimeOffset.UtcNow;
    var task = new PatientTaskRecord(
        Guid.NewGuid(), GetWorkspaceId(context.User), patientId,
        input.Description.Trim(), input.DueDate, false, now, now);
    var saved = await store.AddPatientTaskAsync(task);
    return saved is null
        ? Results.NotFound()
        : Results.Created($"/api/patients/{patientId}/tasks/{saved.Id}", ToPatientTaskResponse(saved));
});

patientTasks.MapPut("/{taskId:guid}", async (Guid patientId, Guid taskId, PatientTaskInput input, HttpContext context, PatientStore store) =>
{
    var errors = ValidatePatientTask(input);
    if (errors.Count > 0)
        return Results.ValidationProblem(errors);

    var updated = await store.UpdatePatientTaskAsync(
        GetWorkspaceId(context.User), patientId, taskId,
        input.Description.Trim(), input.DueDate, input.IsCompleted);
    return updated is null ? Results.NotFound() : Results.Ok(ToPatientTaskResponse(updated));
});

var appointments = app.MapGroup("/api/appointments").RequireAuthorization();

appointments.MapGet("", async (HttpContext context, PatientStore store, bool? includeCancelled) =>
{
    var records = await store.ListAppointmentsAsync(GetWorkspaceId(context.User), includeCancelled == true);
    return Results.Ok(records.Select(ToAppointmentResponse));
});

appointments.MapPost("", async (AppointmentInput input, HttpContext context, PatientStore store) =>
{
    var errors = ValidateAppointment(input);
    if (errors.Count > 0)
        return Results.ValidationProblem(errors);

    var now = DateTimeOffset.UtcNow;
    var appointment = new AppointmentRecord(
        Guid.NewGuid(), GetWorkspaceId(context.User), input.PatientId,
        "", input.StartsAt.ToUniversalTime(), input.EndsAt.ToUniversalTime(),
        false, decimal.ToInt64(input.Amount * 100), input.Modality.Trim().ToLowerInvariant(),
        input.Status.Trim().ToLowerInvariant(), input.Notes?.Trim() ?? "", now, now);
    var result = await store.AddAppointmentAsync(appointment);
    return result.Status switch
    {
        AppointmentSaveStatus.Success => Results.Created($"/api/appointments/{appointment.Id}", ToAppointmentResponse(result.Appointment!)),
        AppointmentSaveStatus.Conflict => Results.Conflict(new { title = "Este horário conflita com outro compromisso." }),
        AppointmentSaveStatus.PatientUnavailable => Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(input.PatientId)] = ["Selecione um paciente ativo do workspace."] }),
        _ => throw new InvalidOperationException("Resultado inesperado ao criar compromisso.")
    };
});

appointments.MapPut("/{id:guid}", async (Guid id, AppointmentInput input, HttpContext context, PatientStore store) =>
{
    var errors = ValidateAppointment(input, allowCancelled: true);
    if (errors.Count > 0)
        return Results.ValidationProblem(errors);

    var result = await store.UpdateAppointmentAsync(
        GetWorkspaceId(context.User), id, input.PatientId, input.StartsAt.ToUniversalTime(), input.EndsAt.ToUniversalTime(),
        decimal.ToInt64(input.Amount * 100), input.Modality.Trim().ToLowerInvariant(),
        input.Status.Trim().ToLowerInvariant(), input.Notes?.Trim() ?? "");
    return result.Status switch
    {
        AppointmentSaveStatus.Success => Results.Ok(ToAppointmentResponse(result.Appointment!)),
        AppointmentSaveStatus.Conflict => Results.Conflict(new { title = "Este horário conflita com outro compromisso." }),
        AppointmentSaveStatus.PatientUnavailable => Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(input.PatientId)] = ["Selecione um paciente ativo do workspace."] }),
        AppointmentSaveStatus.NotFound => Results.NotFound(),
        _ => throw new InvalidOperationException("Resultado inesperado ao editar compromisso.")
    };
});

appointments.MapPost("/{id:guid}/cancel", async (Guid id, HttpContext context, PatientStore store) =>
{
    var cancelled = await store.CancelAppointmentAsync(GetWorkspaceId(context.User), id);
    return cancelled is null ? Results.NotFound() : Results.Ok(ToAppointmentResponse(cancelled));
});

appointments.MapDelete("/{id:guid}", async (Guid id, HttpContext context, PatientStore store) =>
    await store.DeleteAppointmentAsync(GetWorkspaceId(context.User), id) ? Results.NoContent() : Results.NotFound());

var financialEntries = app.MapGroup("/api/financial-entries").RequireAuthorization();

financialEntries.MapGet("", async (HttpContext context, FinancialStore store, int? year, int? month) =>
{
    var selectedYear = year ?? DateTime.Today.Year;
    var selectedMonth = month ?? DateTime.Today.Month;
    if (selectedYear is < 1 or > 9998 || selectedMonth is < 1 or > 12)
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["month"] = ["Informe um mês válido."] });

    var from = new DateOnly(selectedYear, selectedMonth, 1);
    var entries = await store.ListAsync(GetWorkspaceId(context.User), from, from.AddMonths(1));
    return Results.Ok(entries.Select(ToFinancialEntryResponse));
});

financialEntries.MapPost("", async (FinancialEntryInput input, HttpContext context, FinancialStore store) =>
{
    var validation = ValidateFinancialEntry(input);
    if (validation.Errors.Count > 0)
        return Results.ValidationProblem(validation.Errors);

    var now = DateTimeOffset.UtcNow;
    var entry = new FinancialEntryRecord(
        Guid.NewGuid(), GetWorkspaceId(context.User), input.PatientId, null,
        input.Description.Trim(), input.Type.Trim().ToLowerInvariant(), input.Status.Trim().ToLowerInvariant(),
        validation.AmountCents, input.EntryDate, now, now);
    var result = await store.SaveAsync(entry, false);
    return result.Status switch
    {
        FinancialEntrySaveStatus.Success => Results.Created($"/api/financial-entries/{entry.Id}", ToFinancialEntryResponse(result.Entry!)),
        FinancialEntrySaveStatus.PatientUnavailable => Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(input.PatientId)] = ["Selecione um paciente ativo do workspace."] }),
        _ => throw new InvalidOperationException("Resultado inesperado ao criar lançamento financeiro.")
    };
});

financialEntries.MapPut("/{id:guid}", async (Guid id, FinancialEntryInput input, HttpContext context, FinancialStore store) =>
{
    var validation = ValidateFinancialEntry(input);
    if (validation.Errors.Count > 0)
        return Results.ValidationProblem(validation.Errors);

    var entry = new FinancialEntryRecord(
        id, GetWorkspaceId(context.User), input.PatientId, null,
        input.Description.Trim(), input.Type.Trim().ToLowerInvariant(), input.Status.Trim().ToLowerInvariant(),
        validation.AmountCents, input.EntryDate, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
    var result = await store.SaveAsync(entry, true);
    return result.Status switch
    {
        FinancialEntrySaveStatus.Success => Results.Ok(ToFinancialEntryResponse(result.Entry!)),
        FinancialEntrySaveStatus.PatientUnavailable => Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(input.PatientId)] = ["Selecione um paciente ativo do workspace."] }),
        FinancialEntrySaveStatus.NotFound => Results.NotFound(),
        _ => throw new InvalidOperationException("Resultado inesperado ao editar lançamento financeiro.")
    };
});

financialEntries.MapDelete("/{id:guid}", async (Guid id, HttpContext context, FinancialStore store) =>
    await store.DeleteAsync(GetWorkspaceId(context.User), id) ? Results.NoContent() : Results.NotFound());

app.Run();

static string GetWorkspaceId(ClaimsPrincipal user) =>
    user.FindFirstValue("workspace_id") ?? throw new InvalidOperationException("Workspace não encontrado na sessão.");

static string GetActorName(ClaimsPrincipal user) =>
    user.Identity?.Name ?? user.FindFirstValue(ClaimTypes.NameIdentifier)
    ?? throw new InvalidOperationException("Responsável pela alteração não encontrado na sessão.");

static bool FixedTimeEquals(string left, string right)
{
    var leftHash = SHA256.HashData(Encoding.UTF8.GetBytes(left));
    var rightHash = SHA256.HashData(Encoding.UTF8.GetBytes(right));
    return CryptographicOperations.FixedTimeEquals(leftHash, rightHash);
}

static (Dictionary<string, string[]> Errors, long AmountCents) ValidateFinancialEntry(FinancialEntryInput input)
{
    var errors = new Dictionary<string, string[]>();
    if (string.IsNullOrWhiteSpace(input.Description))
        errors[nameof(input.Description)] = ["Informe a descrição do lançamento."];
    else if (input.Description.Trim().Length > 200)
        errors[nameof(input.Description)] = ["A descrição deve ter no máximo 200 caracteres."];
    if (input.Amount <= 0 || input.Amount > 999999999.99m || decimal.Round(input.Amount, 2) != input.Amount)
        errors[nameof(input.Amount)] = ["Informe um valor positivo com até duas casas decimais."];
    if (!string.Equals(input.Type, "revenue", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(input.Type, "expense", StringComparison.OrdinalIgnoreCase))
        errors[nameof(input.Type)] = ["Selecione receita ou despesa."];
    if (!string.Equals(input.Status, "paid", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(input.Status, "pending", StringComparison.OrdinalIgnoreCase))
        errors[nameof(input.Status)] = ["Selecione pago ou pendente."];
    if (input.EntryDate == default)
        errors[nameof(input.EntryDate)] = ["Informe a data do lançamento."];
    if (input.PatientId == Guid.Empty)
        errors[nameof(input.PatientId)] = ["Selecione um paciente válido ou deixe o campo vazio."];

    return (errors, errors.ContainsKey(nameof(input.Amount)) ? 0 : decimal.ToInt64(input.Amount * 100));
}

static Dictionary<string, string[]> ValidateAppointment(AppointmentInput input, bool allowCancelled = false)
{
    var errors = new Dictionary<string, string[]>();
    if (input.PatientId == Guid.Empty)
        errors[nameof(input.PatientId)] = ["Selecione um paciente."];
    if (input.StartsAt == default || input.EndsAt == default || input.EndsAt <= input.StartsAt)
        errors[nameof(input.EndsAt)] = ["O término deve ser posterior ao início do compromisso."];
    if (input.Amount < 0 || input.Amount > 999999999.99m || decimal.Round(input.Amount, 2) != input.Amount)
        errors[nameof(input.Amount)] = ["Informe um valor igual ou maior que zero, com até duas casas decimais."];
    if (!string.Equals(input.Modality, "in_person", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(input.Modality, "online", StringComparison.OrdinalIgnoreCase))
        errors[nameof(input.Modality)] = ["Selecione uma modalidade válida."];
    var validStatuses = allowCancelled
        ? new[] { "scheduled", "confirmed", "attended", "missed", "cancelled" }
        : new[] { "scheduled", "confirmed", "attended", "missed" };
    if (!validStatuses.Contains(input.Status, StringComparer.OrdinalIgnoreCase))
        errors[nameof(input.Status)] = ["Selecione um status válido."];
    if (input.Notes?.Length > 2000)
        errors[nameof(input.Notes)] = ["As observações devem ter no máximo 2000 caracteres."];
    return errors;
}

static Dictionary<string, string[]> ValidateSession(SessionInput input)
{
    var errors = new Dictionary<string, string[]>();
    if (input.SessionDate == default)
        errors[nameof(input.SessionDate)] = ["Informe a data da sessão."];
    if (string.IsNullOrWhiteSpace(input.Note))
        errors[nameof(input.Note)] = ["Informe uma anotação para a sessão."];
    else if (input.Note.Trim().Length > 20000)
        errors[nameof(input.Note)] = ["A anotação deve ter no máximo 20000 caracteres."];
    return errors;
}

static Dictionary<string, string[]> ValidatePatientTask(PatientTaskInput input)
{
    var errors = new Dictionary<string, string[]>();
    if (string.IsNullOrWhiteSpace(input.Description))
        errors[nameof(input.Description)] = ["Informe a descrição da pendência."];
    else if (input.Description.Trim().Length > 2000)
        errors[nameof(input.Description)] = ["A descrição deve ter no máximo 2000 caracteres."];
    return errors;
}

static Dictionary<string, string[]> Validate(PatientInput input)
{
    var errors = new Dictionary<string, string[]>();
    if (string.IsNullOrWhiteSpace(input.Name))
        errors[nameof(input.Name)] = ["Informe o nome do paciente."];
    else if (input.Name.Trim().Length > 150)
        errors[nameof(input.Name)] = ["O nome deve ter no máximo 150 caracteres."];
    if (input.Contact?.Trim().Length > 200)
        errors[nameof(input.Contact)] = ["O contato deve ter no máximo 200 caracteres."];
    return errors;
}

static string? NormalizeContact(string? contact) =>
    string.IsNullOrWhiteSpace(contact) ? null : contact.Trim();

static PatientResponse ToResponse(PatientRecord record) => new(
    record.Id, record.Name, record.Contact, record.IsArchived, record.CreatedAt, record.UpdatedAt);

static AppointmentResponse ToAppointmentResponse(AppointmentRecord record) => new(
    record.Id, record.PatientId, record.PatientName, record.StartsAt, record.EndsAt,
    record.IsCancelled, record.AmountCents / 100m, record.Modality, record.Status, record.Notes,
    record.CreatedAt, record.UpdatedAt);

static SessionResponse ToSessionResponse(SessionRecord record) => new(
    record.Id, record.SessionDate, record.Note, record.CreatedAt, record.UpdatedAt);

static SessionVersionResponse ToSessionVersionResponse(SessionVersionRecord record) => new(
    record.Version, record.SessionDate, record.Note, record.ChangedAt, record.ChangedBy);

static PatientTaskResponse ToPatientTaskResponse(PatientTaskRecord record) => new(
    record.Id, record.Description, record.DueDate, record.IsCompleted, record.CreatedAt, record.UpdatedAt);

static FinancialEntryResponse ToFinancialEntryResponse(FinancialEntryRecord record) => new(
    record.Id, record.PatientId, record.PatientName, record.Description, record.Type, record.Status,
    record.AmountCents / 100m, record.EntryDate, record.CreatedAt, record.UpdatedAt);

public sealed record LoginRequest(string Email, string Password);
public sealed record UserResponse(string Name, string Email);
public sealed record PatientInput(string Name, string? Contact);
public sealed record PatientResponse(Guid Id, string Name, string? Contact, bool IsArchived, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record AppointmentInput(
    Guid PatientId, DateTimeOffset StartsAt, DateTimeOffset EndsAt,
    decimal Amount = 0, string Modality = "in_person", string Status = "scheduled", string Notes = "");
public sealed record AppointmentResponse(
    Guid Id, Guid PatientId, string PatientName, DateTimeOffset StartsAt, DateTimeOffset EndsAt,
    bool IsCancelled, decimal Amount, string Modality, string Status, string Notes,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record SessionInput(DateOnly SessionDate, string Note);
public sealed record SessionResponse(Guid Id, DateOnly SessionDate, string Note, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record SessionVersionResponse(int Version, DateOnly SessionDate, string Note, DateTimeOffset ChangedAt, string ChangedBy);
public sealed record PatientTaskInput(string Description, DateOnly? DueDate, bool IsCompleted);
public sealed record PatientTaskResponse(Guid Id, string Description, DateOnly? DueDate, bool IsCompleted, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record FinancialEntryInput(Guid? PatientId, string Description, string Type, string Status, decimal Amount, DateOnly EntryDate);
public sealed record FinancialEntryResponse(Guid Id, Guid? PatientId, string? PatientName, string Description, string Type, string Status, decimal Amount, DateOnly EntryDate, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

