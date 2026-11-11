using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueryDoctor.Domain.Core;

namespace QueryDoctor.Infrastructure.Persistence.Configurations;

public sealed class QueryTextConfiguration : IEntityTypeConfiguration<QueryText>
{
    public void Configure(EntityTypeBuilder<QueryText> builder)
    {
        builder.ToTable("query_text", "core");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.EncryptedSqlText).HasColumnType("text");

        builder.HasIndex(t => new { t.ConnectionId, t.QueryStoreTextId }).IsUnique();

        builder.HasOne<Connection>()
            .WithMany()
            .HasForeignKey(t => t.ConnectionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
