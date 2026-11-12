using Microsoft.AspNetCore.Identity;

namespace QueryDoctor.Infrastructure.Persistence;

/// <summary>Rol de la plataforma (DBA, Desarrollador). Tabla ops.roles.</summary>
public sealed class ApplicationRole : IdentityRole<Guid>
{
}
