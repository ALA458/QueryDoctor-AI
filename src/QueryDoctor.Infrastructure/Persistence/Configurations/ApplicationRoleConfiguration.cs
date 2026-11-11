using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueryDoctor.Domain.Ops;

namespace QueryDoctor.Infrastructure.Persistence.Configurations;

/// <summary>
/// Siembra los dos roles de la plataforma con identificadores fijos para que la migración sea estable.
/// Las políticas de acceso por rol llegan en S1-06 y S2-12.
/// </summary>
public sealed class ApplicationRoleConfiguration : IEntityTypeConfiguration<ApplicationRole>
{
    private static readonly Guid DbaRoleId = new("6f1c3b0e-8a52-4d0b-9c1e-2a7f4e5d1001");
    private static readonly Guid DeveloperRoleId = new("6f1c3b0e-8a52-4d0b-9c1e-2a7f4e5d1002");

    public void Configure(EntityTypeBuilder<ApplicationRole> builder)
    {
        builder.HasData(
            new ApplicationRole
            {
                Id = DbaRoleId,
                Name = RoleNames.Dba,
                NormalizedName = "DBA",
                ConcurrencyStamp = "role-dba-v1",
            },
            new ApplicationRole
            {
                Id = DeveloperRoleId,
                Name = RoleNames.Developer,
                NormalizedName = "DESARROLLADOR",
                ConcurrencyStamp = "role-desarrollador-v1",
            });
    }
}
