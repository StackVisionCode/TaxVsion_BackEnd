using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using BuildingBlocks.Persistence;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Settings;

namespace TaxVision.Signature.Application.Settings.IntegrationEvents;

/// <summary>
/// Alta de tenant ⇒ se inicializan las settings de firma con los defaults del ecosistema.
/// El tenant interno de plataforma no se inicializa. El handler es idempotente: si ya
/// existe la fila, no hace nada.
/// </summary>
public static class TenantCreatedConsumer
{
    public static async Task Handle(
        TenantCreatedIntegrationEvent evt,
        ITenantSignatureSettingsRepository repository,
        IAuditSecretFactory auditSecretFactory,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<TenantSignatureSettings> logger,
        CancellationToken ct
    )
    {
        var correlationId = ResolveCorrelationId(evt);
        using (correlation.Push(correlationId))
        {
            if (IsPlatformTenant(evt))
            {
                logger.LogInformation(
                    "Skipping Signature settings initialization for platform tenant {TenantId}.",
                    evt.NewTenantId
                );
                return;
            }

            if (await AlreadyInitialized(evt.NewTenantId, repository, ct))
            {
                logger.LogInformation(
                    "Signature settings already exist for tenant {TenantId}; skipping.",
                    evt.NewTenantId
                );
                return;
            }

            var settings = CreateDefaultSettings(evt.NewTenantId, auditSecretFactory);
            await repository.AddAsync(settings, ct);
            await unitOfWork.SaveChangesAsync(ct);

            logger.LogInformation(
                "Signature settings initialized for tenant {TenantId} with defaults (channels={Channels}).",
                evt.NewTenantId,
                settings.AllowedVerificationChannels
            );
        }
    }

    // ------------------------------------------------------------------
    // Métodos privados: cada uno hace una cosa y su nombre lo dice.
    // ------------------------------------------------------------------

    private static string ResolveCorrelationId(TenantCreatedIntegrationEvent evt) =>
        string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId;

    private static bool IsPlatformTenant(TenantCreatedIntegrationEvent evt) =>
        Enum.TryParse<TenantKind>(evt.Kind, ignoreCase: true, out var kind) && kind == TenantKind.Platform;

    private static async Task<bool> AlreadyInitialized(
        Guid tenantId,
        ITenantSignatureSettingsRepository repository,
        CancellationToken ct
    )
    {
        var existing = await repository.GetByTenantIdAsync(tenantId, ct);
        return existing is not null;
    }

    private static TenantSignatureSettings CreateDefaultSettings(Guid tenantId, IAuditSecretFactory auditSecretFactory)
    {
        var encryptedSecret = auditSecretFactory.GenerateProtected();
        var result = TenantSignatureSettings.CreateForNewTenant(tenantId, encryptedSecret);
        if (result.IsFailure)
            throw new InvalidOperationException(
                $"Cannot initialize Signature settings for tenant {tenantId:D}: {result.Error.Message}"
            );

        return result.Value;
    }
}
