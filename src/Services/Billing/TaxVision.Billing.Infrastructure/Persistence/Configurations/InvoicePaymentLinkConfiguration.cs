using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Billing.Domain.Invoices;

namespace TaxVision.Billing.Infrastructure.Persistence.Configurations;

/// <summary>Mapeo del enlace de cobro como entidad normal (tabla <c>InvoicePaymentLinks</c>).</summary>
public sealed class InvoicePaymentLinkConfiguration : IEntityTypeConfiguration<InvoicePaymentLink>
{
    public void Configure(EntityTypeBuilder<InvoicePaymentLink> b)
    {
        b.ToTable("InvoicePaymentLinks");
        b.HasKey(l => l.Id);
        // PK Guid asignado por el dominio (BaseEntity), no store-generated. Sin esto EF lo trata como
        // ValueGeneratedOnAdd y, al agregarse por navegación con un Id no-default, asume que "ya existe"
        // → emite UPDATE (0 filas) en vez de INSERT. ValueGeneratedNever fuerza el estado Added correcto.
        b.Property(l => l.Id).ValueGeneratedNever();

        b.Property(l => l.InvoiceId).IsRequired();
        b.Property(l => l.ExternalPayableId).IsRequired();
        b.Property(l => l.CheckoutUrl).HasMaxLength(2048).IsRequired();
        b.Property(l => l.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        b.Property(l => l.CreatedAtUtc).IsRequired();
        b.Property(l => l.ExpiresAtUtc);
        b.Property(l => l.RevokedAtUtc);

        b.HasIndex(l => l.InvoiceId);
        // Correlación Fase 3 por el payable de PaymentClient.
        b.HasIndex(l => l.ExternalPayableId);
    }
}
