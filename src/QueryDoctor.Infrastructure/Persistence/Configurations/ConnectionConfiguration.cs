using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueryDoctor.Domain.Core;

namespace QueryDoctor.Infrastructure.Persistence.Configurations;

public sealed class ConnectionConfiguration : IEntityTypeConfiguration<Connection>
{
    public void Configure(EntityTypeBuilder<Connection> builder)
    {
        builder.ToTable("connection", "core");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Platform).HasConversion<string>().HasMaxLength(32);
        builder.Property(c => c.Server).HasMaxLength(255).IsRequired();
        builder.Property(c => c.DatabaseName).HasMaxLength(128).IsRequired();
        builder.Property(c => c.AuthenticationMethod).HasConversion<string>().HasMaxLength(32);
        builder.Property(c => c.Encryption).HasConversion<string>().HasMaxLength(32);
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(c => c.EncryptedCredentials).HasColumnType("text");

        // Una misma base no se registra dos veces.
        builder.HasIndex(c => new { c.Server, c.DatabaseName }).IsUnique();
    }
}
