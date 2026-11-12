using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueryDoctor.Domain.Core;

namespace QueryDoctor.Infrastructure.Persistence.Configurations;

public sealed class PlanConfiguration : IEntityTypeConfiguration<Plan>
{
    public void Configure(EntityTypeBuilder<Plan> builder)
    {
        builder.ToTable("plan", "core");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.QueryPlanHash).HasMaxLength(64);
        builder.Property(p => p.EncryptedPlanXml).HasColumnType("text");
        builder.Property(p => p.EngineVersion).HasMaxLength(64);
        builder.Property(p => p.AnalysisStatus).HasConversion<string>().HasMaxLength(32);
        builder.Property(p => p.NotAnalyzableCause).HasMaxLength(512);

        builder.HasIndex(p => new { p.ConnectionId, p.QueryStorePlanId }).IsUnique();

        builder.HasOne<Connection>()
            .WithMany()
            .HasForeignKey(p => p.ConnectionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Query>()
            .WithMany()
            .HasForeignKey(p => p.QueryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
