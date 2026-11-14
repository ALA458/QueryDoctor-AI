using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueryDoctor.Domain.Analysis;
using QueryDoctor.Domain.Core;

namespace QueryDoctor.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración de la tabla <c>analysis.health_score</c> para persistir el puntaje compuesto <c>H(q)</c>,
/// su desglose, los parámetros empleados y la serie histórica por consulta (S2-02, RF-SCO-01..06, E-07).
/// </summary>
public sealed class HealthScoreConfiguration : IEntityTypeConfiguration<HealthScore>
{
    public void Configure(EntityTypeBuilder<HealthScore> builder)
    {
        builder.ToTable("health_score", "analysis");
        builder.HasKey(h => h.Id);

        builder.Property(h => h.Band)
            .HasConversion(
                band => band.ToContractCode(),
                code => AnalysisEnumCodes.ParseHealthScoreBand(code))
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(h => h.ContributingFindingsJson)
            .IsRequired()
            .HasColumnType("text");

        builder.Property(h => h.ParametersJson)
            .IsRequired()
            .HasColumnType("text");

        builder.Property(h => h.ParametersHash)
            .IsRequired()
            .HasMaxLength(64);

        // Índices para el listado principal por puntaje/consumo (RNF-RND-02, RF-SCO-04) y serie histórica por consulta (RF-SCO-05).
        builder.HasIndex(h => new { h.ConnectionId, h.CalculatedAt });
        builder.HasIndex(h => new { h.ConnectionId, h.Score });
        builder.HasIndex(h => new { h.QueryId, h.CalculatedAt });
        builder.HasIndex(h => h.IngestionRunId);

        builder.HasOne<Connection>()
            .WithMany()
            .HasForeignKey(h => h.ConnectionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Query>()
            .WithMany()
            .HasForeignKey(h => h.QueryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<IngestionRun>()
            .WithMany()
            .HasForeignKey(h => h.IngestionRunId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
