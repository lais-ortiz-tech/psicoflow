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

var app = builder.Build();
await app.Services.GetRequiredService<PatientStore>().InitializeAsync();

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

app.Run();

static string GetWorkspaceId(ClaimsPrincipal user) =>
    user.FindFirstValue("workspace_id") ?? throw new InvalidOperationException("Workspace não encontrado na sessão.");

static bool FixedTimeEquals(string left, string right)
{
    var leftHash = SHA256.HashData(Encoding.UTF8.GetBytes(left));
    var rightHash = SHA256.HashData(Encoding.UTF8.GetBytes(right));
    return CryptographicOperations.FixedTimeEquals(leftHash, rightHash);
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

public sealed record LoginRequest(string Email, string Password);
public sealed record UserResponse(string Name, string Email);
public sealed record PatientInput(string Name, string? Contact);
public sealed record PatientResponse(Guid Id, string Name, string? Contact, bool IsArchived, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

