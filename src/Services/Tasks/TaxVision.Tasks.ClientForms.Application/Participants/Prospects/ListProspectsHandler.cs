namespace TaxVision.Tasks.ClientForms.Application.Participants.Prospects;

public static class ListProspectsHandler
{
    public const int DefaultSize = 25;
    public const int MaxSize = 100;

    public static async Task<ProspectPageResponse> Handle(
        ListProspectsQuery query,
        IProspectReadRepository reads,
        ProspectResponseMapper mapper,
        CancellationToken ct
    )
    {
        var normalized = query with
        {
            Search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim(),
            Page = Math.Max(1, query.Page),
            Size = query.Size < 1 ? DefaultSize : Math.Min(query.Size, MaxSize),
        };
        var page = await reads.ListAsync(normalized, ct);
        var items = await mapper.MapAsync(
            page.Items,
            normalized.TenantId,
            normalized.ActorUserId,
            normalized.CanViewAll,
            normalized.CanStartCustomerCreation,
            ct
        );
        return new ProspectPageResponse(items, normalized.Page, normalized.Size, page.TotalCount);
    }
}
