using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Billing.Domain.Invoices;

namespace TaxVision.Billing.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapeo EF de la factura. Snapshots/descuento/líneas como columnas JSON (owned types con ToJson);
/// cada Money va como "cents|CUR" vía MoneyToStringConverter (escalar, sin owned-type). RowVersion =
/// concurrencia optimista.
/// </summary>
public sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> b)
    {
        b.ToTable("Invoices");
        b.HasKey(i => i.Id);

        b.Property(i => i.TenantId).IsRequired();
        b.Property(i => i.InvoiceNumber).HasMaxLength(64);
        b.Property(i => i.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        b.Property(i => i.Currency).HasMaxLength(3).IsRequired();
        b.Property(i => i.PoNumber).HasMaxLength(128);
        b.Property(i => i.Summary).HasMaxLength(512);
        b.Property(i => i.Notes).HasMaxLength(4000);
        b.Property(i => i.PaymentMethod).HasConversion<string>().HasMaxLength(32);
        b.Property(i => i.ReceiptNumber).HasMaxLength(96);
        b.Property(i => i.ReceiptHash).HasMaxLength(64);
        b.Property(i => i.RowVersion).IsRowVersion();

        // Onboarding pago-primero: factura pre-tenant keyed por OnboardingId (re-hospedada al activar).
        b.Property(i => i.OnboardingId);
        b.Property(i => i.PlanId);
        b.Property(i => i.PaymentId);
        b.Property(i => i.SettlementType).HasConversion<string>().HasMaxLength(32);

        b.HasIndex(i => new { i.TenantId, i.InvoiceNumber }).IsUnique().HasFilter("[InvoiceNumber] IS NOT NULL");
        b.HasIndex(i => new { i.TenantId, i.Status });
        // Una factura por onboarding (idempotencia del alta pre-tenant).
        b.HasIndex(i => i.OnboardingId).IsUnique().HasFilter("[OnboardingId] IS NOT NULL");

        // Totales Money como escalar "cents|CUR".
        var money = new MoneyToStringConverter();
        b.Property(i => i.Subtotal).HasConversion(money).HasMaxLength(32).IsRequired();
        b.Property(i => i.TaxTotal).HasConversion(money).HasMaxLength(32).IsRequired();
        b.Property(i => i.DiscountTotal).HasConversion(money).HasMaxLength(32).IsRequired();
        b.Property(i => i.Total).HasConversion(money).HasMaxLength(32).IsRequired();
        b.Property(i => i.AmountPaid).HasConversion(money).HasMaxLength(32).IsRequired();
        b.Property(i => i.AmountDue).HasConversion(money).HasMaxLength(32).IsRequired();

        // Snapshots (records) como JSON string — STJ los materializa por su ctor posicional.
        b.Property(i => i.Customer)
            .HasConversion(new JsonValueConverter<Domain.ValueObjects.CustomerSnapshot>())
            .HasColumnType("nvarchar(max)")
            .IsRequired();
        b.Property(i => i.Issuer)
            .HasConversion(new JsonValueConverter<Domain.ValueObjects.IssuerSnapshot>())
            .HasColumnType("nvarchar(max)");

        // Descuento a nivel factura: Fase posterior (no se usa en Fase 1).
        b.Ignore(i => i.Discount);

        // Líneas en tabla propia; cada Money como "cents|CUR".
        b.OwnsMany(
            i => i.Lines,
            lb =>
            {
                lb.ToTable("InvoiceLineItems");
                lb.WithOwner().HasForeignKey(l => l.InvoiceId);
                lb.HasKey(l => l.Id);
                lb.Property(l => l.Description).HasMaxLength(1000).IsRequired();
                lb.Property(l => l.UnitAmount).HasConversion(new MoneyToStringConverter()).HasMaxLength(32);
                lb.Property(l => l.TaxAmount).HasConversion(new MoneyToStringConverter()).HasMaxLength(32);
                lb.Property(l => l.LineTotal).HasConversion(new MoneyToStringConverter()).HasMaxLength(32);
            }
        );

        // Líneas de ajuste (descuentos de onboarding) en tabla propia; magnitud del descuento como Money.
        b.OwnsMany(
            i => i.Adjustments,
            ab =>
            {
                ab.ToTable("InvoiceAdjustmentLines");
                ab.WithOwner().HasForeignKey(a => a.InvoiceId);
                ab.HasKey(a => a.Id);
                ab.Property(a => a.Id).ValueGeneratedNever();
                ab.Property(a => a.Type).HasConversion<string>().HasMaxLength(32).IsRequired();
                ab.Property(a => a.Code).HasMaxLength(64);
                ab.Property(a => a.GrowthReservationId);
                ab.Property(a => a.Amount).HasConversion(new MoneyToStringConverter()).HasMaxLength(32).IsRequired();
            }
        );

        // Enlaces de pago (Fase 2A): entidad NORMAL (no owned) en tabla propia — la Fase 3 la busca por
        // ExternalPayableId, transiciona estados e indexa. La colección se llena por campo (_paymentLinks).
        b.HasMany(i => i.PaymentLinks).WithOne().HasForeignKey(l => l.InvoiceId).OnDelete(DeleteBehavior.Cascade);
        b.Metadata.FindNavigation(nameof(Invoice.PaymentLinks))!.SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
