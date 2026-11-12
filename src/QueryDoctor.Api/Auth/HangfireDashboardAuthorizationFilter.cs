using System.Security.Claims;
using Hangfire.Dashboard;
using QueryDoctor.Domain.Ops;

namespace QueryDoctor.Api.Auth;

/// <summary>
/// Filtro de autorización para el panel de monitoreo de Hangfire (/hangfire).
/// Exige que el usuario posea una sesión autenticada con rol Administrador/DBA
/// y no tenga pendiente el cambio obligatorio de contraseña (ADR-04, RNF-SEG-11, RNF-SEG-12).
/// </summary>
public sealed class HangfireDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    /// <inheritdoc />
    public bool Authorize(DashboardContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var httpContext = context.GetHttpContext();
        return IsAuthorized(httpContext?.User);
    }

    /// <summary>
    /// Evalúa si el usuario autenticado cumple las políticas para acceder al panel administrativo de Hangfire.
    /// </summary>
    public static bool IsAuthorized(ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        var isDba = user.IsInRole(RoleNames.Dba);
        var mustChangePassword = user.HasClaim(
            c => c.Type == AuthConstants.MustChangePasswordClaim && c.Value == "true");

        return isDba && !mustChangePassword;
    }
}
