using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueryDoctor.Domain.Ai;
using QueryDoctor.Infrastructure.Persistence.Seeds;

namespace QueryDoctor.Infrastructure.Persistence.Configurations;

public sealed class KbCausalRelationConfiguration : IEntityTypeConfiguration<KbCausalRelation>
{
    public void Configure(EntityTypeBuilder<KbCausalRelation> builder)
    {
        builder.ToTable("kb_causal_relation", "ai");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.RuleId).HasMaxLength(32).IsRequired();
        builder.Property(r => r.FragmentId).IsRequired();
        builder.Property(r => r.RelationType).HasConversion<string>().HasMaxLength(32).IsRequired();

        builder.HasOne(r => r.Fragment)
            .WithMany(f => f.CausalRelations)
            .HasForeignKey(r => r.FragmentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(r => new { r.RuleId, r.FragmentId, r.RelationType }).IsUnique();
        builder.HasIndex(r => r.RuleId);
        builder.HasIndex(r => r.FragmentId);

        // Siembra de relaciones causa-efecto curadas iniciales (S1-17)
        builder.HasData(KnowledgeBaseSeedData.GetCausalRelations());
    }
}
