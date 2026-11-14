using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueryDoctor.Domain.Core;

namespace QueryDoctor.Infrastructure.Persistence.Configurations;

public sealed class ConnectionConfiguration : IEntityTypeConfiguration<Connection>
{
    /// <summary>Propiedad sombra: columna generada database_name_key = lower(database_name).</summary>
    public const string DatabaseNameKeyProperty = "DatabaseNameKey";

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
        builder.Property(c => c.ProbeFailureCause).HasConversion<string>().HasMaxLength(32);

        // S1-09: server ya llega normalizado en minúsculas (ConnectionAddress). database_name conserva
        // las mayúsculas (intercalaciones CS), así que la unicidad usa una columna generada lower(...).
        builder.Property<string>(DatabaseNameKeyProperty)
            .HasMaxLength(128)
            .HasComputedColumnSql("lower(database_name)", stored: true);

        // Una misma base no se registra dos veces, sin distinguir mayúsculas.
        builder.HasIndex(nameof(Connection.Server), DatabaseNameKeyProperty).IsUnique();
    }
}
