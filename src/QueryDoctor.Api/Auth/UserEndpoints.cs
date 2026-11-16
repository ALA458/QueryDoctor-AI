using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using QueryDoctor.Api.Auditing;
using QueryDoctor.Application.Traceability;
using QueryDoctor.Domain.Audit;
using QueryDoctor.Domain.Ops;
using QueryDoctor.Infrastructure.Persistence;

namespace QueryDoctor.Api.Auth;

internal sealed record CreateDeveloperUserRequest(string? UserName, string? Email, string? Password)
{
    public override string ToString() =>
        $"CreateDeveloperUserRequest {{ UserName = {UserName}, Email = {Email}, Password = ******** }}";
}

/// <summary>
/// Alta de cuentas con rol Desarrollador (S2-12 D6, RNF-SEG-11/12/13). Solo DBA y con cambio obligatorio de contraseña.
/// </summary>
internal static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/users", CreateDeveloperAsync).RequireAuthorization(AuthConstants.DbaOnlyPolicy);
        return app;
    }

    private static async Task<IResult> CreateDeveloperAsync(
        CreateDeveloperUserRequest? body,
        UserManager<ApplicationUser> users,
        ClaimsPrincipal principal,
        IAuditLog audit,
        CancellationToken cancellationToken)
    {
        if (body is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, detail: "Falta el cuerpo de la solicitud.");
        }

        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        var userName = body.UserName?.Trim() ?? string.Empty;
        var email = body.Email?.Trim() ?? string.Empty;
        var password = body.Password ?? string.Empty;

        if (userName.Length == 0)
        {
            errors["userName"] = ["El nombre de usuario es obligatorio."];
        }

        if (email.Length == 0 || !email.Contains('@', StringComparison.Ordinal))
        {
            errors["email"] = ["Ingresa un correo válido."];
        }

        if (password.Length == 0)
        {
            errors["password"] = ["La contraseña inicial es obligatoria."];
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors, detail: "Revisa los datos del usuario.");
        }

        var user = new ApplicationUser
        {
            UserName = userName,
            Email = email,
            EmailConfirmed = true,
            MustChangePassword = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var created = await users.CreateAsync(user, password);
        if (!created.Succeeded)
        {
            var identityErrors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["user"] = created.Errors.Select(e => e.Description).ToArray(),
            };
            return Results.ValidationProblem(identityErrors, detail: "No se pudo crear el usuario.");
        }

        await users.AddToRoleAsync(user, RoleNames.Developer);

        await audit.AppendAsync(
            new AuditEventDraft(
                AuditEventType.DeveloperUserCreated,
                AuditTrail.UserId(principal),
                AuditTrail.UserName(principal),
                AuditEventDraft.OutcomeOk,
                AuditEventDraft.SubjectUser,
                user.Id,
                RelatedUserId: user.Id),
            cancellationToken);

        return Results.Created(
            $"/users/{user.Id:D}",
            new
            {
                id = user.Id,
                userName = user.UserName,
                email = user.Email,
                role = RoleNames.Developer,
                mustChangePassword = true,
            });
    }
}
