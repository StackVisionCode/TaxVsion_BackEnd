using BuildingBlocks.Common;
using BuildingBlocks.Messaging.TenantIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Settings;

namespace TaxVision.Signature.Application.Settings.IntegrationEvents;

/// <summary>
/// La oficina cambió su retención en Cumplimiento (dueño: Tenant): Signature la usa como años de retención,
/// nunca por debajo del mínimo del plan. Ordenado por <c>UpdatedAtUtc</c> e idempotente. Si la fila de ajustes
/// aún no existe (TenantCreated no llegó), se crea con los defaults; TenantCreated luego la encuentra y no hace nada.
/// La purga sigue siendo opt-in de Signature.
/// </summary>
public static class TenantRecordRetentionChangedConsumer
{
    public static async Task Handle(
        TenantRecordRetentionChangedIntegrationEvent evt,
        ITenantSignatureSettingsRepository repository,
        IAuditSecretFactory auditSecretFactory,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<TenantSignatureSettings> logger,
        CancellationToken ct
    )
    {
        var correlationId = string.IsNullOrWhiteSpace(evt.CorrelationId)
            ? evt.EventId.ToString("N")
            : evt.CorrelationId;
        using (correlation.Push(correlationId))
        {
            var settings = await repository.GetByTenantIdAsync(evt.OfficeTenantId, ct);
            var isNew = settings is null;
            settings ??= CreateDefaultSettings(evt.OfficeTenantId, auditSecretFactory);

            var applied = settings.ApplyOfficeRetention(evt.RetentionYears, evt.UpdatedAtUtc);
            if (applied.IsFailure)
            {
                // Dato fuera de rango: reintentar no lo arregla.
                logger.LogWarning(
                    "Office retention {Years} rejected for tenant {TenantId}: {Reason}",
                    evt.RetentionYears,
                    evt.OfficeTenantId,
                    applied.Error.Message
                );
                return;
            }

            if (!applied.Value && !isNew)
            {
                logger.LogInformation(
                    "Office retention change of {UpdatedAtUtc:o} ignored for tenant {TenantId}: already applied {AppliedAtUtc:o}.",
                    evt.UpdatedAtUtc,
                    evt.OfficeTenantId,
                    settings.OfficeRetentionUpdatedAtUtc
                );
                return;
            }

            if (isNew)
                await repository.AddAsync(settings, ct);
            await unitOfWork.SaveChangesAsync(ct);

            logger.LogInformation(
                "Office retention {Years} applied to Signature settings of tenant {TenantId}.",
                evt.RetentionYears?.ToString() ?? "(not set)",
                evt.OfficeTenantId
            );
        }
    }

    private static TenantSignatureSettings CreateDefaultSettings(Guid tenantId, IAuditSecretFactory auditSecretFactory)
    {
        var result = TenantSignatureSettings.CreateForNewTenant(tenantId, auditSecretFactory.GenerateProtected());
        if (result.IsFailure)
            throw new InvalidOperationException(
                $"Cannot initialize Signature settings for tenant {tenantId:D}: {result.Error.Message}"
            );
        return result.Value;
    }
}
