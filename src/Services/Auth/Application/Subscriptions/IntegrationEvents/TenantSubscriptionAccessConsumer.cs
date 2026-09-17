using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Subscriptions.Abstractions;
using TaxVision.Auth.Domain.Audit;

namespace TaxVision.Auth.Application.Subscriptions.IntegrationEvents;

/// <summary>
/// Corte/reactivación de acceso por facturación (plan de Expiración/Dunning, Fase 2). Consume el
/// ciclo de vida de la suscripción y, cuando entra en <c>Suspended</c>/<c>Expired</c>, marca el tenant
/// como bloqueado por billing y REVOCA todas sus sesiones vivas (staff y clientes por igual — la
/// revocación no filtra por actor). Los gates de login/refresh consultan el flag y devuelven
/// <c>Auth.SubscriptionInactive</c> — distinto de la suspensión admin (<c>tenant.IsActive</c>). Al volver
/// a <c>Active</c> (recuperación de pago o reactivación) se limpia el bloqueo y los usuarios re-loguean.
/// Estados intermedios (PastDue/GracePeriod/Trialing/Cancelled) NO cortan acceso: son avisos.
/// </summary>
public static class TenantSubscriptionAccessConsumer
{
    private static readonly HashSet<string> AccessBlockingStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Suspended",
        "Expired",
    };

    public static async Task Handle(
        TenantSubscriptionStatusChangedIntegrationEvent evt,
        ITenantRegistry tenants,
        ISessionRepository sessions,
        IAuthAuditWriter audit,
        ISubscriptionAccessMetrics metrics,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<TenantSubscriptionStatusChangedIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        var correlationId = string.IsNullOrWhiteSpace(evt.CorrelationId)
            ? evt.EventId.ToString("N")
            : evt.CorrelationId;

        using (correlation.Push(correlationId))
        {
            if (AccessBlockingStatuses.Contains(evt.Status))
            {
                await tenants.SetBillingBlockedAsync(evt.TenantId, blocked: true, reason: evt.Status, ct);
                var revoked = await sessions.RevokeAllForTenantAsync(evt.TenantId, "subscription_lapsed", ct);
                // Auditoría (sistema, sin userId/IP): quién no, pero sí qué/por qué/cuánto — TargetType corto.
                await audit.AddAsync(
                    AuthAuditLog.Record(
                        evt.TenantId,
                        userId: null,
                        AuthAuditAction.SubscriptionAccessBlocked,
                        success: true,
                        ipAddress: null,
                        userAgent: null,
                        correlationId,
                        targetType: "TenantSubscription",
                        targetId: evt.TenantSubscriptionId,
                        detailsJson: $$"""{"status":"{{evt.Status}}","reason":"{{evt.Reason}}","revokedSessions":{{revoked}}}"""
                    ),
                    ct
                );
                await unitOfWork.SaveChangesAsync(ct);
                metrics.RecordAccessBlocked(evt.Status);

                logger.LogWarning(
                    "Tenant {TenantId} access blocked for billing ({Status}); revoked {Count} session(s).",
                    evt.TenantId,
                    evt.Status,
                    revoked
                );
                return;
            }

            if (string.Equals(evt.Status, "Active", StringComparison.OrdinalIgnoreCase))
            {
                await tenants.SetBillingBlockedAsync(evt.TenantId, blocked: false, reason: null, ct);
                await audit.AddAsync(
                    AuthAuditLog.Record(
                        evt.TenantId,
                        userId: null,
                        AuthAuditAction.SubscriptionAccessRestored,
                        success: true,
                        ipAddress: null,
                        userAgent: null,
                        correlationId,
                        targetType: "TenantSubscription",
                        targetId: evt.TenantSubscriptionId,
                        detailsJson: $$"""{"reason":"{{evt.Reason}}"}"""
                    ),
                    ct
                );
                await unitOfWork.SaveChangesAsync(ct);
                metrics.RecordAccessRestored();

                logger.LogInformation("Tenant {TenantId} billing access restored (Active).", evt.TenantId);
            }
        }
    }
}
