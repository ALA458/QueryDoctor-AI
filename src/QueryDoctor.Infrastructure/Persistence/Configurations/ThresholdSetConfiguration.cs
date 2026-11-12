using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueryDoctor.Application.Engine.Rules;
using QueryDoctor.Domain.Analysis;

namespace QueryDoctor.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración de la tabla <c>analysis.threshold_set</c> con el sembrado del conjunto de umbrales por defecto del Cuadro 4.5 del SRS (RF-MOT-14).
/// </summary>
public sealed class ThresholdSetConfiguration : IEntityTypeConfiguration<ThresholdSet>
{
    public static readonly Guid DefaultThresholdSetId = new("7a2d4c0e-9b63-4e1a-8d2f-3b8e5f6a1001");
    private static readonly DateTimeOffset DefaultSeededAt = new(2026, 10, 28, 0, 0, 0, TimeSpan.Zero);

    public void Configure(EntityTypeBuilder<ThresholdSet> builder)
    {
        builder.ToTable("threshold_set", "analysis");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.CatalogVersion).IsRequired().HasMaxLength(32);
        builder.Property(t => t.ConfigurationHash).IsRequired().HasMaxLength(64);
        builder.Property(t => t.ThresholdsJson).IsRequired().HasColumnType("text");

        builder.HasIndex(t => new { t.CatalogVersion, t.ConfigurationHash }).IsUnique();

        var defaults = RuleThresholds.Default;
        builder.HasData(
            new ThresholdSet
            {
                Id = DefaultThresholdSetId,
                CatalogVersion = VersionedRuleCatalog.CurrentCatalogVersion,
                ConfigurationHash = defaults.ComputeConfigurationHash(),
                ThresholdsJson = defaults.ToCanonicalJson(),
                IsDefault = true,
                CreatedAt = DefaultSeededAt,
            });
    }
}
