using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.CloudStorage.Domain.Quotas;

namespace TaxVision.CloudStorage.Infrastructure.Persistence.Configurations;

public sealed class UploadSizePolicyConfiguration : IEntityTypeConfiguration<UploadSizePolicy>
{
    public void Configure(EntityTypeBuilder<UploadSizePolicy> builder)
    {
        builder.ToTable("UploadSizePolicies");
        builder.HasKey(policy => policy.Id);
        builder.Property(policy => policy.Scope).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(policy => policy.FolderType).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(policy => policy.PlanCode).HasMaxLength(UploadSizePolicy.PlanCodeMaxLength);
        builder.Property(policy => policy.MaxFileSizeBytes).IsRequired();
        builder.Property(policy => policy.UpdatedByUserId).IsRequired();
        builder.Property(policy => policy.UpdatedAtUtc).IsRequired();
        builder.Property(policy => policy.RowVersion).IsRowVersion();

        // Una sola fila por (scope, carpeta, plan, tenant): el upsert del handler depende de este índice.
        // Sin el filtro "IS NOT NULL" que EF agrega por defecto: las filas Global/Plan llevan nulls y
        // también deben ser únicas (SQL Server trata NULL como un valor en índices sin filtro).
        builder
            .HasIndex(policy => new
            {
                policy.Scope,
                policy.FolderType,
                policy.PlanCode,
                policy.TenantId,
            })
            .IsUnique()
            .HasFilter(null)
            .HasDatabaseName("IX_UploadSizePolicies_Scope_FolderType_PlanCode_TenantId");
    }
}

public sealed class UploadSizePolicyAuditConfiguration : IEntityTypeConfiguration<UploadSizePolicyAudit>
{
    public void Configure(EntityTypeBuilder<UploadSizePolicyAudit> builder)
    {
        builder.ToTable("UploadSizePolicyAudits");
        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Scope).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(entry => entry.FolderType).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(entry => entry.PlanCode).HasMaxLength(UploadSizePolicy.PlanCodeMaxLength);
        builder.Property(entry => entry.ActorUserId).IsRequired();
        builder.Property(entry => entry.OccurredAtUtc).IsRequired();

        builder.HasIndex(entry => entry.OccurredAtUtc).HasDatabaseName("IX_UploadSizePolicyAudits_OccurredAt");
    }
}
