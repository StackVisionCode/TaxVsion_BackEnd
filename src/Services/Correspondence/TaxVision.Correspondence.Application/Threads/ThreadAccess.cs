using BuildingBlocks.Results;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Inbox;

namespace TaxVision.Correspondence.Application.Threads;

/// <summary>
/// Gate de UN hilo para leerlo o actuar sobre él: buzón visible (<c>null</c> = todos) y cliente asignado
/// (<c>null</c> = todos, <c>customers.view_all</c>). Quien no lo ve recibe el mismo NotFound que si no existiera.
/// </summary>
internal static class ThreadAccess
{
    public static readonly Error NotFound = new("EmailThread.NotFound", "The thread was not found for this tenant.");

    public static async Task<bool> CanSeeAsync(
        IEmailThreadRepository emailThreads,
        EmailThread thread,
        IReadOnlyCollection<Guid>? visibleAccountIds,
        Guid? restrictToUserId,
        CancellationToken ct
    )
    {
        // Un hilo sin ningún mensaje de un buzón visible se oculta; si es visible se ve ENTERO.
        if (
            visibleAccountIds is not null
            && !await emailThreads.HasVisibleMessageAsync(thread.TenantId, thread.Id, visibleAccountIds, ct)
        )
            return false;

        return await emailThreads.IsCustomerVisibleAsync(thread.TenantId, thread.CustomerId, restrictToUserId, ct);
    }
}
