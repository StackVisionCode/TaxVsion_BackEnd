using BuildingBlocks.Common;
using BuildingBlocks.Results;
using TaxVision.Tenant.Application.Team.Abstractions;
using TaxVision.Tenant.Domain.Team;

namespace TaxVision.Tenant.Application.Team.Queries;

/// <summary>
/// Permisos del que mira, resueltos en la API: <c>users.work.view</c>, <c>customers.view_all</c>,
/// <c>notes.view_all</c> e <c>invoicing.view</c>.
/// </summary>
public sealed record ActivityViewerRights(
    bool CanViewOthers,
    bool CanViewAllCustomers,
    bool CanViewPrivate,
    bool CanViewRestricted
);

/// <summary>Actividad de un miembro en [FromUtc, ToUtc), más reciente primero. <c>Verbs</c> es una lista con comas.</summary>
public sealed record GetMemberActivityQuery(
    Guid TenantId,
    Guid ViewerUserId,
    Guid MemberUserId,
    ActivityViewerRights Rights,
    DateTime? FromUtc,
    DateTime? ToUtc,
    string? Verbs,
    int? Page,
    int? Size
);

public static class GetMemberActivityHandler
{
    public const int DefaultSize = 25;
    public const int MaxSize = 100;

    public static async Task<Result<PagedResult<ActivityEntryView>>> Handle(
        GetMemberActivityQuery query,
        IActivityEntryRepository entries,
        CancellationToken ct
    )
    {
        var filter = FilterFor(query);
        if (filter.IsFailure)
            return Result.Failure<PagedResult<ActivityEntryView>>(filter.Error);

        var page = await entries.ListAsync(filter.Value, ct);
        var items = page.Items.Select(ActivityEntryView.From).ToList();
        return Result.Success(new PagedResult<ActivityEntryView>(items, page.Page, page.Size, page.TotalCount));
    }

    // Lo propio siempre; lo de otro exige users.work.view. Privadas: el autor (el propio miembro) o notes.view_all.
    private static Result<ActivityFeedFilter> FilterFor(GetMemberActivityQuery query)
    {
        var own = query.ViewerUserId == query.MemberUserId;
        if (!own && !query.Rights.CanViewOthers)
            return Result.Failure<ActivityFeedFilter>(TeamErrors.ActivityForbidden);
        if (query.FromUtc is { } from && query.ToUtc is { } to && from >= to)
            return Result.Failure<ActivityFeedFilter>(TeamErrors.InvalidRange);
        if (!TryParseVerbs(query.Verbs, out var verbs))
            return Result.Failure<ActivityFeedFilter>(TeamErrors.UnknownVerbFilter);

        return Result.Success(
            new ActivityFeedFilter(
                query.TenantId,
                query.MemberUserId,
                query.FromUtc,
                query.ToUtc,
                verbs,
                query.Rights.CanViewAllCustomers ? null : query.ViewerUserId,
                own || query.Rights.CanViewPrivate,
                query.Rights.CanViewRestricted,
                Math.Max(1, query.Page ?? 1),
                Math.Clamp(query.Size ?? DefaultSize, 1, MaxSize)
            )
        );
    }

    private static bool TryParseVerbs(string? raw, out IReadOnlyCollection<string> verbs)
    {
        var parsed = (raw ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        verbs = parsed;
        return parsed.TrueForAll(ActivityVerbs.IsKnown);
    }
}
