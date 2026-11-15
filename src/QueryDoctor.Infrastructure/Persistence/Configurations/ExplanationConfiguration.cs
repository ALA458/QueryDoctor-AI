using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueryDoctor.Domain.Ai;
using QueryDoctor.Domain.Core;

namespace QueryDoctor.Infrastructure.Persistence.Configurations;

/// <summary>
/// Tabla <c>ai.explanation</c> (S2-05, E4). Las afirmaciones van dentro del cuerpo cifrado; <c>claim</c> y
/// <c>claim_reference</c> de §7 quedan aplazadas. Sigue a su consulta (ON DELETE CASCADE, retención §7).
/// </summary>
public sealed class ExplanationConfiguration : IEntityTypeConfiguration<Explanation>
{
    public void Configure(EntityTypeBuilder<Explanation> builder)
    {
        builder.ToTable("explanation", "ai");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.State).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(e => e.Cause).HasConversion<string>().HasMaxLength(32);
        builder.Property(e => e.ValidationStatus).HasMaxLength(32);
        builder.Property(e => e.ContextHash).HasMaxLength(64);
        builder.Property(e => e.FindingCodesJson).HasColumnType("text").IsRequired();
        builder.Property(e => e.CatalogVersion).HasMaxLength(64);
        builder.Property(e => e.LlmProfile).HasMaxLength(64);
        builder.Property(e => e.Model).HasMaxLength(200);
        builder.Property(e => e.PromptVersion).HasMaxLength(64);
        builder.Property(e => e.ServedBy).HasMaxLength(200);
        builder.Property(e => e.DataPolicy).HasMaxLength(64);
        builder.Property(e => e.EncryptedBody).HasColumnType("text");

        // Última explicación de una consulta (GET …/latest) y solicitud en curso (409, E10).
        builder.HasIndex(e => new { e.QueryId, e.RequestedAt });

        // Caché por contexto (RF-IA-07).
        builder.HasIndex(e => e.ContextHash);

        builder.HasOne<Connection>()
            .WithMany()
            .HasForeignKey(e => e.ConnectionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Query>()
            .WithMany()
            .HasForeignKey(e => e.QueryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Explanation>()
            .WithMany()
            .HasForeignKey(e => e.ReusedFromId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
