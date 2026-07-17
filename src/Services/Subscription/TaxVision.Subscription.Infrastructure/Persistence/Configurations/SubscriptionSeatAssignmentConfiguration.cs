using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Subscription.Domain.Seats;

namespace TaxVision.Subscription.Infrastructure.Persistence.Configurations;

public sealed class SubscriptionSeatAssignmentConfiguration : IEntityTypeConfiguration<SubscriptionSeatAssignment>
{
    public void Configure(EntityTypeBuilder<SubscriptionSeatAssignment> builder)
    {
        builder.ToTable("SubscriptionSeatAssignments");
        builder.HasKey(assignment => assignment.Id);

        // *** GUARDRAIL persistencia (§49) ***
        // Id se genera en la factory de dominio (Guid.NewGuid() via BaseEntity) y la
        // entidad cuelga de SubscriptionSeat._assignments (HasMany). Sin esto, EF marca la
        // entidad como Unchanged/Modified en lugar de Added -> UPDATE de fila inexistente
        // -> DbUpdateConcurrencyException.
        builder.Property(assignment => assignment.Id).ValueGeneratedNever();

        builder.Property(assignment => assignment.SeatId).IsRequired();
        builder.Property(assignment => assignment.TenantId).IsRequired();
        builder.Property(assignment => assignment.UserId).IsRequired();
        builder.Property(assignment => assignment.AssignedAtUtc).IsRequired();
        builder.Property(assignment => assignment.AssignedByUserId).IsRequired();
        builder.Property(assignment => assignment.ReleaseReason).HasMaxLength(200);

        builder.Ignore(assignment => assignment.IsActive);

        builder
            .HasIndex(assignment => assignment.SeatId)
            .HasFilter("[ReleasedAtUtc] IS NULL")
            .IsUnique()
            .HasDatabaseName("UX_SubscriptionSeatAssignments_SeatId_Active");

        builder
            .HasIndex(assignment => new { assignment.UserId, assignment.TenantId })
            .HasDatabaseName("IX_SubscriptionSeatAssignments_UserId_TenantId");
    }
}
