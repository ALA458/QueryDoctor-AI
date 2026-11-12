using Microsoft.AspNetCore.Identity;

namespace QueryDoctor.Infrastructure.Persistence;

/// <summary>
/// Usuario de la plataforma (ASP.NET Core Identity). Tabla ops.users.
/// El bloqueo por intentos fallidos usa los campos de Identity (RNF-SEG-13).
/// </summary>
public sealed class ApplicationUser : IdentityUser<Guid>
{
    /// <summary>Obliga a cambiar la contraseña en el primer ingreso (S1-06).</summary>
    public bool MustChangePassword { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
