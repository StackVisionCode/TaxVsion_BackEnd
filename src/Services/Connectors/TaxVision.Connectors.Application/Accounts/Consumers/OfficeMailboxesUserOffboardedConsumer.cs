using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.ConnectorsIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Connectors.Domain.Accounts;
using Wolverine;

namespace TaxVision.Connectors.Application.Accounts.Consumers;

/// <summary>
/// Al RETIRAR a un empleado: los buzones de OFICINA ligados a él (su dirección, o los creó/conectó) NO se
/// desconectan — son de la oficina y cortarlos a ciegas frenaría el correo de clientes. Se marcan para
/// revisión y se avisa a quienes administran los buzones. Idempotente (uno ya marcado no se re-avisa) y
/// sin asumir orden: si alguien lo volvió a conectar después del retiro, no se toca.
/// </summary>
public static class OfficeMailboxesUserOffboardedConsumer
{
    public static async Task Handle(
        UserOffboardedIntegrationEvent msg,
        ITenantEmailAccountRepository accounts,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<TenantEmailAccount> logger,
        CancellationToken ct
    )
    {
        using var _ = correlation.Push(
            string.IsNullOrWhiteSpace(msg.CorrelationId) ? msg.EventId.ToString("N") : msg.CorrelationId
        );

        var tenantAccounts = await accounts.ListByTenantAsync(msg.TenantId, ct);
        var now = DateTime.UtcNow;
        var flagged = new List<(TenantEmailAccount Account, string Reason)>();
        foreach (var account in tenantAccounts)
        {
            // Reconectado después del retiro = ya lo autorizó alguien vigente.
            if (account.ConnectedAtUtc > msg.RemovedAtUtc)
                continue;
            if (account.OffboardingReviewReasonFor(msg.UserId, msg.Email) is not { } reason)
                continue;
            if (account.RequireReview(reason, now))
                flagged.Add((account, reason));
        }

        if (flagged.Count == 0)
            return;

        // Guardar antes de publicar: si el guardado falla y se reintenta, el aviso no sale repetido.
        await unitOfWork.SaveChangesAsync(ct);

        foreach (var (account, reason) in flagged)
            await bus.PublishAsync(
                new ConnectorsOfficeMailboxReviewRequiredIntegrationEvent
                {
                    TenantId = msg.TenantId,
                    CorrelationId = correlation.CorrelationId,
                    AccountId = account.Id,
                    EmailAddress = account.EmailAddress,
                    ProviderCode = account.ProviderCode.ToString(),
                    Reason = reason,
                    OffboardedUserId = msg.UserId,
                    AudiencePermission = ConnectorsPermissions.AccountsWrite,
                    FlaggedAtUtc = now,
                }
            );

        logger.LogInformation(
            "Offboarded user {UserId}: flagged {Count} office mailbox(es) for review in tenant {TenantId}.",
            msg.UserId,
            flagged.Count,
            msg.TenantId
        );
    }
}
