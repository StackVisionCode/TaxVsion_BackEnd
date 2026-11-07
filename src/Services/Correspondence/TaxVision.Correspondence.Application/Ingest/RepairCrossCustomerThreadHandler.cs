using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Inbox;

namespace TaxVision.Correspondence.Application.Ingest;

/// <summary>
/// Un hilo de la reparación de COR-02: cada correo ajeno va al hilo <c>(TenantId, cliente del correo, ProviderThreadId
/// del original)</c>, que se busca o se crea, y se recalculan contador y fechas de los hilos tocados. Un solo guardado.
/// </summary>
public static class RepairCrossCustomerThreadHandler
{
    public static async Task<RepairCrossCustomerThreadResult> Handle(
        RepairCrossCustomerThreadCommand command,
        IIncomingEmailRepository incomingEmails,
        IEmailThreadRepository threads,
        IUnitOfWork unitOfWork,
        ILogger<RepairCrossCustomerThreadCommand> logger,
        CancellationToken ct
    )
    {
        var tenantId = command.TenantId;
        var source = await threads.GetByIdAsync(tenantId, command.ThreadId, ct);
        if (source?.CustomerId is not { } owner)
            return new RepairCrossCustomerThreadResult(0, 0);

        var emails = await incomingEmails.ListByThreadForUpdateAsync(tenantId, source.Id, ct);
        var foreign = emails
            .Where(e => e.CustomerId is { } customerId && customerId != owner)
            .OrderBy(e => e.ReceivedAtUtc)
            .ToList();
        if (foreign.Count == 0)
            return new RepairCrossCustomerThreadResult(0, 0);

        var targets = new Dictionary<Guid, (EmailThread Thread, List<IncomingEmail> Emails)>();
        var movedEmails = new List<IncomingEmail>();
        var created = 0;
        foreach (var email in foreign)
        {
            var customerId = email.CustomerId!.Value;
            if (!targets.TryGetValue(customerId, out var target))
            {
                var thread = source.ProviderThreadId is null
                    ? null
                    : await threads.FindByProviderThreadIdAsync(tenantId, customerId, source.ProviderThreadId, ct);
                List<IncomingEmail> existing = [];
                if (thread is null)
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
                    thread = newThread.Value;
                    await threads.AddAsync(thread, ct);
                    created++;
                }
                else
                    existing = [.. await incomingEmails.ListAllByThreadAsync(tenantId, thread.Id, ct)];
                target = (thread, existing);
                targets[customerId] = target;
            }

            if (email.MoveToThread(target.Thread.Id).IsFailure)
                continue;
            target.Emails.Add(email);
            movedEmails.Add(email);
        }

        // Se recalcula en memoria para guardar todo junto.
        ResetStats(source, emails.Except(movedEmails));
        foreach (var (thread, threadEmails) in targets.Values)
            ResetStats(thread, threadEmails);
        await unitOfWork.SaveChangesAsync(ct);
        return new RepairCrossCustomerThreadResult(movedEmails.Count, created);
    }

    private static void ResetStats(EmailThread thread, IEnumerable<IncomingEmail> emails)
    {
        var alive = emails.Where(e => !e.IsDeleted).ToList();
        if (alive.Count == 0)
            thread.ResetMessageStats(0, thread.FirstMessageAtUtc, thread.LastMessageAtUtc);
        else
            thread.ResetMessageStats(alive.Count, alive.Min(e => e.ReceivedAtUtc), alive.Max(e => e.ReceivedAtUtc));
    }
}
