using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Settings;
using Wolverine;

namespace TaxVision.Signature.Application.Settings.Commands.ApplyPlanConstraints;

public static class ApplyPlanConstraintsHandler
{
    public static async Task<Result> Handle(
        ApplyPlanConstraintsCommand cmd,
        ITenantSignatureSettingsRepository repository,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var settings = await repository.GetByTenantIdAsync(cmd.TenantId, ct);
        if (settings is null)
            return Result.Failure(new Error("Signature.Settings.NotFound", "No settings found for this tenant."));

        var constraintsResult = SignaturePlanConstraints.Create(
            cmd.MaxAllowedPdfBytes,
            cmd.MaxAllowedImageBytes,
            cmd.MaxAllowedPages,
            cmd.MinRetentionYears,
            cmd.PurgeAllowed,
            cmd.AllowedChannels,
            cmd.MaxTokenExpirationHours
        );

        if (constraintsResult.IsFailure)
            return Result.Failure(constraintsResult.Error);

        // Aplica las nuevas restricciones y auto-corrige la configuración del tenant
        // para que no exceda los nuevos techos.
        var applyResult = settings.ApplyPlanConstraints(constraintsResult.Value);
        if (applyResult.IsFailure)
            return applyResult;

        await unitOfWork.SaveChangesAsync(ct);

        await bus.PublishAsync(
            new SignaturePlanConstraintsUpdatedIntegrationEvent
            {
                TenantId = cmd.TenantId,
                ChangedByUserId = cmd.ChangedByUserId,
                UpdatedAtUtc = settings.UpdatedAtUtc,
            }
        );

        return Result.Success();
    }
}
