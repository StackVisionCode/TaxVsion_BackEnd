using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Customer.Domain.Assignments;

namespace TaxVision.Customer.Infrastructure.Persistence.Configurations;

public sealed class CustomerAssignmentConfiguration : IEntityTypeConfiguration<CustomerAssignment>
{
    public void Configure(EntityTypeBuilder<CustomerAssignment> b)
    {
        b.ToTable("CustomerAssignments");
        b.HasKey(a => a.Id);
        b.Property(a => a.TenantId).IsRequired();
        b.Property(a => a.CustomerId).IsRequired();
        b.Property(a => a.UserId).IsRequired();
        b.Property(a => a.IsPrimary).IsRequired();
        b.Property(a => a.AssignedByUserId).IsRequired();
        b.Property(a => a.AssignedAtUtc).IsRequired();

        // Un usuario asignado una sola vez por cliente.
        b.HasIndex(a => new
            {
                a.TenantId,
                a.CustomerId,
                a.UserId,
            })
            .IsUnique();
        // Visibilidad: "clientes asignados a este usuario" (filtro de lectura y offboard).
        b.HasIndex(a => new { a.TenantId, a.UserId });
    }
}
