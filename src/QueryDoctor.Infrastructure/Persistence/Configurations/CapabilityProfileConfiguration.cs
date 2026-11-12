using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueryDoctor.Domain.Core;

namespace QueryDoctor.Infrastructure.Persistence.Configurations;

public sealed class CapabilityProfileConfiguration : IEntityTypeConfiguration<CapabilityProfile>
{
    public void Configure(EntityTypeBuilder<CapabilityProfile> builder)
    {
        builder.ToTable("capability_profile", "core");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.EngineVersion).HasMaxLength(64).IsRequired();
        builder.Property(p => p.Edition).HasMaxLength(128);
        builder.Property(p => p.SupportLevel).HasConversion<string>().HasMaxLength(32);
        builder.Property(p => p.QueryStoreActualState).HasMaxLength(64);
        builder.Property(p => p.QueryStoreDesiredState).HasMaxLength(64);
        builder.Property(p => p.QueryStoreReadOnlyReason).HasMaxLength(128);
        builder.Property(p => p.CaptureMode).HasMaxLength(64);
        builder.Property(p => p.OmittedFeaturesJson).HasColumnType("text");

        // Un perfil vigente por conexión.
        builder.HasIndex(p => p.ConnectionId).IsUnique();

        builder.HasOne<Connection>()
            .WithMany()
            .HasForeignKey(p => p.ConnectionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
