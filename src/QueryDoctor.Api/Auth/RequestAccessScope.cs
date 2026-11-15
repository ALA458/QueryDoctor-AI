using System.Security.Claims;
using QueryDoctor.Application.Common.Security;
using QueryDoctor.Domain.Ops;

namespace QueryDoctor.Api.Auth;

/// <summary>
/// Contexto de acceso de una solicitud de la Api (S2-12, RNF-SEG-12). Nace en <see cref="AccessKind.None"/> (cero filas)
/// y solo <see cref="AccessScopeMiddleware"/> lo inicializa, una vez, a partir del usuario autenticado. La Api nunca
/// usa el acceso Sistema.
/// </summary>
internal sealed class RequestAccessScope : IAccessScope
{
    private Guid[] _assigned = [];
    private bool _initialized;

    public AccessKind Kind { get; private set; } = AccessKind.None;

    public IReadOnlyCollection<Guid> AssignedConnectionIds => _assigned;

    public void InitializeDba() => Initialize(AccessKind.Dba, []);

    public void InitializeDeveloper(Guid[] assignedConnectionIds) =>
        Initialize(AccessKind.Developer, assignedConnectionIds ?? throw new ArgumentNullException(nameof(assignedConnectionIds)));

    private void Initialize(AccessKind kind, Guid[] assigned)
    {
        if (_initialized)
        {
            throw new InvalidOperationException("El contexto de acceso de la solicitud ya se inicializó.");
        }

        _initialized = true;
        Kind = kind;
        _assigned = assigned;
    }
}

/// <summary>
/// Inicializa <see cref="RequestAccessScope"/> después de la autorización y antes del endpoint (S2-12). Las
/// asignaciones se leen de la base en cada solicitud: quitar una surte efecto en la siguiente. Sin usuario, con cambio
/// de contraseña pendiente o con un rol desconocido el scope queda en None: denegación por defecto.
/// </summary>
internal sealed class AccessScopeMiddleware
{
    private readonly RequestDelegate _next;

    public AccessScopeMiddleware(RequestDelegate next)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
    }

    public async Task InvokeAsync(HttpContext context, RequestAccessScope scope, IAssignedConnectionsReader assignments)
    {
        await InitializeAsync(context.User, scope, assignments, context.RequestAborted);
        await _next(context);
    }

    internal static async Task InitializeAsync(
        ClaimsPrincipal user,
        RequestAccessScope scope,
        IAssignedConnectionsReader assignments,
        CancellationToken cancellationToken)
    {
        if (user.Identity?.IsAuthenticated != true || user.HasClaim(AuthConstants.MustChangePasswordClaim, "1"))
        {
            return;
        }

        // Con los dos roles gana DBA: ya ve todo.
        if (user.IsInRole(RoleNames.Dba))
        {
            scope.InitializeDba();
            return;
        }

        if (user.IsInRole(RoleNames.Developer)
            && Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            scope.InitializeDeveloper(await assignments.ForUserAsync(userId, cancellationToken));
        }
    }
}

/// <summary>Registro y uso del contexto de acceso de la Api.</summary>
internal static class AccessScopeExtensions
{
    public static IServiceCollection AddRequestAccessScope(this IServiceCollection services)
    {
        services.AddScoped<RequestAccessScope>();
        services.AddScoped<IAccessScope>(sp => sp.GetRequiredService<RequestAccessScope>());
        return services;
    }

    /// <summary>Va después de UseAuthorization: una solicitud rechazada (401/403) no llega a leer asignaciones.</summary>
    public static IApplicationBuilder UseRequestAccessScope(this IApplicationBuilder app) =>
        app.UseMiddleware<AccessScopeMiddleware>();
}
