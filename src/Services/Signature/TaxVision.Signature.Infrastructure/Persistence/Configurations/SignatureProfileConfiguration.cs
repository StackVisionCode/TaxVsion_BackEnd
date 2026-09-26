using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Signature.Domain.Profiles;

namespace TaxVision.Signature.Infrastructure.Persistence.Configurations;

public sealed class SignatureProfileConfiguration : IEntityTypeConfiguration<SignatureProfile>
{
    public void Configure(EntityTypeBuilder<SignatureProfile> builder)
    {
        builder.ToTable("SignatureProfiles");
        builder.HasKey(profile => profile.Id);

        builder.Property(profile => profile.Label).HasMaxLength(SignatureProfile.MaxLabelLength).IsRequired();
        builder.Property(profile => profile.FileId).IsRequired();
        builder.Property(profile => profile.IsDefault).IsRequired();
        builder.Property(profile => profile.IsArchived).IsRequired();

        // Ámbito de consulta: firmas de un tenant + owner (usuario u oficina=null).
        builder.HasIndex(profile => new { profile.TenantId, profile.OwnerUserId });
    }
}
