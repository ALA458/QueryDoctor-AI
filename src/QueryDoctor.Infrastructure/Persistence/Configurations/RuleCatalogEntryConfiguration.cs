using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueryDoctor.Application.Engine.Rules;
using QueryDoctor.Domain.Analysis;

namespace QueryDoctor.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración de la tabla <c>analysis.rule_catalog</c> con el sembrado determinista de las 11 reglas del catálogo v1.0.0 (RF-MOT-15, Tabla 4.11).
/// </summary>
public sealed class RuleCatalogEntryConfiguration : IEntityTypeConfiguration<RuleCatalogEntry>
{
    public void Configure(EntityTypeBuilder<RuleCatalogEntry> builder)
    {
        builder.ToTable("rule_catalog", "analysis");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.RuleId).IsRequired().HasMaxLength(16);
        builder.Property(r => r.CatalogVersion).IsRequired().HasMaxLength(32);
        builder.Property(r => r.RuleVersion).IsRequired().HasMaxLength(32);
        builder.Property(r => r.Name).IsRequired().HasMaxLength(128);
        builder.Property(r => r.Description).IsRequired().HasMaxLength(1024);
        builder.Property(r => r.DocumentationSource).IsRequired().HasMaxLength(512);
        builder.Property(r => r.RuleClass).HasConversion<string>().HasMaxLength(32);
        builder.Property(r => r.EvidenceClass).HasConversion<string>().HasMaxLength(32);
        builder.Property(r => r.BaseSeverity).HasConversion<string>().HasMaxLength(32);
        builder.Property(r => r.Priority).HasConversion<string>().HasMaxLength(32);
        builder.Property(r => r.DefaultThresholdsJson).IsRequired().HasColumnType("text");

        builder.HasIndex(r => new { r.RuleId, r.CatalogVersion, r.RuleVersion }).IsUnique();

        builder.HasData(VersionedRuleCatalog.CreateSeedEntries());
    }
}
