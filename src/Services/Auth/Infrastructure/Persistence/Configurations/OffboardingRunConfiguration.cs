using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Auth.Domain.Offboarding;

namespace TaxVision.Auth.Infrastructure.Persistence.Configurations;

public sealed class OffboardingRunConfiguration : IEntityTypeConfiguration<OffboardingRun>
{
    public void Configure(EntityTypeBuilder<OffboardingRun> builder)
    {
        builder.ToTable("OffboardingRuns");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.TenantId).IsRequired();
        builder.Property(r => r.UserId).IsRequired();
        builder.Property(r => r.StartedAtUtc).IsRequired();
        builder.Property(r => r.Reason).HasConversion<string>().HasMaxLength(32);
        builder.Property(r => r.ReasonNote).HasMaxLength(OffboardingDetails.ReasonNoteMaxLength);
        builder.Ignore(r => r.Status);
        builder.Ignore(r => r.ExpectedCount);
        builder.Ignore(r => r.CompletedCount);
        builder.Ignore(r => r.AllCompleted);
        builder.Ignore(r => r.CompletedAtUtc);
        builder.Ignore(r => r.PendingSteps);

        // Un retiro por usuario (es terminal); el barrido busca por fecha entre los no marcados.
        builder.HasIndex(r => new { r.TenantId, r.UserId }).IsUnique();
        builder.HasIndex(r => new { r.IncompleteAtUtc, r.StartedAtUtc });

        builder
            .HasMany(r => r.Steps)
            .WithOne()
            .HasForeignKey(s => s.RunId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(r => r.Steps).HasField("_steps").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class OffboardingRunStepConfiguration : IEntityTypeConfiguration<OffboardingRunStep>
{
    public void Configure(EntityTypeBuilder<OffboardingRunStep> builder)
    {
        builder.ToTable("OffboardingRunSteps");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Service).HasMaxLength(OffboardingRunStep.ServiceMaxLength).IsRequired();
        builder.Property(s => s.Step).HasMaxLength(OffboardingRunStep.StepMaxLength).IsRequired();
        builder.Ignore(s => s.IsCompleted);

        // Idempotencia por Service+Step dentro de la corrida.
        builder
            .HasIndex(s => new
            {
                s.RunId,
                s.Service,
                s.Step,
            })
            .IsUnique();
    }
}
