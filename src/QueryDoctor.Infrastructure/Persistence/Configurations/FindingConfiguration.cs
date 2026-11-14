using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueryDoctor.Domain.Analysis;
using QueryDoctor.Domain.Core;

namespace QueryDoctor.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración de la tabla <c>analysis.finding</c> (S1-13, RF-MOT-03, RF-MOT-17, RNF-SEG-14, Arquitectura §7).
/// </summary>
public sealed class FindingConfiguration : IEntityTypeConfiguration<Finding>
{
    public void Configure(EntityTypeBuilder<Finding> builder)
    {
        builder.ToTable("finding", "analysis");
        builder.HasKey(f => f.Id);

        builder.Property(f => f.FindingCode).IsRequired().HasMaxLength(64);
        builder.Property(f => f.RuleId).IsRequired().HasMaxLength(16);
        builder.Property(f => f.RuleVersion).IsRequired().HasMaxLength(32);
        builder.Property(f => f.CatalogVersion).IsRequired().HasMaxLength(32);
        builder.Property(f => f.SchemaVersion).IsRequired().HasMaxLength(16);
        builder.Property(f => f.Kind).HasConversion<string>().HasMaxLength(32);
        builder.Property(f => f.Severity).HasConversion<string>().HasMaxLength(32);
        builder.Property(f => f.EvidenceClass).HasConversion<string>().HasMaxLength(32);
        builder.Property(f => f.NodesJson).IsRequired().HasColumnType("text");
        builder.Property(f => f.EncryptedEvidenceJson).IsRequired().HasColumnType("text");
        builder.Property(f => f.ContextJson).IsRequired().HasColumnType("text");
        builder.Property(f => f.ThresholdsUsedJson).IsRequired().HasColumnType("text");
        builder.Property(f => f.EngineVersion).HasMaxLength(64);

        // En una misma corrida de análisis, no pueden existir dos hallazgos con el mismo finding_id determinístico.
        builder.HasIndex(f => new { f.AnalysisRunId, f.FindingCode }).IsUnique();
        builder.HasIndex(f => f.PlanId);
        builder.HasIndex(f => f.QueryId);
        builder.HasIndex(f => new { f.ConnectionId, f.Severity });

        builder.HasOne<AnalysisRun>()
            .WithMany()
            .HasForeignKey(f => f.AnalysisRunId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Connection>()
            .WithMany()
            .HasForeignKey(f => f.ConnectionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Query>()
            .WithMany()
            .HasForeignKey(f => f.QueryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Plan>()
            .WithMany()
            .HasForeignKey(f => f.PlanId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
