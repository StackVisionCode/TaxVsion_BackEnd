using BuildingBlocks.Results;
using BuildingBlocks.TimeZones;
using TaxVision.CloudStorage.Application.Abstractions;

namespace TaxVision.CloudStorage.Application.Files.Queries;

/// <summary>Resumen del widget Documents: los últimos <paramref name="Days"/> días de la oficina, hoy incluido.</summary>
public sealed record GetFileSummaryQuery(Guid TenantId, StorageActorScope Scope, int Days);

public sealed record FileSummaryResponse(
    int Days,
    string TimeZone,
    IReadOnlyList<DailyUploads> UploadsByDay,
    bool UploadsTruncated,
    PendingReviewSummary PendingReview,
    IReadOnlyList<FileSummaryItem> Latest
);

public sealed record DailyUploads(DateOnly Day, int Count);

public sealed record PendingReviewSummary(int Count, int Stale, int StaleAfterDays);

public static class FileSummaryLimits
{
    public const int MinDays = 1;
    public const int MaxDays = 31;
    public const int DefaultDays = 14;
    public const int StaleAfterDays = 3;
    public const int MaxStamps = 20_000;
    public const int Latest = 3;

    public static readonly Error InvalidWindow = new(
        "Storage.InvalidSummaryWindow",
        "The summary window must be between 1 and 31 days."
    );
}

public static class GetFileSummaryHandler
{
    public static async Task<Result<FileSummaryResponse>> Handle(
        GetFileSummaryQuery query,
        IFileSummaryReader reader,
        IOfficeTimeZone zones,
        ISystemClock clock,
        CancellationToken ct
    )
    {
        if (query.Days is < FileSummaryLimits.MinDays or > FileSummaryLimits.MaxDays)
            return Result.Failure<FileSummaryResponse>(FileSummaryLimits.InvalidWindow);

        var zone = await zones.ResolveAsync(query.TenantId, ct);
        var now = clock.UtcNow;
        var from = CivilDate.Of(now, zone).AddDays(1 - query.Days);
        var rows = await reader.ReadAsync(
            new FileSummaryFilter(
                query.TenantId,
                // Recorte sin usuario: cerrado, solo lo de la oficina.
                query.Scope.BypassAssignmentFilter
                    ? null
                    : query.Scope.AssignedUserId ?? Guid.Empty,
                LocalDay.StartUtc(from, zone),
                now.AddDays(-FileSummaryLimits.StaleAfterDays),
                FileSummaryLimits.MaxStamps,
                FileSummaryLimits.Latest
            ),
            ct
        );
        return Result.Success(
            new FileSummaryResponse(
                query.Days,
                zone.Id,
                ByDay(rows.CreatedInWindow.Take(FileSummaryLimits.MaxStamps), from, query.Days, zone),
                rows.CreatedInWindow.Count > FileSummaryLimits.MaxStamps,
                new PendingReviewSummary(rows.PendingReview, rows.StalePendingReview, FileSummaryLimits.StaleAfterDays),
                rows.Latest
            )
        );
    }

    // SQL no conoce zonas IANA: el día de la oficina se calcula aquí, con ceros en los días sin altas.
    private static IReadOnlyList<DailyUploads> ByDay(
        IEnumerable<DateTime> stamps,
        DateOnly from,
        int days,
        ResolvedTimeZone zone
    )
    {
        var counts = new int[days];
        foreach (var stamp in stamps)
        {
            var index = CivilDate.Of(stamp, zone).DayNumber - from.DayNumber;
            if (index >= 0 && index < days)
                counts[index]++;
        }
        return counts.Select((count, i) => new DailyUploads(from.AddDays(i), count)).ToList();
    }
}
