using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueryDoctor.Domain.Core;

namespace QueryDoctor.Infrastructure.Persistence.Configurations;

public sealed class RuntimeStatConfiguration : IEntityTypeConfiguration<RuntimeStat>
{
    public void Configure(EntityTypeBuilder<RuntimeStat> builder)
    {
        builder.ToTable("runtime_stat", "core");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedOnAdd();

        builder.Property(r => r.ExecutionType).HasConversion<string>().HasMaxLength(32);

        // Clave natural: hace idempotente el upsert de la ingesta (RF-ING-05).
        builder.HasIndex(r => new { r.ConnectionId, r.PlanId, r.ExecutionType, r.IntervalId }).IsUnique();

        // Índice para el tablero (arquitectura §7).
        builder.HasIndex(r => new { r.ConnectionId, r.IntervalEnd });

        builder.HasOne<Connection>()
            .WithMany()
            .HasForeignKey(r => r.ConnectionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Plan>()
            .WithMany()
            .HasForeignKey(r => r.PlanId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
