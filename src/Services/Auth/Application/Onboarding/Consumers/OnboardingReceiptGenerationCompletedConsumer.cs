using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.DocumentsIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Onboarding.Abstractions;
using Wolverine;

namespace TaxVision.Auth.Application.Onboarding.Consumers;

/// <summary>
/// PayFlow (Fase 11) — cierra el ciclo receipt: Documents (Fase 10) terminó de generar el PDF y
/// publicó <see cref="DocumentGenerationCompletedIntegrationEvent"/>. Este consumer sólo le importan
/// las generaciones cuyo <c>OwnerType</c> es <c>"Onboarding"</c> (mismo patrón de filtro que
/// Billing's DocumentGenerationCompletedConsumer usa para <c>"Invoice"</c>) — cualquier otra se
/// ignora con un early-return, sin excepción. No hay binding nuevo que registrar: la cola
/// <c>auth-tenant-events</c> ya escucha todo el exchange <c>taxvision-events</c>.
/// </summary>
public static class OnboardingReceiptGenerationCompletedConsumer
{
    private const string OwnerTypeOnboarding = "Onboarding";

    public static async Task Handle(
        DocumentGenerationCompletedIntegrationEvent evt,
        ITenantOnboardingRepository onboardings,
        IOptions<OnboardingOptions> onboardingOptions,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<DocumentGenerationCompletedIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        if (!string.Equals(evt.OwnerType, OwnerTypeOnboarding, StringComparison.OrdinalIgnoreCase))
            return;

        var correlationId = string.IsNullOrWhiteSpace(evt.CorrelationId)
            ? evt.EventId.ToString("N")
            : evt.CorrelationId;

        using (correlation.Push(correlationId))
        {
            var onboarding = await onboardings.GetByIdAsync(evt.OwnerId, ct);
            if (onboarding is null)
            {
                logger.LogWarning(
                    "DocumentGenerationCompleted(Onboarding) for unknown onboarding {OnboardingId}; ignoring.",
                    evt.OwnerId
                );
                return;
            }

            var setResult = onboarding.SetReceiptFileId(evt.FileId);
            if (setResult.IsFailure)
            {
                logger.LogWarning(
                    "SetReceiptFileId failed for onboarding {OnboardingId}: {ErrorCode}",
                    evt.OwnerId,
                    setResult.Error.Code
                );
                return;
            }

            var downloadUrl =
                $"{onboardingOptions.Value.AuthPublicBaseUrl.TrimEnd('/')}/onboarding/receipts/{evt.FileId}/download";

            await bus.PublishAsync(
                new OnboardingReceiptReadyIntegrationEvent
                {
                    TenantId = PlatformTenant.Id,
                    OnboardingId = onboarding.Id,
                    ReceiptFileId = evt.FileId,
                    ReceiptDownloadUrl = downloadUrl,
                    CorrelationId = correlationId,
                }
            );

            await unitOfWork.SaveChangesAsync(ct);

            logger.LogInformation("Receipt {FileId} attached to onboarding {OnboardingId}.", evt.FileId, onboarding.Id);
        }
    }
}
