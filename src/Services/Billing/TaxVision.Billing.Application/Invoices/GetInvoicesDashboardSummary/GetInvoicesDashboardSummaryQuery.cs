using System.Globalization;
using BuildingBlocks.Results;
using BuildingBlocks.TimeZones;
using Microsoft.Extensions.Options;
using TaxVision.Billing.Application.Abstractions;

namespace TaxVision.Billing.Application.Invoices.GetInvoicesDashboardSummary;

/// <summary>Revenue e Invoice aging del dashboard, sumados en el servidor. Exige <c>invoicing.summary.view</c>.</summary>
public sealed record GetInvoicesDashboardSummaryQuery(Guid TenantId, Guid ActorUserId, bool CanViewAll);

/// <param name="Scope">"office" o "assigned" (solo los clientes asignados al actor).</param>
public sealed record InvoicesDashboardSummaryResponse(
    string? Currency,
    string TimeZoneId,
    DateTime AsOfUtc,
    string Scope,
    DashboardRevenue Revenue,
    DashboardAging Aging
);

public sealed record DashboardRevenue(
    long CollectedThisMonthCents,
    long CollectedPreviousMonthToDateCents,
    long InvoicedThisMonthCents,
    IReadOnlyList<DashboardRevenueMonth> Monthly,
    IReadOnlyList<DashboardRevenueDay> Daily,
    DashboardCollectionRate CollectionRate
);

/// <param name="Month">"yyyy-MM" en la zona de la oficina.</param>
public sealed record DashboardRevenueMonth(string Month, long CollectedCents);

/// <param name="Day">"yyyy-MM-dd" del mes en curso de la oficina, hasta hoy.</param>
public sealed record DashboardRevenueDay(string Day, long CollectedCents);

/// <summary>Cobrado y facturado en la ventana de 6 meses; el porcentaje (con tope 100) lo pinta el front.</summary>
public sealed record DashboardCollectionRate(long InvoicedCents, long CollectedCents);

public sealed record DashboardAging(
    long OutstandingCents,
    int OpenCount,
    int PastDueCount,
    IReadOnlyList<DashboardAgingBucket> Buckets,
    IReadOnlyList<DashboardAgingInvoice> Oldest
);

/// <param name="To">null = sin tope.</param>
public sealed record DashboardAgingBucket(int From, int? To, long Cents, int Count);

public sealed record DashboardAgingInvoice(
    Guid InvoiceId,
    string? InvoiceNumber,
    Guid CustomerId,
    long DueCents,
    int DaysPastDue
);

public static class GetInvoicesDashboardSummaryHandler
{
    public const string OfficeScope = "office";
    public const string AssignedScope = "assigned";
    public const int OldestCount = 3;

    public static async Task<Result<InvoicesDashboardSummaryResponse>> Handle(
        GetInvoicesDashboardSummaryQuery query,
        IInvoiceDashboardReader reader,
        IOfficeTimeZone zones,
        TimeProvider clock,
        IOptions<BillingVisibilityOptions> visibility,
        CancellationToken ct
    )
    {
        // Misma visibilidad por asignación que la tabla y el resumen de Billing.
        var assignedTo = visibility.Value.Enabled && !query.CanViewAll ? query.ActorUserId : (Guid?)null;
        var zone = await zones.ResolveAsync(query.TenantId, ct);
        var nowUtc = clock.GetUtcNow().UtcDateTime;
        var periods = InvoiceDashboardPeriods.For(nowUtc, zone);

        var currency = await reader.LatestCurrencyAsync(query.TenantId, assignedTo, ct);
        var totals = currency is null
            ? DashboardTotals.Empty(periods)
            : await SumAsync(reader, new InvoiceDashboardScope(query.TenantId, assignedTo, currency), periods, ct);

        return Result.Success(
            new InvoicesDashboardSummaryResponse(
                currency,
                zone.Id,
                nowUtc,
                assignedTo is null ? OfficeScope : AssignedScope,
                Revenue(periods, totals),
                Aging(periods, zone, totals)
            )
        );
    }

    // Consultas en serie: comparten el DbContext del handler.
    private static async Task<DashboardTotals> SumAsync(
        IInvoiceDashboardReader reader,
        InvoiceDashboardScope scope,
        InvoiceDashboardPeriods periods,
        CancellationToken ct
    ) =>
        new(
            await reader.CollectedByRangeAsync(scope, periods.CollectedBoundsUtc, ct),
            await reader.InvoicedByRangeAsync(scope, periods.InvoicedBoundsUtc, ct),
            await reader.AgingAsync(scope, periods.Aging, ct),
            await reader.OldestOpenAsync(scope, OldestCount, ct)
        );

