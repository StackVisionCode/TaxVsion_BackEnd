using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Projections;

namespace TaxVision.Correspondence.Application.Projections;

/// <summary>
/// Llena y repara <see cref="MailboxOwnership"/> desde Connectors, un tenant por llamada. Idempotente: es el llenado
/// inicial y también lo que corrige un evento perdido. Si Connectors no responde, no toca nada.
/// </summary>
public sealed class MailboxOwnershipReconciliation(
    IConnectorsClient connectors,
    IMailboxOwnershipRepository ownership,
    IUnitOfWork unitOfWork,
    ILogger<MailboxOwnershipReconciliation> logger
)
{
    public async Task ReconcileTenantAsync(Guid tenantId, CancellationToken ct = default)
    {
        // Marca = hora de lectura del estado autoritativo: lo anterior que llegue después ya está reflejado.
        var readAtUtc = DateTime.UtcNow;
        var mailboxes = await connectors.GetMailboxAddressesAsync(tenantId, ct);
        if (mailboxes.IsFailure)
        {
            logger.LogWarning(
                "Could not read mailboxes of tenant {TenantId}; will retry on the next run: {Error}",
                tenantId,
                mailboxes.Error.Code
            );
            return;
        }

        // Sin estos campos Connectors todavía no está desplegado: todo parecería de oficina.
        if (mailboxes.Value.Any(mailbox => mailbox.IsActive is null))
        {
            logger.LogWarning("Connectors did not send mailbox owners for tenant {TenantId}; skipped.", tenantId);
            return;
        }

        foreach (var mailbox in mailboxes.Value)
        {
            var row = await ownership.GetAsync(tenantId, mailbox.AccountId, ct);
            if (row is not null)
                row.Apply(mailbox.OwnerUserId, mailbox.IsActive!.Value, readAtUtc);
            else
                await ownership.AddAsync(
                    MailboxOwnership.Create(
                        tenantId,
                        mailbox.AccountId,
                        mailbox.OwnerUserId,
                        mailbox.IsActive!.Value,
                        readAtUtc
                    ),
                    ct
                );
        }

        if (mailboxes.Value.Count > 0)
            await unitOfWork.SaveChangesAsync(ct);
    }
}
