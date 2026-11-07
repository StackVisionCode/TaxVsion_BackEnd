using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Inbox;

namespace TaxVision.Correspondence.Application.Ingest;

/// <summary>
/// Ver <see cref="RepairCrossCustomerThreadsCommand"/>. Para cada correo ajeno busca (o crea) el hilo
/// <c>(TenantId, CustomerId del correo, ProviderThreadId del hilo original)</c>, lo mueve y recalcula contador y
/// fechas de los hilos tocados. Requiere el índice nuevo de COR-02 aplicado.
/// </summary>
public static class RepairCrossCustomerThreadsHandler
{
    public static async Task<RepairCrossCustomerThreadsResult> Handle(
        RepairCrossCustomerThreadsCommand command,
        IIncomingEmailRepository incomingEmails,
        IEmailThreadRepository threads,
        IUnitOfWork unitOfWork,
        ILogger<RepairCrossCustomerThreadsCommand> logger,
        CancellationToken ct
    )
    {
        var tenantId = command.TenantId;
        var foreign = await incomingEmails.ListInForeignThreadsAsync(tenantId, ct);
        if (foreign.Count == 0)
            return new RepairCrossCustomerThreadsResult(0, 0);

        var touched = new Dictionary<Guid, EmailThread>();
        var targets = new Dictionary<(Guid SourceId, Guid CustomerId), EmailThread>();
        var moved = 0;
        var created = 0;

        foreach (var email in foreign.OrderBy(e => e.ReceivedAtUtc))
        {
            if (email.CustomerId is not { } customerId)
                continue;

            if (!touched.TryGetValue(email.EmailThreadId, out var source))
            {
                source = await threads.GetByIdAsync(tenantId, email.EmailThreadId, ct);
                if (source is null)
                    continue;
                touched[source.Id] = source;
            }

            var key = (source.Id, customerId);
            if (!targets.TryGetValue(key, out var target))
            {
                target = source.ProviderThreadId is null
                    ? null
                    : await threads.FindByProviderThreadIdAsync(tenantId, customerId, source.ProviderThreadId, ct);
                if (target is null)
                {
                    var newThread = EmailThread.NewFromMessage(
                        tenantId,
                        customerId,
                        source.Subject,
                        source.ProviderThreadId,
                        email.ReceivedAtUtc
                    );
                    if (newThread.IsFailure)
                    {
                        logger.LogWarning("Repair skipped email {EmailId}: {Error}", email.Id, newThread.Error.Code);
                        continue;
                    }
                    target = newThread.Value;
                    await threads.AddAsync(target, ct);
                    created++;
                }
                targets[key] = target;
                touched[target.Id] = target;
            }

            if (email.MoveToThread(target.Id).IsSuccess)
                moved++;
        }

        // Se guarda antes de recalcular: los conteos se leen de la base con los correos ya movidos.
        await unitOfWork.SaveChangesAsync(ct);
        foreach (var thread in touched.Values)
        {
            var alive = (await incomingEmails.ListAllByThreadAsync(tenantId, thread.Id, ct))
                .Where(e => !e.IsDeleted)
                .ToList();
            if (alive.Count == 0)
                thread.ResetMessageStats(0, thread.FirstMessageAtUtc, thread.LastMessageAtUtc);
            else
                thread.ResetMessageStats(alive.Count, alive.Min(e => e.ReceivedAtUtc), alive.Max(e => e.ReceivedAtUtc));
        }
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation(
            "Cross-customer thread repair for tenant {TenantId}: {Moved} emails moved, {Created} threads created.",
            tenantId,
            moved,
            created
        );
        return new RepairCrossCustomerThreadsResult(moved, created);
    }
}
