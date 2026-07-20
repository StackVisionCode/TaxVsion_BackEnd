using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Growth.Infrastructure.Persistence.Idempotency;

namespace TaxVision.Growth.Infrastructure.Persistence.Configurations;

public sealed class ProcessedBusinessMessageConfiguration : IEntityTypeConfiguration<ProcessedBusinessMessage>
{
    public void Configure(EntityTypeBuilder<ProcessedBusinessMessage> builder)
    {
        builder.ToTable("ProcessedBusinessMessages", GrowthSchemas.Integration);
        builder.HasKey(message => message.Id);

        builder.Property(message => message.TenantId).IsRequired();
        builder.Property(message => message.Operation).HasMaxLength(100).IsRequired();
        builder.Property(message => message.ScopeId).IsRequired();
        builder.Property(message => message.IdempotencyKey).HasMaxLength(200).IsRequired();
        builder
            .Property(message => message.RequestFingerprint)
            .HasColumnType("char(64)")
            .IsFixedLength()
            .IsRequired();
        builder.Property(message => message.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(message => message.ResponseContentType).HasMaxLength(100);
        builder.Property(message => message.ResponseJson).HasColumnType("nvarchar(max)");
        builder.Property(message => message.FailureCode).HasMaxLength(100);
        builder.Property(message => message.CreatedAtUtc).HasColumnType("datetime2(7)").IsRequired();
        builder.Property(message => message.CompletedAtUtc).HasColumnType("datetime2(7)");
        builder.Property(message => message.ExpiresAtUtc).HasColumnType("datetime2(7)").IsRequired();
        builder.Property(message => message.RowVersion).IsRowVersion();

        builder
            .HasIndex(message => new
            {
                message.TenantId,
                message.Operation,
                message.ScopeId,
                message.IdempotencyKey,
            })
            .IsUnique()
            .HasDatabaseName("UX_ProcessedBusinessMessages_Tenant_Operation_Scope_Key");

        builder
            .HasIndex(message => new { message.Status, message.ExpiresAtUtc })
            .HasDatabaseName("IX_ProcessedBusinessMessages_Status_ExpiresAtUtc");
    }
}
