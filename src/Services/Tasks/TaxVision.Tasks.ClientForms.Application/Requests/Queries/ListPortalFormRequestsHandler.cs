using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.ClientForms.Application.Requests.Queries;

public static class ListPortalFormRequestsHandler
{
    public static async Task<IReadOnlyList<PortalFormRequestSummaryResponse>> Handle(
        ListPortalFormRequestsQuery query,
        IFormRequestRepository requests,
        CancellationToken ct
    ) =>
        (await requests.ListByCustomerAsync(query.TenantId, query.CustomerId, query.OnlyOpen, ct))
            .Select(item => new PortalFormRequestSummaryResponse(
                item.Id,
                item.Title,
                item.Status,
                item.DueAtUtc,
                item.DueAtUtc < DateTime.UtcNow
                    && item.Status is FormRequestStatus.Pending or FormRequestStatus.Returned,
                item.DraftProgressPercent,
                item.LastSubmittedAtUtc,
                item.Status == FormRequestStatus.Returned
            ))
            .ToArray();
}
