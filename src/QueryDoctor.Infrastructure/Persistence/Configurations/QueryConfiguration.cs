using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueryDoctor.Domain.Core;

namespace QueryDoctor.Infrastructure.Persistence.Configurations;

public sealed class QueryConfiguration : IEntityTypeConfiguration<Query>
{
    public void Configure(EntityTypeBuilder<Query> builder)
    {
        builder.ToTable("query", "core");
        builder.HasKey(q => q.Id);

        builder.Property(q => q.QueryHash).HasMaxLength(64);
        builder.Property(q => q.OriginContext).HasMaxLength(512);

        builder.HasIndex(q => new { q.ConnectionId, q.QueryStoreQueryId }).IsUnique();

        builder.HasOne<Connection>()
            .WithMany()
            .HasForeignKey(q => q.ConnectionId)
            .OnDelete(DeleteBehavior.Cascade);

        // Al purgar un texto por retención, la consulta se conserva sin texto.
        builder.HasOne<QueryText>()
            .WithMany()
            .HasForeignKey(q => q.QueryTextId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