    private static DashboardRevenue Revenue(InvoiceDashboardPeriods periods, DashboardTotals totals)
    {
        var collected = totals.Collected;
        var days = collected.Skip(InvoiceDashboardPeriods.FirstDayRange).ToList();
        var thisMonth = days.Sum();
        long[] monthly =
        [
            collected[0],
            collected[1],
            collected[2],
            collected[3],
            collected[InvoiceDashboardPeriods.PreviousMonthToDateRange] + collected[5],
            thisMonth,
        ];
        var monthStart = new DateOnly(periods.Today.Year, periods.Today.Month, 1);

        return new DashboardRevenue(
            thisMonth,
            collected[InvoiceDashboardPeriods.PreviousMonthToDateRange],
            totals.Invoiced[1],
            [.. periods.MonthKeys.Select((month, i) => new DashboardRevenueMonth(month, monthly[i]))],
            [.. days.Select((cents, i) => new DashboardRevenueDay(Key(monthStart.AddDays(i)), cents))],
            new DashboardCollectionRate(totals.Invoiced.Sum(), collected.Sum())
        );
    }

    private static DashboardAging Aging(InvoiceDashboardPeriods periods, ResolvedTimeZone zone, DashboardTotals totals)
    {
        var aging = totals.Aging;
        return new DashboardAging(
            aging.OutstandingCents,
            aging.OpenCount,
            aging.PastDueCount,
            [
                new DashboardAgingBucket(0, 30, aging.UpTo30Cents, aging.UpTo30Count),
                new DashboardAgingBucket(31, 60, aging.Days31To60Cents, aging.Days31To60Count),
                new DashboardAgingBucket(61, 90, aging.Days61To90Cents, aging.Days61To90Count),
                new DashboardAgingBucket(91, null, aging.Over90Cents, aging.Over90Count),
            ],
            [
                .. totals.Oldest.Select(row => new DashboardAgingInvoice(
                    row.InvoiceId,
                    row.InvoiceNumber,
                    row.CustomerId,
                    row.DueCents,
                    // Días civiles de la oficina; la que aún no vence cuenta 0.
                    Math.Max(0, periods.Today.DayNumber - CivilDate.Of(row.BaseDateUtc, zone).DayNumber)
                )),
            ]
        );
    }

    private static string Key(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private sealed record DashboardTotals(
        IReadOnlyList<long> Collected,
        IReadOnlyList<long> Invoiced,
        InvoiceAgingTotals Aging,
        IReadOnlyList<OpenInvoiceRow> Oldest
    )
    {
        public static DashboardTotals Empty(InvoiceDashboardPeriods periods) =>
            new(
                new long[periods.CollectedBoundsUtc.Count - 1],
                new long[periods.InvoicedBoundsUtc.Count - 1],
                InvoiceAgingTotals.Empty,
                []
            );
    }
}

/// <summary>
/// Cortes de mes y día en la zona de la oficina, enviados a SQL como instantes UTC.
/// <para><see cref="CollectedBoundsUtc"/> parte la ventana en tramos contiguos: 0-3 los meses -5 a -2, 4 el mes
/// anterior hasta el mismo día de hoy (o su último día), 5 el resto de ese mes y desde 6 un tramo por día del mes en
/// curso hasta hoy. <see cref="InvoicedBoundsUtc"/>: los 5 meses previos y el mes en curso hasta hoy.</para>
/// </summary>
public sealed record InvoiceDashboardPeriods(
    DateOnly Today,
    IReadOnlyList<string> MonthKeys,
    IReadOnlyList<DateTime> CollectedBoundsUtc,
    IReadOnlyList<DateTime> InvoicedBoundsUtc,
    InvoiceAgingCutoffs Aging
)
{
    public const int Months = 6;
    public const int PreviousMonthToDateRange = 4;
    public const int FirstDayRange = 6;

    public static InvoiceDashboardPeriods For(DateTime nowUtc, ResolvedTimeZone zone)
    {
        var today = CivilDate.Of(nowUtc, zone);
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var first = monthStart.AddMonths(-(Months - 1));
        var previous = monthStart.AddMonths(-1);
        var sameDay = previous.AddDays(Math.Min(today.Day, DateTime.DaysInMonth(previous.Year, previous.Month)) - 1);
        DateTime Start(DateOnly day) => LocalDay.StartUtc(day, zone);

        var collected = new List<DateTime>();
        for (var month = 0; month < Months - 1; month++)
            collected.Add(Start(first.AddMonths(month)));
        collected.Add(Start(sameDay.AddDays(1)));
        for (var day = 0; day <= today.Day; day++)
            collected.Add(Start(monthStart.AddDays(day)));

        return new InvoiceDashboardPeriods(
            today,
            [
                .. Enumerable
                    .Range(0, Months)
                    .Select(m => first.AddMonths(m).ToString("yyyy-MM", CultureInfo.InvariantCulture)),
            ],
            collected,
            [Start(first), Start(monthStart), Start(today.AddDays(1))],
            new InvoiceAgingCutoffs(
                PastDueBeforeUtc(nowUtc, zone),
                Start(today.AddDays(-30)),
                Start(today.AddDays(-60)),
                Start(today.AddDays(-90))
            )
        );
    }

    /// <summary>Corte de "vencida": inicio de hoy en la zona de la oficina (lo comparte el listado "past-due").</summary>
    public static DateTime PastDueBeforeUtc(DateTime nowUtc, ResolvedTimeZone zone) =>
        LocalDay.StartUtc(CivilDate.Of(nowUtc, zone), zone);
}
