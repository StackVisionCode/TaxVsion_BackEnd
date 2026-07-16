using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.PaymentApp.Domain.ProviderCustomers;

namespace TaxVision.PaymentApp.Infrastructure.Persistence.Configurations;

public sealed class TenantSavedPaymentMethodConfiguration : IEntityTypeConfiguration<TenantSavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<TenantSavedPaymentMethod> builder)
    {
        builder.ToTable("TenantSavedPaymentMethods");
        builder.HasKey(method => method.Id);

        // *** GUARDRAIL persistencia (§49) ***
        // Id se genera en la factory de dominio y la entidad cuelga de
        // TenantProviderCustomer._savedMethods (HasMany) -> requiere ValueGeneratedNever().
        builder.Property(method => method.Id).ValueGeneratedNever();

        builder.Property(method => method.TenantProviderCustomerId).IsRequired();
        builder.Property(method => method.TenantId).IsRequired();
        builder.Property(method => method.MethodReference).HasMaxLength(200).IsRequired();
        builder.Property(method => method.Brand).HasMaxLength(30).IsRequired();
        builder.Property(method => method.Last4).HasMaxLength(4).IsRequired();
        builder.Property(method => method.ExpMonth).IsRequired();
        builder.Property(method => method.ExpYear).IsRequired();
        builder.Property(method => method.IsDefault).IsRequired();
        builder.Property(method => method.IsDetached).IsRequired();

        // Filtrado a activos: un método detached puede volver a adjuntarse (misma tarjeta,
        // nuevo ciclo de vida) sin chocar con el registro histórico.
        builder.HasIndex(method => new { method.TenantProviderCustomerId, method.MethodReference })
            .IsUnique()
            .HasFilter("[IsDetached] = 0")
            .HasDatabaseName("UX_TenantSavedPaymentMethods_Customer_MethodReference_Active");

        builder.HasIndex(method => new { method.ExpYear, method.ExpMonth })
            .HasFilter("[IsDetached] = 0")
            .HasDatabaseName("IX_TenantSavedPaymentMethods_Expiration");
    }
}
