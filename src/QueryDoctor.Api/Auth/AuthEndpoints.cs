using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using QueryDoctor.Api.Auditing;
using QueryDoctor.Application.Common.Security;
using QueryDoctor.Application.Traceability;
using QueryDoctor.Domain.Audit;
using QueryDoctor.Infrastructure.Persistence;

namespace QueryDoctor.Api.Auth;

/// <summary>
/// Login, logout y cambio de contraseña. Aceptan formularios HTML y responden con redirecciones,
/// para que el front (HTML estático o PHP) no maneje contraseñas ni sesiones (FRONT_PHP.md §5).
/// La verificación de origen (CSRF) se hace antes, en <see cref="SameOriginFormsExtensions"/>.
/// </summary>
internal static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth");

        group.MapPost("/login", LoginAsync).AllowAnonymous();
        group.MapPost("/logout", LogoutAsync).RequireAuthorization(AuthConstants.SessionPolicy);
        group.MapPost("/change-password", ChangePasswordAsync).RequireAuthorization(AuthConstants.SessionPolicy);
        group.MapGet("/me", MeAsync).RequireAuthorization(AuthConstants.SessionPolicy);

        return app;
    }

    private static async Task<IResult> LoginAsync(
        HttpRequest request,
        UserManager<ApplicationUser> users,
        SignInManager<ApplicationUser> signIn,
        IAuditLog audit,
        ILoggerFactory loggers)
    {
        // S2-06 (RF-TRZ-05): cada salida deja su evento. Nunca se registra la contraseña; un fallo de la auditoría no
        // impide entrar (T7).
        var logger = loggers.CreateLogger(typeof(AuditTrail));
        Task Audit(AuditEventType type, ApplicationUser? who, string? typedName, string outcome) =>
            AuditTrail.RecordTolerantAsync(
                audit,
                new AuditEventDraft(type, who?.Id, who?.UserName ?? (string.IsNullOrEmpty(typedName) ? null : typedName), outcome),
                logger,
                request.HttpContext.RequestAborted);

        // Sin cuerpo de formulario se trata como formulario vacío: más abajo cae en el mensaje de error normal.
        IFormCollection form = request.HasFormContentType ? await request.ReadFormAsync() : FormCollection.Empty;
        // FRONT_PHP.md usa el campo "email"; la pantalla propia usa "usuario". Se aceptan ambos.
        var userName = (form.ContainsKey("usuario") ? form["usuario"] : form["email"]).ToString().Trim();
        var password = form["password"].ToString();
        var returnUrl = LocalUrl.Sanitize(form["returnUrl"].ToString());

        if (userName.Length == 0 || password.Length == 0)
        {
            await Audit(AuditEventType.LoginFailed, null, userName, AuditEventDraft.OutcomeRejected);
            return Redirect(AuthConstants.LoginPage, "error", "1", returnUrl);
        }

        var user = await users.FindByNameAsync(userName) ?? await users.FindByEmailAsync(userName);
        if (user is null)
        {
            await Audit(AuditEventType.LoginFailed, null, userName, AuditEventDraft.OutcomeUnknownUser);
            return Redirect(AuthConstants.LoginPage, "error", "1", returnUrl);
        }

        // lockoutOnFailure: cuenta el intento fallido y bloquea al llegar a 5; un acierto reinicia el contador.
        var result = await signIn.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
        if (result.IsLockedOut)
        {
            await Audit(AuditEventType.LoginLockedOut, user, userName, AuditEventDraft.OutcomeLocked);
            return Redirect(AuthConstants.LoginPage, "error", "locked", returnUrl);
        }

        if (!result.Succeeded)
        {
            await Audit(AuditEventType.LoginFailed, user, userName, AuditEventDraft.OutcomeRejected);
            return Redirect(AuthConstants.LoginPage, "error", "1", returnUrl);
        }

        await Audit(AuditEventType.LoginSucceeded, user, userName, AuditEventDraft.OutcomeOk);

        if (user.MustChangePassword)
        {
            // Sesión restringida: solo puede cambiar la contraseña; el resto de la API responde 403.
            // El claim lo agrega MustChangePasswordClaimsPrincipalFactory, también en cada revalidación (fix S1-09).
            await signIn.SignInAsync(user, isPersistent: false);

            return Redirect(AuthConstants.ChangePasswordPage, null, null, returnUrl);
        }

        await signIn.SignInAsync(user, isPersistent: false);
        return Results.Redirect(returnUrl);
    }

    private static async Task<IResult> LogoutAsync(
        HttpRequest request,
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> users,
        SignInManager<ApplicationUser> signIn,
        IAuditLog audit,
        ILoggerFactory loggers)
    {
        var user = await users.GetUserAsync(principal);
        await AuditTrail.RecordTolerantAsync(
            audit,
            new AuditEventDraft(AuditEventType.Logout, user?.Id, user?.UserName, AuditEventDraft.OutcomeOk),
            loggers.CreateLogger(typeof(AuditTrail)),
            request.HttpContext.RequestAborted);
        if (user is not null)
        {
            // Cambiar el sello invalida la cookie en el servidor, no solo en el navegador (RNF-SEG-13).
            await users.UpdateSecurityStampAsync(user);
        }

        await signIn.SignOutAsync();
        return Redirect(AuthConstants.LoginPage, "bye", "1", "/");
    }

    private static async Task<IResult> ChangePasswordAsync(
        HttpRequest request,
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> users,
        SignInManager<ApplicationUser> signIn,
        IAuditLog audit,
        ILoggerFactory loggers)
    {
        // Sin cuerpo de formulario se trata como formulario vacío: más abajo cae en el mensaje de error normal.
        IFormCollection form = request.HasFormContentType ? await request.ReadFormAsync() : FormCollection.Empty;
        var current = form["actual"].ToString();
        var next = form["nueva"].ToString();
        var confirm = form["confirmar"].ToString();
        var returnUrl = LocalUrl.Sanitize(form["returnUrl"].ToString());

        if (next != confirm)
        {
            return Redirect(AuthConstants.ChangePasswordPage, "error", "coinciden", returnUrl);
        }

        if (next == current)
        {
            return Redirect(AuthConstants.ChangePasswordPage, "error", "igual", returnUrl);
        }

        var user = await users.GetUserAsync(principal);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var result = await users.ChangePasswordAsync(user, current, next);
        if (!result.Succeeded)
        {
            var wrongCurrent = result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.PasswordMismatch));
            return Redirect(AuthConstants.ChangePasswordPage, "error", wrongCurrent ? "actual" : "politica", returnUrl);
        }

        user.MustChangePassword = false;
        await users.UpdateAsync(user);
        await AuditTrail.RecordTolerantAsync(
            audit,
            new AuditEventDraft(AuditEventType.PasswordChanged, user.Id, user.UserName, AuditEventDraft.OutcomeOk),
            loggers.CreateLogger(typeof(AuditTrail)),
            request.HttpContext.RequestAborted);

        // La sesión anterior queda invalidada (cambió el sello): se vuelve a entrar con la contraseña nueva.
        await signIn.SignOutAsync();
        return Redirect(AuthConstants.LoginPage, "changed", "1", returnUrl);
    }

    private static async Task<IResult> MeAsync(ClaimsPrincipal principal, UserManager<ApplicationUser> users)
    {
        var user = await users.GetUserAsync(principal);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var roles = await users.GetRolesAsync(user);
        return Results.Ok(new
        {
            userName = user.UserName,
            roles,
            mustChangePassword = user.MustChangePassword,
        });
    }

    private static IResult Redirect(string page, string? key, string? value, string returnUrl)
    {
        var query = new List<string>();
        if (key is not null && value is not null)
        {
            query.Add($"{key}={Uri.EscapeDataString(value)}");
        }

        if (returnUrl != "/")
        {
            query.Add($"returnUrl={Uri.EscapeDataString(returnUrl)}");
        }

        var url = query.Count == 0 ? page : $"{page}?{string.Join('&', query)}";
        return Results.Redirect(url);
    }
}
