using Microsoft.AspNetCore.Identity;
using QueryDoctor.Domain.Ops;
using QueryDoctor.Infrastructure.Persistence;

namespace QueryDoctor.Api.Auth;

/// <summary>
/// Crea el primer usuario DBA al arrancar, con la contraseña tomada de un secreto (archivo) y
/// cambio obligatorio en el primer ingreso (RNF-SEG-11/13). Nunca escribe la contraseña en logs.
/// </summary>
internal static partial class InitialDbaSeeder
{
    public static async Task SeedAsync(IServiceProvider services, IConfiguration configuration)
    {
        var passwordFile = configuration["InitialDba:PasswordFile"];
        if (string.IsNullOrWhiteSpace(passwordFile) || !File.Exists(passwordFile))
        {
            return;
        }

        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("InitialDba");
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();

        if ((await users.GetUsersInRoleAsync(RoleNames.Dba)).Count > 0)
        {
            return;
        }

        var password = (await File.ReadAllTextAsync(passwordFile)).Trim();
        var userName = configuration["InitialDba:UserName"] ?? "dba";

        var user = new ApplicationUser
        {
            UserName = userName,
            Email = configuration["InitialDba:Email"] ?? "dba@querydoctor.local",
            EmailConfirmed = true,
            MustChangePassword = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var created = await users.CreateAsync(user, password);
        if (!created.Succeeded)
        {
            // Solo códigos de error (p. ej. PasswordTooShort); la contraseña no se imprime.
            throw new InvalidOperationException(
                "No se pudo crear el DBA inicial: " + string.Join(", ", created.Errors.Select(e => e.Code)));
        }

        await users.AddToRoleAsync(user, RoleNames.Dba);
        LogCreated(logger, userName);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "DBA inicial '{UserName}' creado; debe cambiar su contraseña en el primer ingreso.")]
    private static partial void LogCreated(ILogger logger, string userName);
}
