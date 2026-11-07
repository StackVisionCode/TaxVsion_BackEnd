using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Inbox;

namespace TaxVision.Correspondence.Application.Ingest;

/// <summary>Una dirección acaba de entrar a la proyección de clientes: su correo "Not a client" pasa a la ficha.</summary>
public sealed record LinkUnlinkedMailCommand(Guid TenantId, Guid CustomerId, string Address);

/// <summary>
/// COR-05 (D-COR-3). Idempotente: una segunda corrida no encuentra nada sin cliente. Un hilo que solo tiene correo de
/// esa dirección pasa entero al cliente; si hay otros remitentes, sus correos se mueven a un hilo del cliente, porque
/// un hilo es de un solo cliente (COR-02).
/// </summary>
public static class LinkUnlinkedMailHandler
{
    public static async Task Handle(
        LinkUnlinkedMailCommand command,
        IIncomingEmailRepository incomingEmails,
        IEmailThreadRepository threads,
        IUnitOfWork unitOfWork,
        ILogger<LinkUnlinkedMailCommand> logger,
        CancellationToken ct
    )
    {
        var tenantId = command.TenantId;
        var emails = await incomingEmails.ListUnlinkedByAddressAsync(tenantId, command.Address, ct);
        if (emails.Count == 0)
            return;

        var recount = new Dictionary<Guid, EmailThread>();
        foreach (var group in emails.GroupBy(e => e.EmailThreadId))
        {
            var source = await threads.GetByIdAsync(tenantId, group.Key, ct);
            if (source is null)
                continue;

            var target = await FindCustomerThreadAsync(command, source, ct, threads);
            var sharedWithOthers = (await incomingEmails.ListAllByThreadAsync(tenantId, source.Id, ct)).Any(e =>
                e.From != command.Address
            );

            if (target is null && !sharedWithOthers && source.LinkToCustomer(command.CustomerId))
            {
                foreach (var email in group)
                    email.LinkToCustomer(command.CustomerId);
                continue;
            }

            if (target is null)
            {
                var created = EmailThread.NewFromMessage(
                    tenantId,
                    command.CustomerId,
                    source.Subject,
                    source.ProviderThreadId,
                    group.Min(e => e.ReceivedAtUtc)
                );
                if (created.IsFailure)
                {
                    logger.LogWarning("Could not link thread {ThreadId}: {Error}", source.Id, created.Error.Code);
                    continue;
                }
                target = created.Value;
                await threads.AddAsync(target, ct);
            }

            foreach (var email in group)
            {
                email.LinkToCustomer(command.CustomerId);
                email.MoveToThread(target.Id);
            }
            recount[source.Id] = source;
            recount[target.Id] = target;
        }

        // Se guarda antes de recontar: los conteos se leen de la base con los correos ya movidos.
        await unitOfWork.SaveChangesAsync(ct);
        foreach (var thread in recount.Values)
        {
            var alive = (await incomingEmails.ListAllByThreadAsync(tenantId, thread.Id, ct))
                .Where(e => !e.IsDeleted)
                .ToList();
            if (alive.Count == 0)
                thread.ResetMessageStats(0, thread.FirstMessageAtUtc, thread.LastMessageAtUtc);
            else
                thread.ResetMessageStats(alive.Count, alive.Min(e => e.ReceivedAtUtc), alive.Max(e => e.ReceivedAtUtc));
        }
        if (recount.Count > 0)
            await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation(
            "Linked {Count} emails to customer {CustomerId} of tenant {TenantId}.",
            emails.Count,
            command.CustomerId,
            tenantId
        );
    }

    // Si el cliente ya tiene ese hilo del proveedor, el índice único impide tener dos.
    private static Task<EmailThread?> FindCustomerThreadAsync(
        LinkUnlinkedMailCommand command,
        EmailThread source,
        CancellationToken ct,
        IEmailThreadRepository threads
    ) =>
        source.ProviderThreadId is { } providerThreadId
            ? threads.FindByProviderThreadIdAsync(command.TenantId, command.CustomerId, providerThreadId, ct)
            : Task.FromResult<EmailThread?>(null);
}
