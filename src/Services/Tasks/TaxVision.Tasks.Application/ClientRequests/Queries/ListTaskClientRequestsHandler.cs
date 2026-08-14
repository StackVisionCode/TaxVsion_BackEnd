using BuildingBlocks.Results;
using TaxVision.Tasks.Application.ClientRequests.Abstractions;

namespace TaxVision.Tasks.Application.ClientRequests.Queries;

/// <summary>Lo que la firma le pidió al cliente por este encargo, para la vista del preparador.</summary>
public sealed record ListTaskClientRequestsQuery(Guid TenantId, Guid TaskId);

public static class ListTaskClientRequestsHandler
{
    public static async Task<Result<IReadOnlyList<ClientRequestResponse>>> Handle(
        ListTaskClientRequestsQuery query,
        IClientRequestRepository requests,
        CancellationToken ct
    )
    {
        var found = await requests.ListForTaskAsync(query.TenantId, query.TaskId, ct);

        return Result.Success<IReadOnlyList<ClientRequestResponse>>([.. found.Select(ClientRequestResponse.From)]);
    }
}
