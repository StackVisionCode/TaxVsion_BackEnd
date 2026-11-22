namespace TaxVision.Tasks.ClientForms.Application.Requests.Work;

/// <summary>Lo pide <c>/tasks/work/office/waiting</c> después de autorizar al actor (<c>users.work.view</c>).</summary>
public sealed record GetFormRequestOfficeWaitingQuery(Guid TenantId, Guid ActorUserId, bool CanViewAll);

public static class GetFormRequestOfficeWaitingHandler
{
    public static Task<int> Handle(
        GetFormRequestOfficeWaitingQuery query,
        IFormRequestWorkReader reader,
        CancellationToken ct
    ) => reader.CountOfficeWaitingAsync(query.TenantId, query.ActorUserId, query.CanViewAll, ct);
}
