using Microsoft.EntityFrameworkCore;

namespace BuildingBlocks.CustomerVisibility;

public static class CustomerVisibilityModelBuilderExtensions
{
    /// <summary>Mapea la proyección compartida a la tabla <c>CustomerAssignmentProjections</c> en el schema
    /// del servicio. Se llama en OnModelCreating. Índices: único (Tenant,Customer,User) + JOIN (Tenant,Customer).</summary>
    public static ModelBuilder ApplyCustomerAssignmentProjection(this ModelBuilder modelBuilder, string? schema = null)
    {
        modelBuilder.Entity<CustomerAssignmentProjection>(b =>
        {
            b.ToTable("CustomerAssignmentProjections", schema);
            b.HasKey(a => a.Id);
            b.Property(a => a.TenantId).IsRequired();
            b.Property(a => a.CustomerId).IsRequired();
            b.Property(a => a.UserId).IsRequired();
            b.Property(a => a.Version).IsRequired();
            b.HasIndex(a => new
                {
                    a.TenantId,
                    a.CustomerId,
                    a.UserId,
                })
                .IsUnique();
            b.HasIndex(a => new { a.TenantId, a.CustomerId });
        });
        return modelBuilder;
    }
}
