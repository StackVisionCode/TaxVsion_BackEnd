using BuildingBlocks.TimeZones;
using Microsoft.Extensions.Options;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Application.Invoices.GetInvoicesDashboardSummary;
using TaxVision.Billing.Tests.Fakes;
using Xunit;

namespace TaxVision.Billing.Tests.Invoices;

/// <summary>
/// Resumen del dashboard: cortes de mes y día en la zona de la oficina, armado de la respuesta y alcance del actor.
/// Los filtros de SQL (reversados, crédito arrastrado, anuladas, moneda) los cubre la prueba de integración.
/// </summary>
public sealed class GetInvoicesDashboardSummaryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorUserId = Guid.NewGuid();
    private static readonly ResolvedTimeZone LaPaz = ResolvedTimeZone.Resolve("America/La_Paz");

    [Fact]
    public void The_month_is_the_office_month_even_after_utc_midnight()
    {
        // 1 de diciembre 02:00 UTC = 30 de noviembre 22:00 en La Paz (UTC-4).
        var periods = InvoiceDashboardPeriods.For(Utc(2026, 12, 1, 2), LaPaz);

        Assert.Equal(new DateOnly(2026, 11, 30), periods.Today);
        Assert.Equal(new[] { "2026-06", "2026-07", "2026-08", "2026-09", "2026-10", "2026-11" }, periods.MonthKeys);
        Assert.Equal(Utc(2026, 6, 1, 4), periods.CollectedBoundsUtc[0]);
        Assert.Equal(Utc(2026, 10, 1, 4), periods.CollectedBoundsUtc[4]);
        Assert.Equal(Utc(2026, 11, 1, 4), periods.CollectedBoundsUtc[6]);
        Assert.Equal(Utc(2026, 12, 1, 4), periods.CollectedBoundsUtc[^1]);
        // Seis tramos de mes y medio más uno por día del mes en curso.
        Assert.Equal(6 + 30, periods.CollectedBoundsUtc.Count - 1);
        Assert.Equal(new[] { Utc(2026, 6, 1, 4), Utc(2026, 11, 1, 4), Utc(2026, 12, 1, 4) }, periods.InvoicedBoundsUtc);
    }

    [Fact]
    public void The_previous_month_to_date_ends_on_the_same_day_or_its_last_day()
    {
        var mid = InvoiceDashboardPeriods.For(Utc(2026, 11, 15, 16), LaPaz);
        var endOfMarch = InvoiceDashboardPeriods.For(Utc(2027, 3, 31, 16), LaPaz);

        // Del 1 al 15 de octubre, inclusive.
        Assert.Equal(Utc(2026, 10, 16, 4), mid.CollectedBoundsUtc[5]);
        // Febrero no tiene 31: el tramo llega hasta su último día y el resto queda vacío.
        Assert.Equal(Utc(2027, 3, 1, 4), endOfMarch.CollectedBoundsUtc[5]);
        Assert.Equal(endOfMarch.CollectedBoundsUtc[6], endOfMarch.CollectedBoundsUtc[5]);
    }

    [Fact]
    public void Aging_cutoffs_count_office_civil_days()
    {
        var periods = InvoiceDashboardPeriods.For(Utc(2026, 11, 21, 15), LaPaz);

        // Vencida desde ayer o antes; más de 30 días = vencimiento el 21 de octubre o antes, en La Paz.
        Assert.Equal(Utc(2026, 11, 21, 4), periods.Aging.PastDueBeforeUtc);
        Assert.Equal(Utc(2026, 10, 22, 4), periods.Aging.Over30BeforeUtc);
        Assert.Equal(Utc(2026, 9, 22, 4), periods.Aging.Over60BeforeUtc);
        Assert.Equal(Utc(2026, 8, 23, 4), periods.Aging.Over90BeforeUtc);
    }

    [Fact]
    public async Task Totals_come_from_the_ranges_the_reader_sums()
    {
        var reader = new ScriptedReader
        {
            Collected = bounds => [.. Enumerable.Range(0, bounds.Count - 1).Select(i => (long)(i + 1) * 100)],
            Invoiced = _ => [50_000, 7_000],
            Aging = new InvoiceAgingTotals(9_000, 4, 2, 1_000, 1, 2_000, 1, 0, 0, 6_000, 2),
        };

        var summary = await HandleAsync(reader, Utc(2026, 11, 3, 15), canViewAll: true);

        var revenue = summary.Revenue;
        Assert.Equal("USD", summary.Currency);
        Assert.Equal("America/La_Paz", summary.TimeZoneId);
        // Tramos 0-3: junio-septiembre; 4: 1-3 de octubre; 5: resto de octubre; 6-8: 1, 2 y 3 de noviembre.
        Assert.Equal(500, revenue.CollectedPreviousMonthToDateCents);
        Assert.Equal(700 + 800 + 900, revenue.CollectedThisMonthCents);
        Assert.Equal(
            new long[] { 100, 200, 300, 400, 500 + 600, 2_400 },
            revenue.Monthly.Select(m => m.CollectedCents)
        );
        Assert.Equal("2026-11", revenue.Monthly[^1].Month);
        Assert.Equal(new[] { "2026-11-01", "2026-11-02", "2026-11-03" }, revenue.Daily.Select(d => d.Day));
        Assert.Equal(new long[] { 700, 800, 900 }, revenue.Daily.Select(d => d.CollectedCents));
        Assert.Equal(7_000, revenue.InvoicedThisMonthCents);
        Assert.Equal(new DashboardCollectionRate(57_000, 4_500), revenue.CollectionRate);
        Assert.Equal(9_000, summary.Aging.OutstandingCents);
        Assert.Equal(2, summary.Aging.PastDueCount);
        Assert.Equal(
            new DashboardAgingBucket[]
            {
                new(0, 30, 1_000, 1),
                new(31, 60, 2_000, 1),
                new(61, 90, 0, 0),
                new(91, null, 6_000, 2),
            },
            summary.Aging.Buckets
        );
    }

    [Fact]
    public async Task Oldest_invoices_report_office_civil_days_past_due()
    {
        var invoiceId = Guid.NewGuid();
        var reader = new ScriptedReader
        {
            Oldest =
            [
                // Vence el 21 de octubre a las 23:30 en La Paz: 31 días el 21 de noviembre.
                new(invoiceId, "INV-1", Guid.NewGuid(), 4_000, Utc(2026, 10, 22, 3, 30)),
                // Todavía no vence: 0, no un número negativo.
                new(Guid.NewGuid(), "INV-2", Guid.NewGuid(), 1_000, Utc(2026, 12, 5, 4)),
            ],
        };

        var summary = await HandleAsync(reader, Utc(2026, 11, 21, 15), canViewAll: true);

        Assert.Equal(new[] { 31, 0 }, summary.Aging.Oldest.Select(o => o.DaysPastDue));
        Assert.Equal(invoiceId, summary.Aging.Oldest[0].InvoiceId);
        Assert.Equal(GetInvoicesDashboardSummaryHandler.OldestCount, reader.OldestTake);
    }

    [Fact]
    public async Task A_restricted_actor_sums_only_their_assigned_clients()
    {
        var reader = new ScriptedReader();

        var summary = await HandleAsync(reader, Utc(2026, 11, 21, 15), canViewAll: false);

        Assert.Equal(ActorUserId, reader.Scope!.AssignedToUserId);
        Assert.Equal(ActorUserId, reader.CurrencyAssignedTo);
        Assert.Equal(GetInvoicesDashboardSummaryHandler.AssignedScope, summary.Scope);
    }

    [Fact]
    public async Task An_actor_who_sees_everything_sums_the_whole_office()
    {
        var reader = new ScriptedReader();

        var summary = await HandleAsync(reader, Utc(2026, 11, 21, 15), canViewAll: true);

        Assert.Null(reader.Scope!.AssignedToUserId);
        Assert.Equal(GetInvoicesDashboardSummaryHandler.OfficeScope, summary.Scope);
    }

    [Fact]
    public async Task Every_sum_uses_the_single_currency_of_the_latest_invoice()
    {
        var reader = new ScriptedReader { Currency = "DOP" };

        var summary = await HandleAsync(reader, Utc(2026, 11, 21, 15), canViewAll: true);

        Assert.Equal("DOP", summary.Currency);
        Assert.Equal("DOP", reader.Scope!.Currency);
    }

    [Fact]
    public async Task Without_invoices_the_summary_is_empty_and_nothing_is_summed()
    {
        var reader = new ScriptedReader { Currency = null };

        var summary = await HandleAsync(reader, Utc(2026, 11, 21, 15), canViewAll: true);

        Assert.Null(summary.Currency);
        Assert.Null(reader.Scope);
        Assert.Equal(6, summary.Revenue.Monthly.Count);
        Assert.Equal(21, summary.Revenue.Daily.Count);
        Assert.Equal(4, summary.Aging.Buckets.Count);
        Assert.Empty(summary.Aging.Oldest);
    }

    private static async Task<InvoicesDashboardSummaryResponse> HandleAsync(
        ScriptedReader reader,
        DateTime nowUtc,
        bool canViewAll
    )
    {
        var result = await GetInvoicesDashboardSummaryHandler.Handle(
            new GetInvoicesDashboardSummaryQuery(TenantId, ActorUserId, canViewAll),
            reader,
            new FixedOfficeZone(LaPaz),
            new FixedClock(nowUtc),
            Options.Create(new BillingVisibilityOptions { Enabled = true }),
            CancellationToken.None
        );
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private static DateTime Utc(int year, int month, int day, int hour, int minute = 0) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Utc);

    private sealed class FixedOfficeZone(ResolvedTimeZone zone) : IOfficeTimeZone
    {
        public Task<ResolvedTimeZone> ResolveAsync(Guid tenantId, CancellationToken ct) => Task.FromResult(zone);
    }

    private sealed class ScriptedReader : IInvoiceDashboardReader
    {
        public string? Currency { get; init; } = "USD";
        public Func<IReadOnlyList<DateTime>, IReadOnlyList<long>>? Collected { get; init; }
        public Func<IReadOnlyList<DateTime>, IReadOnlyList<long>>? Invoiced { get; init; }
        public InvoiceAgingTotals Aging { get; init; } = InvoiceAgingTotals.Empty;
        public IReadOnlyList<OpenInvoiceRow> Oldest { get; init; } = [];

        public InvoiceDashboardScope? Scope { get; private set; }
        public Guid? CurrencyAssignedTo { get; private set; }
        public int OldestTake { get; private set; }

        public Task<string?> LatestCurrencyAsync(Guid tenantId, Guid? assignedToUserId, CancellationToken ct)
        {
            CurrencyAssignedTo = assignedToUserId;
            return Task.FromResult(Currency);
        }

        public Task<IReadOnlyList<long>> CollectedByRangeAsync(
            InvoiceDashboardScope scope,
            IReadOnlyList<DateTime> boundsUtc,
            CancellationToken ct
        )
        {
            Scope = scope;
            return Task.FromResult(Collected?.Invoke(boundsUtc) ?? Zeros(boundsUtc));
        }

        public Task<IReadOnlyList<long>> InvoicedByRangeAsync(
            InvoiceDashboardScope scope,
            IReadOnlyList<DateTime> boundsUtc,
            CancellationToken ct
        ) => Task.FromResult(Invoiced?.Invoke(boundsUtc) ?? Zeros(boundsUtc));

        public Task<InvoiceAgingTotals> AgingAsync(
            InvoiceDashboardScope scope,
            InvoiceAgingCutoffs cutoffs,
            CancellationToken ct
        ) => Task.FromResult(Aging);

        public Task<IReadOnlyList<OpenInvoiceRow>> OldestOpenAsync(
            InvoiceDashboardScope scope,
            int take,
            CancellationToken ct
        )
        {
            OldestTake = take;
            return Task.FromResult(Oldest);
        }

        private static IReadOnlyList<long> Zeros(IReadOnlyList<DateTime> bounds) => new long[bounds.Count - 1];
    }
}
