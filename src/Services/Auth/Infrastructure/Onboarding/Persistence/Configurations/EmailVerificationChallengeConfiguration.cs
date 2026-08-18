using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Auth.Domain.Onboarding.EmailVerification;

namespace TaxVision.Auth.Infrastructure.Onboarding.Persistence.Configurations;

public sealed class EmailVerificationChallengeConfiguration : IEntityTypeConfiguration<EmailVerificationChallenge>
{
    public void Configure(EntityTypeBuilder<EmailVerificationChallenge> builder)
    {
        builder.ToTable("EmailVerificationChallenges");
        builder.HasKey(challenge => challenge.Id);

        builder.Property(challenge => challenge.Email).HasMaxLength(256).IsRequired();
        builder.Property(challenge => challenge.OtpHash).HasMaxLength(64).IsRequired();
        builder.Property(challenge => challenge.ExpiresAtUtc).IsRequired();
        builder.Property(challenge => challenge.Attempts).IsRequired();
        builder.Property(challenge => challenge.ResendCount).IsRequired();
        builder.Property(challenge => challenge.CreatedAtUtc).IsRequired();

        builder.HasIndex(challenge => new { challenge.Email, challenge.CreatedAtUtc });
    }
}
