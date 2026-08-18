using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Notification.Domain.Directory;

namespace TaxVision.Notification.Infrastructure.Persistence.Configurations;

public sealed class UserEmailDirectoryEntryConfiguration : IEntityTypeConfiguration<UserEmailDirectoryEntry>
{
    public void Configure(EntityTypeBuilder<UserEmailDirectoryEntry> builder)
    {
        builder.ToTable("UserEmailDirectoryEntries");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.TenantId).IsRequired();
        builder.Property(e => e.UserId).IsRequired();
        builder.Property(e => e.Email).HasMaxLength(320).IsRequired();
        builder.Property(e => e.IsActive).IsRequired();
        builder.Property(e => e.IsStale).IsRequired();
        builder.Property(e => e.UpdatedAtUtc).IsRequired();

        // Un usuario, una fila. El índice único es lo que hace segura la carrera entre el consumer
        // de UserRegistered y la recuperación pull: si las dos intentan insertar, la segunda choca.
        builder.HasIndex(e => new { e.TenantId, e.UserId }).IsUnique();
    }
}
