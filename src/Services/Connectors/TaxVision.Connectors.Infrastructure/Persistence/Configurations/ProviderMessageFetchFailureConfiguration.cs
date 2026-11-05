using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Connectors.Domain.Sync;

namespace TaxVision.Connectors.Infrastructure.Persistence.Configurations;

public sealed class ProviderMessageFetchFailureConfiguration : IEntityTypeConfiguration<ProviderMessageFetchFailure>
{
    public void Configure(EntityTypeBuilder<ProviderMessageFetchFailure> builder)
    {
        builder.ToTable("ProviderMessageFetchFailures");
        builder.HasKey(f => f.Id);
        builder.Property(f => f.AccountId).IsRequired();
        builder
            .Property(f => f.ProviderMessageId)
            .IsRequired()
            .HasMaxLength(ProviderMessageFetchFailure.ProviderMessageIdMaxLength);
        builder.Property(f => f.Attempts).IsRequired();
        builder.Property(f => f.FirstFailedAtUtc).IsRequired();
        builder.Property(f => f.LastFailedAtUtc).IsRequired();
        builder.Property(f => f.LastError).HasMaxLength(ProviderMessageFetchFailure.LastErrorMaxLength);
        builder.Property(f => f.Status).HasConversion<string>().HasMaxLength(16).IsRequired();

        // Dos pasadas que registran el mismo fallo a la vez: una gana y la otra reintenta.
        builder.HasIndex(f => new { f.AccountId, f.ProviderMessageId }).IsUnique();
    }
}
