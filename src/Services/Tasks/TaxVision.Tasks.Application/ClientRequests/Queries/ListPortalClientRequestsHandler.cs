using BuildingBlocks.Results;
using TaxVision.Tasks.Application.ClientRequests.Abstractions;

namespace TaxVision.Tasks.Application.ClientRequests.Queries;

/// <param name="CustomerId">Del token del portal. El cliente no elige de quién son los pedidos que ve.</param>
public sealed record ListPortalClientRequestsQuery(Guid TenantId, Guid CustomerId, bool OnlyOpen);

public static class ListPortalClientRequestsHandler
{
    public static async Task<Result<IReadOnlyList<PortalClientRequestResponse>>> Handle(
        ListPortalClientRequestsQuery query,
        IClientRequestRepository requests,
        CancellationToken ct
    )
    {
        var found = await requests.ListForCustomerAsync(query.TenantId, query.CustomerId, query.OnlyOpen, ct);

        return Result.Success<IReadOnlyList<PortalClientRequestResponse>>([
            .. found.Select(PortalClientRequestResponse.From),
        ]);
    }
}
