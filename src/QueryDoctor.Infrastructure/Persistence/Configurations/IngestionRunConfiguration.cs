using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueryDoctor.Domain.Core;

namespace QueryDoctor.Infrastructure.Persistence.Configurations;

public sealed class IngestionRunConfiguration : IEntityTypeConfiguration<IngestionRun>
{
    public void Configure(EntityTypeBuilder<IngestionRun> builder)
    {
        builder.ToTable("ingestion_run", "core");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.Trigger).HasConversion<string>().HasMaxLength(32);
        builder.Property(i => i.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(i => i.ErrorCause).HasMaxLength(1024);

        builder.HasIndex(i => new { i.ConnectionId, i.StartedAt });

        builder.HasOne<Connection>()
            .WithMany()
            .HasForeignKey(i => i.ConnectionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
