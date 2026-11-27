using BuildingBlocks.Persistence;
using TaxVision.Postmaster.Application.Abstractions;

namespace TaxVision.Postmaster.Application.Projections.ConnectorsEvents;

/// <summary>Resultado de una pasada: filas pendientes vistas, dueños aplicados y si Connectors respondió.</summary>
public sealed record MailboxOwnershipBackfillOutcome(int Pending, int Applied, bool ConnectorsReachable);

/// <summary>
/// DM-02: llena el dueño de los buzones proyectados antes de este campo, leyéndolo de Connectors por tenant.
/// Idempotente; solo escribe dueños. Si Connectors no responde o no manda dueños, la fila sigue sin usarse.
/// </summary>
public static class MailboxOwnershipBackfill
{
    public const int BatchSize = 500;

    public static async Task<MailboxOwnershipBackfillOutcome> RunAsync(
        IConnectorsMailboxDirectory directory,
        IConnectedMailboxRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var pending = await repository.ListWithUnknownOwnershipAsync(BatchSize, ct);
        var applied = 0;
        var reachable = true;
        foreach (var tenant in pending.GroupBy(row => row.TenantId))
        {
            var mailboxes = await directory.ListAsync(tenant.Key, ct);
            if (mailboxes.IsFailure)
            {
                reachable = false;
                continue;
            }

            // Solo lo que Connectors declara (oficina o personal); un Connectors viejo sin IsOffice no aplica nada.
            var owners = mailboxes.Value.ToDictionary(
                mailbox => mailbox.AccountId,
                mailbox => MailboxOwnership.Resolve(mailbox.OwnerUserId, mailbox.IsOffice)
            );
            foreach (var row in tenant)
            {
                if (!owners.TryGetValue(row.AccountId, out var ownership) || !ownership.Known)
                    continue;
                row.ApplyOwnership(ownership.OwnerUserId);
                applied++;
            }
        }

        if (applied > 0)
            await unitOfWork.SaveChangesAsync(ct);
        return new MailboxOwnershipBackfillOutcome(pending.Count, applied, reachable);
    }
}
