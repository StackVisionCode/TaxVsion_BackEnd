using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;

namespace TaxVision.Signature.Infrastructure.Persistence.Configurations;

public sealed class SignedFieldValueConfiguration : IEntityTypeConfiguration<SignedFieldValue>
{
    public void Configure(EntityTypeBuilder<SignedFieldValue> builder)
    {
        builder.ToTable("SignedFieldValues");
        builder.HasKey(value => value.Id);
        builder.Property(value => value.Id).ValueGeneratedNever();

        builder.Property(value => value.SignerId).IsRequired();
        builder.Property(value => value.FieldId).IsRequired();
        builder.Property(value => value.Value).HasMaxLength(SignatureFieldValue.MaxLength).IsRequired();
        builder.Property(value => value.CapturedAtUtc).IsRequired();

        builder.HasIndex(value => value.SignerId);
    }
}
