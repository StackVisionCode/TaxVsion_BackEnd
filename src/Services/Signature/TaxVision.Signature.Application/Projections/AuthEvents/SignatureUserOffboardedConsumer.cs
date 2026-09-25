using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Profiles;

namespace TaxVision.Signature.Application.Projections.AuthEvents;

/// <summary>
/// Al RETIRAR (offboard) a un empleado: archiva sus perfiles de firma PERSONALES (OwnerUserId == el
/// que se va) para que su firma deje de ser un activo seleccionable. Los de OFICINA (OwnerUserId null)
/// no se tocan. Las solicitudes en curso conservan su estampado — el FileId quedó congelado en cada
/// una y sobrevive al archivado. NADA de la cadena HMAC / identidad 8879 firmada / sellados se toca.
/// Idempotente: los ya archivados no reentran (includeArchived: false).
/// </summary>
public static class SignatureUserOffboardedConsumer
{
    public static async Task Handle(
        UserOffboardedIntegrationEvent evt,
        ISignatureProfileRepository profiles,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<SignatureProfile> logger,
        CancellationToken ct
    )
    {
        var correlationId = string.IsNullOrWhiteSpace(evt.CorrelationId)
            ? evt.EventId.ToString("N")
            : evt.CorrelationId;
        using (correlation.Push(correlationId))
        {
            var personalProfiles = await profiles.ListByOwnerAsync(
                evt.TenantId,
                evt.UserId,
                includeArchived: false,
                ct
            );
            if (personalProfiles.Count == 0)
                return;

            foreach (var profile in personalProfiles)
                profile.Archive();

            await unitOfWork.SaveChangesAsync(ct);

            logger.LogInformation(
                "Offboarded user {UserId}: archived {Count} personal signature profile(s) in tenant {TenantId}.",
                evt.UserId,
                personalProfiles.Count,
                evt.TenantId
            );
        }
    }
}
