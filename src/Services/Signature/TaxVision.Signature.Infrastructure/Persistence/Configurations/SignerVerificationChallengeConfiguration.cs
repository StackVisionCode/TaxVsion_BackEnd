using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Infrastructure.Persistence.Configurations;

public sealed class SignerVerificationChallengeConfiguration : IEntityTypeConfiguration<SignerVerificationChallenge>
{
    public void Configure(EntityTypeBuilder<SignerVerificationChallenge> builder)
    {
        builder.ToTable("SignerVerificationChallenges");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.SignerId).IsRequired();
        builder.Property(c => c.Method).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(c => c.AnswerHash).HasMaxLength(SignerVerificationChallenge.MaxAnswerHashLength).IsRequired();
        builder.Property(c => c.IssuedAtUtc).IsRequired();
        builder.Property(c => c.ExpiresAtUtc).IsRequired();
        builder.Property(c => c.ConsumedAtUtc);

        builder.HasIndex(c => new
        {
            c.SignerId,
            c.Method,
            c.ExpiresAtUtc,
        });
    }
}
