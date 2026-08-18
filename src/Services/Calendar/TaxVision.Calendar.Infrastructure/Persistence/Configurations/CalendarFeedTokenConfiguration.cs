using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Calendar.Domain.Feeds;

namespace TaxVision.Calendar.Infrastructure.Persistence.Configurations;

internal sealed class CalendarFeedTokenConfiguration : IEntityTypeConfiguration<CalendarFeedToken>
{
    public void Configure(EntityTypeBuilder<CalendarFeedToken> builder)
    {
        builder.ToTable("CalendarFeedTokens");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();

        builder.Property(t => t.TokenHash).HasColumnType("varbinary(32)").IsRequired();
        builder.Property(t => t.TokenLast4).HasMaxLength(4).IsRequired();

        // La busqueda del feed publico entra por acá y sin tenant: el token es lo que lo resuelve.
        builder.HasIndex(t => t.TokenHash).IsUnique();

        builder.HasIndex(t => new { t.TenantId, t.UserId });
    }
}
