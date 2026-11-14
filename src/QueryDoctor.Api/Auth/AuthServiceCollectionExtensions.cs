using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using QueryDoctor.Domain.Ops;
using QueryDoctor.Infrastructure.Persistence;

namespace QueryDoctor.Api.Auth;

/// <summary>
/// Identity + cookie de sesión + autorización con denegación por defecto (RNF-SEG-11/12/13).
/// </summary>
internal static class AuthServiceCollectionExtensions
{
    private const int MaxFailedAttempts = 5;

    public static IServiceCollection AddQueryDoctorAuth(this IServiceCollection services, IConfiguration configuration)
    {
        if (!services.Any(s => s.ServiceType == typeof(AppDbContext)))
        {
            throw new InvalidOperationException(
                "Falta la conexión a PostgreSQL: define ConnectionStrings:QueryDoctor o Postgres:PasswordFile.");
        }

        services
            .AddIdentity<ApplicationUser, ApplicationRole>(options =>
            {
                options.Password.RequiredLength = 12;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;

                // 5 intentos fallidos bloquean la cuenta (S1-06, RNF-SEG-13).
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = MaxFailedAttempts;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);

                options.User.RequireUniqueEmail = true;
            })
            .AddEntityFrameworkStores<AppDbContext>()
            // El claim de cambio pendiente debe sobrevivir a la revalidación por petición (fix S1-09, RNF-SEG-13).
            .AddClaimsPrincipalFactory<MustChangePasswordClaimsPrincipalFactory>();

        // Inactividad configurable, 30 min por defecto (RNF-SEG-13).
        var idleMinutes = configuration.GetValue("Auth:IdleTimeoutMinutes", 30);

        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = "qd.session";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.ExpireTimeSpan = TimeSpan.FromMinutes(idleMinutes);
            options.SlidingExpiration = true;

            // La API responde códigos HTTP, no redirige: sin sesión es 401 y sin permiso 403.
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

        // Cerrar sesión o cambiar contraseña invalida las cookies ya emitidas: se revisa el sello en cada petición.
        services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.Zero);

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .RequireAssertion(context => !HasPendingPasswordChange(context))
                .Build())
            .AddPolicy(AuthConstants.SessionPolicy, policy => policy.RequireAuthenticatedUser())
            .AddPolicy(AuthConstants.DbaOnlyPolicy, policy => policy
                .RequireAuthenticatedUser()
                .RequireRole(RoleNames.Dba)
                .RequireAssertion(context => !HasPendingPasswordChange(context)));

        return services;
    }

    private static bool HasPendingPasswordChange(AuthorizationHandlerContext context) =>
        context.User.HasClaim(AuthConstants.MustChangePasswordClaim, "1");
}
