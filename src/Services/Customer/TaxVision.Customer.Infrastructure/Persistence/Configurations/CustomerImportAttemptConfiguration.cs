using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Customer.Domain.Imports;

namespace TaxVision.Customer.Infrastructure.Persistence.Configurations;

public sealed class CustomerImportAttemptConfiguration : IEntityTypeConfiguration<CustomerImportAttempt>
{
    public void Configure(EntityTypeBuilder<CustomerImportAttempt> b)
    {
        b.ToTable("CustomerImportAttempts");
        b.HasKey(a => a.Id);
        b.Property(a => a.TenantId).IsRequired();
        b.Property(a => a.CreatedByUserId).IsRequired();
        b.Property(a => a.IdempotencyKey).HasMaxLength(80).IsRequired();
        b.Property(a => a.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(a => a.Strategy).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(a => a.SourceKind).HasConversion<string>().HasMaxLength(10).IsRequired();
        b.Property(a => a.SourceFileName).HasMaxLength(256).IsRequired();
        b.Property(a => a.TotalRows).IsRequired();
        b.Property(a => a.ProcessedRows).IsRequired();
        b.Property(a => a.SuccessCount).IsRequired();
        b.Property(a => a.UpdatedCount).IsRequired();
        b.Property(a => a.SkippedCount).IsRequired();
        b.Property(a => a.FailedCount).IsRequired();
        b.Property(a => a.CreatedAtUtc).IsRequired();
        b.Property(a => a.StartedAtUtc);
        b.Property(a => a.CompletedAtUtc);
        b.Property(a => a.CanceledAtUtc);
        b.Property(a => a.CanceledByUserId);
        b.Property(a => a.FailureReason).HasMaxLength(500);

        // Idempotency: una sola attempt por (Tenant, Key)
        b.HasIndex(a => new { a.TenantId, a.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName("IX_CustomerImportAttempts_Tenant_IdempotencyKey");

        // Para query de "import activo del tenant" y listado por tenant
        b.HasIndex(a => new
            {
                a.TenantId,
                a.Status,
                a.CreatedAtUtc,
            })
            .HasDatabaseName("IX_CustomerImportAttempts_Tenant_Status_Created");

        // RULE: 1 job activo por tenant a nivel BD. Indice filtrado unique sobre TenantId
        // donde Status NO es terminal. Evita race condition entre dos POST concurrentes
        // que ambos pasaron el check de CountActiveByTenantAsync.
        b.HasIndex(a => a.TenantId)
            .IsUnique()
            .HasFilter("[Status] IN ('Queued','Validating','Applying','Canceling')")
            .HasDatabaseName("UX_CustomerImportAttempts_Tenant_Active");

        // Para purga >90 dias
        b.HasIndex(a => a.CreatedAtUtc).HasDatabaseName("IX_CustomerImportAttempts_Created");

        b.HasMany(a => a.Rows).WithOne().HasForeignKey(r => r.CustomerImportAttemptId).OnDelete(DeleteBehavior.Cascade);

        b.Navigation(a => a.Rows).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
