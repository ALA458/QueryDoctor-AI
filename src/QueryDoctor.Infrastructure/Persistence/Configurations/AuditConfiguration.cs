using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueryDoctor.Domain.Audit;

namespace QueryDoctor.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>audit.trace_entry</c> (S2-06, RF-TRZ-01/02). Sin claves foráneas a propósito: la traza sobrevive a la purga de
/// consultas y explicaciones (S2-13, §7). La seq la asigna el escritor bajo el bloqueo de la cadena. Un trigger de la
/// migración rechaza UPDATE, DELETE y TRUNCATE.
/// </summary>
public sealed class TraceEntryConfiguration : IEntityTypeConfiguration<TraceEntry>
{
    public void Configure(EntityTypeBuilder<TraceEntry> builder)
    {
        builder.ToTable("trace_entry", "audit");
        builder.HasKey(e => e.Seq);
        builder.Property(e => e.Seq).ValueGeneratedNever();

        builder.Property(e => e.State).HasMaxLength(32).IsRequired();
        builder.Property(e => e.Cause).HasMaxLength(32);
        builder.Property(e => e.ValidationStatus).HasMaxLength(32);
        builder.Property(e => e.FindingCodesJson).HasColumnType("text").IsRequired();
        builder.Property(e => e.CatalogVersion).HasMaxLength(64);
        builder.Property(e => e.Model).HasMaxLength(200);
        builder.Property(e => e.PromptVersion).HasMaxLength(64);
        builder.Property(e => e.LlmProfile).HasMaxLength(64);
        builder.Property(e => e.ServedBy).HasMaxLength(200);
        builder.Property(e => e.DataPolicy).HasMaxLength(64);
        builder.Property(e => e.ContextHash).HasMaxLength(64);
        builder.Property(e => e.PrevHash).HasMaxLength(64).IsRequired();
        builder.Property(e => e.EntryHash).HasMaxLength(64).IsRequired();

        // RF-TRZ-03: filtros por consulta, por explicación y por fecha.
        builder.HasIndex(e => new { e.QueryId, e.RecordedAt });
        builder.HasIndex(e => e.ExplanationId);
        builder.HasIndex(e => e.RecordedAt);
    }
}

/// <summary><c>audit.audit_event</c> (S2-06, RF-TRZ-05). Mismas reglas de solo adición que la traza.</summary>
public sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.ToTable("audit_event", "audit");
        builder.HasKey(e => e.Seq);
        builder.Property(e => e.Seq).ValueGeneratedNever();

        builder.Property(e => e.EventType).HasConversion<string>().HasMaxLength(64).IsRequired();
        builder.Property(e => e.UserName).HasMaxLength(256);
        builder.Property(e => e.SubjectType).HasMaxLength(64);
        builder.Property(e => e.Outcome).HasMaxLength(32).IsRequired();
        builder.Property(e => e.PrevHash).HasMaxLength(64).IsRequired();
        builder.Property(e => e.EntryHash).HasMaxLength(64).IsRequired();

        builder.HasIndex(e => e.RecordedAt);
        builder.HasIndex(e => new { e.EventType, e.RecordedAt });
    }
}
