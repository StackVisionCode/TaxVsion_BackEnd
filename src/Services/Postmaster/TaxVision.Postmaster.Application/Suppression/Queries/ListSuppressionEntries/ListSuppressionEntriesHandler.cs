namespace TaxVision.Postmaster.Application.Suppression.Queries.ListSuppressionEntries;

public static class ListSuppressionEntriesHandler
{
    public static async Task<IReadOnlyList<SuppressionListEntryDto>> Handle(
        ListSuppressionEntriesQuery query,
        ISuppressionListRepository repository,
        CancellationToken ct
    )
    {
        var page = query.Page <= 0 ? 1 : query.Page;
        var pageSize = query.PageSize is <= 0 or > 200 ? 50 : query.PageSize;

        var entries = await repository.ListAsync(query.TenantId, query.Address, query.Reason, page, pageSize, ct);
        return entries
            .Select(e => new SuppressionListEntryDto(
                e.EmailAddress,
                e.Reason.ToString(),
                e.AddedAtUtc,
                e.AddedByUserId,
                e.Notes
            ))
            .ToList();
    }
}
