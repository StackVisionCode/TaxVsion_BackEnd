using BuildingBlocks.Results;
using BuildingBlocks.TimeZones;
using Microsoft.Extensions.Options;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Analytics;

namespace TaxVision.Signature.Application.Requests.Queries.Summary;

/// <summary>
/// Resumen del widget de firmas en días de la oficina (ambos inclusive), con el recorte del listado en todo alcance.
/// CanViewOffice = el actor tiene <c>users.work.view</c> (lo exige <see cref="DashboardScope.Office"/>).
/// </summary>
public sealed record GetSignatureRequestsSummaryQuery(
    Guid TenantId,
    Guid ActorUserId,
    bool CanViewAll,
    DateOnly? From,
    DateOnly? To,
    DateTime NowUtc,
    DashboardScope Scope = DashboardScope.Default,
    bool CanViewOffice = false
);

/// <summary>
/// Alcance del resumen. Default = como siempre (lo visible para el actor); Me = lo que creó el actor; Office = toda
/// la oficina con el recorte del actor.
/// </summary>
public enum DashboardScope
{
    Default,
    Me,
    Office,
}

public static class DashboardScopes
{
    /// <summary>Vacío = <see cref="DashboardScope.Default"/>; acepta "me" u "office" sin distinguir mayúsculas.</summary>
    public static bool TryParse(string? raw, out DashboardScope scope)
    {
        scope = DashboardScope.Default;
        if (string.IsNullOrWhiteSpace(raw))
            return true;
        if (string.Equals(raw, "me", StringComparison.OrdinalIgnoreCase))
            scope = DashboardScope.Me;
        else if (string.Equals(raw, "office", StringComparison.OrdinalIgnoreCase))
            scope = DashboardScope.Office;
        else
            return false;
        return true;
    }
}

public sealed record SignatureRequestsSummaryResponse(
    DateOnly From,
    DateOnly To,
    int Draft,
    int InProgress,
    int ExpiringWithin48h,
    int Sent,
    int Completed,
    int Expired,
    int Canceled,
    double? AvgMinutesToSign
);

public static class SignatureRequestsSummaryErrors
{
    public const int MaxDays = 366;

    public static readonly Error ScopeInvalid = new(
        "Signature.Summary.ScopeInvalid",
        "scope must be 'me' or 'office'."
    );

    public static readonly Error OfficeForbidden = new(
        "Signature.Summary.OfficeForbidden",
        "The office view requires permission to view your team's work."
    );

    public static readonly Error RangeInvalid = new(
        "Signature.Summary.RangeInvalid",
        "The range is invalid: 'to' must not be before 'from' and the range cannot exceed 366 days."
    );
}

public static class GetSignatureRequestsSummaryHandler
{
    public static async Task<Result<SignatureRequestsSummaryResponse>> Handle(
        GetSignatureRequestsSummaryQuery query,
        ISignatureRequestsSummaryReader reader,
        ISignatureAnalyticsReadService analytics,
        IOfficeTimeZone zones,
        IOptions<SignatureVisibilityOptions> visibility,
        CancellationToken ct
    )
    {
        if (query.Scope == DashboardScope.Office && !query.CanViewOffice)
            return Result.Failure<SignatureRequestsSummaryResponse>(SignatureRequestsSummaryErrors.OfficeForbidden);

        var zone = await zones.ResolveAsync(query.TenantId, ct);
        var to = query.To ?? CivilDate.Of(query.NowUtc, zone);
        var from = query.From ?? new DateOnly(to.Year, to.Month, 1);
        if (to < from || to.DayNumber - from.DayNumber + 1 > SignatureRequestsSummaryErrors.MaxDays)
            return Result.Failure<SignatureRequestsSummaryResponse>(SignatureRequestsSummaryErrors.RangeInvalid);

        // Misma regla que la lista en todo alcance: sin view_all y con el flag, solo lo asignado al actor.
        var assignedTo = visibility.Value.Enabled && !query.CanViewAll ? query.ActorUserId : (Guid?)null;
        // Me = lo que creó el actor; el snapshot es de toda la oficina, así que va todo en vivo.
        var createdBy = query.Scope == DashboardScope.Me ? query.ActorUserId : (Guid?)null;
        var live = await reader.ReadAsync(
            new SignatureRequestsSummaryFilter(
                query.TenantId,
                assignedTo,
                LocalDay.StartUtc(from, zone),
                LocalDay.StartUtc(to.AddDays(1), zone),
                query.NowUtc,
                createdBy
            ),
            ct
        );
        if (assignedTo is not null || createdBy is not null)
            return Result.Success(Response(from, to, live, live.Sent, live.Completed, live.Expired, live.Canceled));

        // Oficina completa: el período sale del snapshot diario; lo vivo, de la consulta.
        var office = await analytics.GetSummaryAsync(new SignatureAnalyticsSummaryQuery(query.TenantId, from, to), ct);
        return Result.Success(
            Response(
                from,
                to,
                live,
                office.RequestsSent,
                office.RequestsCompleted,
                office.RequestsExpired,
                office.RequestsCanceled
            )
        );
    }

    private static SignatureRequestsSummaryResponse Response(
        DateOnly from,
        DateOnly to,
        SignatureRequestsLiveCounts live,
        int sent,
        int completed,
        int expired,
        int canceled
    ) =>
        new(
            from,
            to,
            live.Draft,
            live.InProgress,
            live.ExpiringWithin48h,
            sent,
            completed,
            expired,
            canceled,
            live.AvgMinutesToSign
        );
}
