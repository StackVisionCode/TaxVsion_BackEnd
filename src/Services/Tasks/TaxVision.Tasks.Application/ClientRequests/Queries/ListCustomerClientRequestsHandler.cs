using BuildingBlocks.Results;
using TaxVision.Tasks.Application.ClientRequests.Abstractions;

namespace TaxVision.Tasks.Application.ClientRequests.Queries;

/// <summary>
/// Todo lo que la firma le pidió a un cliente, para la vista del preparador en el perfil del
/// cliente. El portal ve lo suyo por <see cref="ListPortalClientRequestsQuery"/> (deriva el
/// cliente del token); esta consulta es la contraparte de staff, que pide el cliente explícito.
/// </summary>
public sealed record ListCustomerClientRequestsQuery(Guid TenantId, Guid CustomerId, bool OnlyOpen);

public static class ListCustomerClientRequestsHandler
{
    public static async Task<Result<IReadOnlyList<ClientRequestResponse>>> Handle(
        ListCustomerClientRequestsQuery query,
        IClientRequestRepository requests,
        CancellationToken ct
    )
    {
        var found = await requests.ListForCustomerAsync(query.TenantId, query.CustomerId, query.OnlyOpen, ct);

        return Result.Success<IReadOnlyList<ClientRequestResponse>>([.. found.Select(ClientRequestResponse.From)]);
    }
}
