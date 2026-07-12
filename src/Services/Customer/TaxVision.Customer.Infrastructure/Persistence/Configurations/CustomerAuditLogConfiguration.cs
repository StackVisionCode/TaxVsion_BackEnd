using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Customer.Domain.Audit;

namespace TaxVision.Customer.Infrastructure.Persistence.Configurations;

public sealed class CustomerAuditLogConfiguration : IEntityTypeConfiguration<CustomerAuditLog>
{
    public void Configure(EntityTypeBuilder<CustomerAuditLog> b)
    {
        b.ToTable("CustomerAuditLogs");
        b.HasKey(l => l.Id);
        b.Property(l => l.TenantId).IsRequired();
        b.Property(l => l.CustomerId).IsRequired();
        b.Property(l => l.ActorUserId).IsRequired();
        b.Property(l => l.Action).HasMaxLength(128).IsRequired();
        b.Property(l => l.Outcome).HasMaxLength(64).IsRequired();
        b.Property(l => l.IpAddress).HasMaxLength(64);
        b.Property(l => l.UserAgent).HasMaxLength(512);
        b.Property(l => l.CorrelationId).HasMaxLength(64).IsRequired();
        b.Property(l => l.Details).HasMaxLength(2000);
        b.Property(l => l.OccurredAtUtc).IsRequired();

        b.HasIndex(l => new
        {
            l.TenantId,
            l.CustomerId,
            l.OccurredAtUtc,
        });
        b.HasIndex(l => new
        {
            l.TenantId,
            l.ActorUserId,
            l.OccurredAtUtc,
        });
    }
}
