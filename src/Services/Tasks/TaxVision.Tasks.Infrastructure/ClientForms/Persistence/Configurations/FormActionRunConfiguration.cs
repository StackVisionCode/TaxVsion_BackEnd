using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.Infrastructure.ClientForms.Persistence.Configurations;

public sealed class FormActionRunConfiguration : IEntityTypeConfiguration<FormActionRun>
{
    public void Configure(EntityTypeBuilder<FormActionRun> builder)
    {
        builder.ToTable(
            "FormActionRuns",
            table =>
                table.HasCheckConstraint(
                    "CK_FormActionRuns_Command",
                    "([Status] = 5 AND [CommandId] IS NULL) OR ([Status] = 4) OR ([Status] IN (1, 2, 3) AND [CommandId] IS NOT NULL)"
                )
        );
        builder.HasKey(run => run.Id);
        builder.Property(run => run.Id).ValueGeneratedNever();
        builder.Property(run => run.TenantId).IsRequired();
        builder.Property(run => run.ActionKey).HasMaxLength(FormActionRun.ActionKeyMaxLength).IsRequired();
        builder.Property(run => run.ActionType).HasMaxLength(FormActionRun.ActionTypeMaxLength).IsRequired();
        builder.Property(run => run.Status).HasConversion<int>().IsRequired();
        builder.Property(run => run.IdempotencyKey).HasMaxLength(128).IsRequired();
        builder.Property(run => run.ResultRefKind).HasMaxLength(32);
        builder.Property(run => run.ErrorCode).HasMaxLength(FormActionRun.CodeMaxLength);
        builder.Property(run => run.ErrorDetail).HasMaxLength(FormActionRun.CodeMaxLength);
        builder.Property(run => run.Warnings).HasMaxLength(FormActionRun.CodeMaxLength);
        builder.Property(run => run.RowVersion).IsRowVersion();
        builder.Ignore(run => run.IsLatestAttempt);
        builder.Ignore(run => run.IsRetryable);

        // Dos reintentos simultáneos piden el mismo Attempt: el segundo choca acá y no emite nada.
        builder
            .HasIndex(run => new
            {
                run.FormRequestId,
                run.IdempotencyKey,
                run.Attempt,
            })
            .IsUnique()
            .HasDatabaseName("UX_FormActionRuns_Request_Key_Attempt");
        builder
            .HasIndex(run => new { run.TenantId, run.CommandId })
            .IsUnique()
            .HasFilter("[CommandId] IS NOT NULL")
            .HasDatabaseName("UX_FormActionRuns_Tenant_Command");
        // La alerta de runs colgados busca los Requested más viejos que el corte.
        builder
            .HasIndex(run => new { run.Status, run.RequestedAtUtc })
            .HasDatabaseName("IX_FormActionRuns_Status_RequestedAtUtc");
    }
}
