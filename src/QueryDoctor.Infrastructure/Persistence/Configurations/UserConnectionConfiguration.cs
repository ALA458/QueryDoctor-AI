using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueryDoctor.Domain.Core;
using QueryDoctor.Domain.Ops;

namespace QueryDoctor.Infrastructure.Persistence.Configurations;

public sealed class UserConnectionConfiguration : IEntityTypeConfiguration<UserConnection>
{
    public void Configure(EntityTypeBuilder<UserConnection> builder)
    {
        builder.ToTable("user_connection", "ops");

        // La clave compuesta impide asignar dos veces la misma conexión al mismo usuario.
        builder.HasKey(uc => new { uc.UserId, uc.ConnectionId });

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(uc => uc.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Connection>()
            .WithMany()
            .HasForeignKey(uc => uc.ConnectionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
