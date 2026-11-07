using Microsoft.Extensions.Logging;
using TaxVision.Correspondence.Application.Abstractions;
using Wolverine;
using Wolverine.Attributes;

namespace TaxVision.Correspondence.Application.Ingest;

/// <summary>
/// Ver <see cref="RepairCrossCustomerThreadsCommand"/>. Sin transacción propia: cada hilo es una unidad aparte
/// (<see cref="RepairCrossCustomerThreadCommand"/>), así un hilo que falla no revierte los ya reparados.
/// </summary>
public static class RepairCrossCustomerThreadsHandler
{
    [NonTransactional]
    public static async Task<RepairCrossCustomerThreadsReport> Handle(
        RepairCrossCustomerThreadsCommand command,
        IIncomingEmailRepository incomingEmails,
        IMessageBus bus,
        ILogger<RepairCrossCustomerThreadsCommand> logger,
        CancellationToken ct
    )
    {
        var foreign = await incomingEmails.ListInForeignThreadsAsync(command.TenantId, ct);
        var threadIds = foreign
            .GroupBy(e => e.EmailThreadId)
            .OrderBy(g => g.Min(e => e.ReceivedAtUtc))
            .Select(g => g.Key)
            .ToList();
        if (command.DryRun || threadIds.Count == 0)
            return new RepairCrossCustomerThreadsReport(command.DryRun, threadIds.Count, foreign.Count, 0, 0, 0, []);

        var repaired = 0;
        var moved = 0;
        var created = 0;
        var failed = new List<Guid>();
        foreach (var threadId in threadIds)
        {
            try
            {
                var unit = await bus.InvokeAsync<RepairCrossCustomerThreadResult>(
                    new RepairCrossCustomerThreadCommand(command.TenantId, threadId),
                    ct
                );
                if (unit.MovedEmails > 0)
                    repaired++;
                moved += unit.MovedEmails;
                created += unit.CreatedThreads;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogError(ex, "Cross-customer repair failed for thread {ThreadId}; it can be retried.", threadId);
                failed.Add(threadId);
            }
        }

        logger.LogInformation(
            "Cross-customer thread repair for tenant {TenantId}: {Repaired} of {Found} threads, {Moved} emails moved, {Created} threads created, {Failed} failed.",
            command.TenantId,
            repaired,
            threadIds.Count,
            moved,
            created,
            failed.Count
        );
        return new RepairCrossCustomerThreadsReport(false, threadIds.Count, foreign.Count, repaired, moved, created, failed);
    }
}
