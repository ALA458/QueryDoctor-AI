using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueryDoctor.Domain.Analysis;
using QueryDoctor.Domain.Core;

namespace QueryDoctor.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración de la tabla <c>analysis.analysis_run</c> (S1-13, RF-MOT-02, RF-MOT-14, Arquitectura §6.3 y §7).
/// </summary>
public sealed class AnalysisRunConfiguration : IEntityTypeConfiguration<AnalysisRun>
{
    public void Configure(EntityTypeBuilder<AnalysisRun> builder)
    {
        builder.ToTable("analysis_run", "analysis");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.CatalogVersion).IsRequired().HasMaxLength(32);
        builder.Property(a => a.EngineVersion).HasMaxLength(64);
        builder.Property(a => a.ThresholdsUsedJson).IsRequired().HasColumnType("text");
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(a => a.ErrorCause).HasMaxLength(1024);

        builder.HasIndex(a => new { a.PlanId, a.StartedAt });
        builder.HasIndex(a => a.ConnectionId);

        builder.HasOne<Connection>()
            .WithMany()
            .HasForeignKey(a => a.ConnectionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Query>()
            .WithMany()
            .HasForeignKey(a => a.QueryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Plan>()
            .WithMany()
            .HasForeignKey(a => a.PlanId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<ThresholdSet>()
            .WithMany()
            .HasForeignKey(a => a.ThresholdSetId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
