using TaxVision.Correspondence.Application.Abstractions;

namespace TaxVision.Correspondence.Application.Threads;

public sealed record GetUnreadMailCountQuery(
    Guid TenantId,
    IReadOnlyCollection<Guid>? VisibleAccountIds,
    Guid? RestrictToUserId
);

/// <summary>No leídos de los hilos activos visibles, para los badges sin cargar listas.</summary>
public static class GetUnreadMailCountHandler
{
    public static Task<UnreadMailCount> Handle(
        GetUnreadMailCountQuery query,
        IEmailThreadRepository emailThreads,
        CancellationToken ct
    ) => emailThreads.CountUnreadAsync(query.TenantId, query.VisibleAccountIds, query.RestrictToUserId, ct);
}
