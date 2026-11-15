using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueryDoctor.Domain.Ai;

namespace QueryDoctor.Infrastructure.Persistence.Configurations;

/// <summary>
/// Tablas <c>ai.claim</c> y <c>ai.claim_reference</c> (§7, RF-IA-02, RF-DASH-05). Siguen a su explicación con borrado
/// en cascada; el texto de la afirmación va cifrado (RNF-SEG-14).
/// </summary>
public sealed class ExplanationClaimConfiguration : IEntityTypeConfiguration<ExplanationClaim>
{
    public void Configure(EntityTypeBuilder<ExplanationClaim> builder)
    {
        builder.ToTable("claim", "ai");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.ClaimType).HasMaxLength(ExplanationColumnLimits.ClaimCode).IsRequired();
        builder.Property(c => c.EncryptedText).HasColumnType("text").IsRequired();
        builder.Property(c => c.IndicationLabel).HasMaxLength(ExplanationColumnLimits.ClaimCode);

        builder.HasIndex(c => new { c.ExplanationId, c.ClaimIndex }).IsUnique();

        builder.HasOne<Explanation>()
            .WithMany()
            .HasForeignKey(c => c.ExplanationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(c => c.References)
            .WithOne()
            .HasForeignKey(r => r.ClaimId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Configuración de <c>ai.claim_reference</c>.</summary>
public sealed class ExplanationClaimReferenceConfiguration : IEntityTypeConfiguration<ExplanationClaimReference>
{
    public void Configure(EntityTypeBuilder<ExplanationClaimReference> builder)
    {
        builder.ToTable("claim_reference", "ai");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.FindingCode).HasMaxLength(ExplanationColumnLimits.ShortCode).IsRequired();
        builder.Property(r => r.NodeIdsJson).HasColumnType("text").IsRequired();

        builder.HasIndex(r => r.ClaimId);
        builder.HasIndex(r => r.FindingCode);
    }
}
