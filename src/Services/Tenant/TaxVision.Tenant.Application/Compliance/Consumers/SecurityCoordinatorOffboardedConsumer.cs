using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.TenantIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Tenant.Application.Compliance.Abstractions;
using TaxVision.Tenant.Domain.Compliance;
using Wolverine;

namespace TaxVision.Tenant.Application.Compliance.Consumers;

/// <summary>
/// Al retirar a un empleado que era el responsable de seguridad de su oficina (D-3): la designación queda
/// vacante (no pasa al sucesor: es legal) y se publica <see cref="OfficeSecurityCoordinatorVacatedIntegrationEvent"/>
/// para que Notification avise a los administradores. Idempotente: si ya no es el responsable, no hace nada.
/// Guarda antes de publicar; un conflicto con el job del WISP sube y Wolverine reintenta.
/// </summary>
public static class SecurityCoordinatorOffboardedConsumer
{
    public static async Task Handle(
        UserOffboardedIntegrationEvent evt,
        IOfficeComplianceRepository compliances,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<OfficeCompliance> logger,
        TimeProvider clock,
        CancellationToken ct
    )
    {
        using (
            correlation.Push(
                string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId
            )
        )
        {
            var compliance = await compliances.GetAsync(evt.TenantId, ct);
            var nowUtc = clock.GetUtcNow().UtcDateTime;
            if (
                compliance is null
                || !compliance.VacateSecurityCoordinatorOnOffboarding(evt.UserId, evt.OffboardedByUserId, nowUtc)
            )
                return;

            await unitOfWork.SaveChangesAsync(ct);
            await bus.PublishAsync(Vacated(evt, nowUtc, correlation.CorrelationId));
            logger.LogInformation(
                "Security coordinator {UserId} of tenant {TenantId} was offboarded; designation vacated by {ActorId}.",
                evt.UserId,
                evt.TenantId,
                evt.OffboardedByUserId
            );
        }
    }

    private static OfficeSecurityCoordinatorVacatedIntegrationEvent Vacated(
        UserOffboardedIntegrationEvent evt,
        DateTime vacatedAtUtc,
        string correlationId
    ) =>
        new()
        {
            TenantId = evt.TenantId,
            OfficeTenantId = evt.TenantId,
            FormerCoordinatorUserId = evt.UserId,
            Reason = OfficeSecurityCoordinatorVacancyReasons.Offboarded,
            VacatedByUserId = evt.OffboardedByUserId,
            VacatedAtUtc = vacatedAtUtc,
            CorrelationId = correlationId,
        };
}
