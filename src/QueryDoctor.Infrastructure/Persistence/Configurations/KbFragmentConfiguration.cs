using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueryDoctor.Domain.Ai;
using QueryDoctor.Infrastructure.Persistence.Seeds;

namespace QueryDoctor.Infrastructure.Persistence.Configurations;

public sealed class KbFragmentConfiguration : IEntityTypeConfiguration<KbFragment>
{
    public void Configure(EntityTypeBuilder<KbFragment> builder)
    {
        builder.ToTable("kb_fragment", "ai");
        builder.HasKey(f => f.Id);

        builder.Property(f => f.RuleId).HasMaxLength(32).IsRequired();
        builder.Property(f => f.Title).HasMaxLength(200).IsRequired();
        builder.Property(f => f.Content).HasColumnType("text").IsRequired();
        builder.Property(f => f.SourceTitle).HasMaxLength(300).IsRequired();
        builder.Property(f => f.SourceUrl).HasMaxLength(1000).IsRequired();
        builder.Property(f => f.SourceCitation).HasMaxLength(500).IsRequired();
        builder.Property(f => f.CreatedAt).IsRequired();

        builder.HasIndex(f => f.RuleId);

        // Siembra de fragmentos curados iniciales (S1-17)
        builder.HasData(KnowledgeBaseSeedData.GetFragments());
    }
}
