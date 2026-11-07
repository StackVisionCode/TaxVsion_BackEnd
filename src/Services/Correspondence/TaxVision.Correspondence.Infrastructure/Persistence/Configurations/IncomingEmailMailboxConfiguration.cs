using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Correspondence.Domain.Inbox;

namespace TaxVision.Correspondence.Infrastructure.Persistence.Configurations;

internal sealed class IncomingEmailMailboxConfiguration : IEntityTypeConfiguration<IncomingEmailMailbox>
{
    public void Configure(EntityTypeBuilder<IncomingEmailMailbox> builder)
    {
        builder.ToTable("IncomingEmailMailboxes");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.IncomingEmailId).IsRequired();
        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.AccountId).IsRequired();
        builder.Property(x => x.ProviderMessageId).IsRequired().HasMaxLength(IncomingEmail.ProviderMessageIdMaxLength);

        // Una copia por buzón: dos ingestas concurrentes de la misma copia chocan aquí.
        builder
            .HasIndex(x => new { x.IncomingEmailId, x.AccountId })
            .IsUnique()
            .HasDatabaseName("IX_IncomingEmailMailboxes_IncomingEmailId_AccountId_Unique");

        // Gate de buzón: ¿este correo tiene copia en alguno de los buzones visibles?
        builder
            .HasIndex(x => new { x.TenantId, x.AccountId })
            .IncludeProperties(x => x.IncomingEmailId)
            .HasDatabaseName("IX_IncomingEmailMailboxes_TenantId_AccountId");
    }
}
