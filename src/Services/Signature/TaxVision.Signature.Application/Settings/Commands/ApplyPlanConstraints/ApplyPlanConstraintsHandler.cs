using BuildingBlocks.Common;
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
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var settings = await repository.GetByTenantIdAsync(cmd.TenantId, ct);
        if (settings is null)
            return Result.Failure(new Error("Signature.Settings.NotFound", "No settings found for this tenant."));

        var constraintsResult = SignaturePlanConstraints.Create(
            cmd.MaxDocumentsPerRequest,
            cmd.MaxSignersPerRequest,
            cmd.MaxAllowedPdfBytes,
            cmd.MaxAllowedPages,
            cmd.MinRetentionYears,
            cmd.PurgeAllowed,
            cmd.AllowedChannels,
            cmd.MaxTokenExpirationHours
        );

        if (constraintsResult.IsFailure)
            return Result.Failure(constraintsResult.Error);

        // Lo que eligió la oficina no se reescribe: se acota a los techos nuevos al leerlo.
        settings.ApplyPlanConstraints(constraintsResult.Value);

        await unitOfWork.SaveChangesAsync(ct);

        await bus.PublishAsync(
            new SignaturePlanConstraintsUpdatedIntegrationEvent
            {
                TenantId = cmd.TenantId,
                CorrelationId = correlation.CorrelationId,
                ChangedByUserId = cmd.ChangedByUserId,
                UpdatedAtUtc = settings.UpdatedAtUtc,
            }
        );

        return Result.Success();
    }
}
