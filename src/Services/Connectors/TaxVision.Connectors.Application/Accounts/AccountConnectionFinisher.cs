using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Connectors.Application.Abstractions;
using TaxVision.Connectors.Application.Audit;
using TaxVision.Connectors.Application.Watch;
using TaxVision.Connectors.Domain.Accounts;
using TaxVision.Connectors.Domain.Audit;

namespace TaxVision.Connectors.Application.Accounts;

/// <summary>Último tramo de conectar una cuenta, igual por OAuth y por manual: activar el watch y dejar constancia.</summary>
public sealed class AccountConnectionFinisher(
    ITenantEmailAccountRepository accounts,
    IProviderWatchSubscriptionRepository watchSubscriptions,
    IWatchProviderClientFactory watchClientFactory,
    IProviderConnectionAuditLogRepository auditLogs,
    IUnitOfWork unitOfWork,
    ICorrelationContext correlation
)
{
    /// <summary>
    /// <see cref="WatchActivationService"/> publica ConnectorsTenantEmailAccountConnected al activar (punto único
    /// compartido con el reauth), así que quien llama ya no lo publica: se evita el doble evento.
    /// </summary>
    public Task<Result> ActivateAsync(Guid tenantId, Guid accountId, IOutboxUnit unit, CancellationToken ct) =>
        WatchActivationService.ActivateAsync(
            tenantId,
            accountId,
            accounts,
            watchSubscriptions,
            watchClientFactory,
            unit,
            correlation,
            ct
        );

    public async Task AuditConnectedAsync(TenantEmailAccount account, string detail, DateTime now, CancellationToken ct)
    {
        var auditResult = ProviderConnectionAuditLog.Create(
            account.Id,
            ProviderConnectionAuditAction.Connect,
            detail,
            "Success",
            now
        );
        if (auditResult.IsFailure)
            return;

        await auditLogs.AddAsync(auditResult.Value, ct);
        await unitOfWork.SaveChangesAsync(ct);
    }
}
