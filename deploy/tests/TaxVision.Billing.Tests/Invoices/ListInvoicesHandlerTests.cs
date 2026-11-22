using BuildingBlocks.TimeZones;
using Microsoft.Extensions.Options;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Application.Invoices.ListInvoices;
using TaxVision.Billing.Domain.Invoices;
using TaxVision.Billing.Tests.Fakes;
using Xunit;

namespace TaxVision.Billing.Tests.Invoices;

/// <summary>
/// El listado tiene tope (un <c>take</c> enorme no vuelca todas las facturas de la oficina) y los filtros del
/// dashboard: "open" y "past-due" piden las pendientes del aging, vencidas antes de hoy en la zona de la oficina.
/// </summary>
public sealed class ListInvoicesHandlerTests
{
    private static readonly ResolvedTimeZone LaPaz = ResolvedTimeZone.Resolve("America/La_Paz");

    [Theory]
    [InlineData(1_000_000, 500)]
    [InlineData(200, 200)]
    [InlineData(0, 0)]
    public async Task Take_is_capped_at_500(int requested, int expected)
    {
        var repository = new CapturingRepository();

        await HandleAsync(repository, new ListInvoicesQuery(Guid.NewGuid(), Guid.NewGuid(), true, Take: requested));

        // 0 sigue llegando como 0: el repositorio lo convierte en su valor por defecto (50).
        Assert.Equal(expected, repository.Take);
    }

    [Fact]
    public async Task Without_status_no_open_filter_is_asked()
    {
        var repository = new CapturingRepository();

        await HandleAsync(repository, new ListInvoicesQuery(Guid.NewGuid(), Guid.NewGuid(), true));

        Assert.True(repository.Called);
        Assert.Null(repository.Open);
    }

    [Fact]
    public async Task Open_asks_for_the_open_invoices_without_a_cutoff()
    {
        var repository = new CapturingRepository();

        await HandleAsync(
            repository,
            new ListInvoicesQuery(Guid.NewGuid(), Guid.NewGuid(), true, Status: InvoiceListStatus.Open)
        );

        Assert.Equal(new OpenInvoicesFilter(), repository.Open);
    }

    [Fact]
    public async Task Past_due_cuts_at_the_start_of_the_office_today()
    {
        var repository = new CapturingRepository();

        // 21 de noviembre 02:00 UTC = 20 de noviembre 22:00 en La Paz: hoy es el 20 para la oficina.
        await HandleAsync(
            repository,
            new ListInvoicesQuery(Guid.NewGuid(), Guid.NewGuid(), true, Status: InvoiceListStatus.PastDue),
            new DateTime(2026, 11, 21, 2, 0, 0, DateTimeKind.Utc)
        );

        Assert.Equal(new OpenInvoicesFilter(new DateTime(2026, 11, 20, 4, 0, 0, DateTimeKind.Utc)), repository.Open);
    }

    [Theory]
    [InlineData("open", InvoiceListStatus.Open)]
    [InlineData("past-due", InvoiceListStatus.PastDue)]
    [InlineData(" Past-Due ", InvoiceListStatus.PastDue)]
    [InlineData("overdue", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Status_parses_the_url_values_and_ignores_the_rest(string? value, InvoiceListStatus? expected) =>
        Assert.Equal(expected, InvoiceListStatuses.Parse(value));

    private static Task HandleAsync(CapturingRepository repository, ListInvoicesQuery query, DateTime? nowUtc = null) =>
        ListInvoicesHandler.Handle(
            query,
            repository,
            Options.Create(new BillingVisibilityOptions()),
            new FixedOfficeZone(LaPaz),
            new FixedClock(nowUtc ?? DateTime.UtcNow),
            CancellationToken.None
        );

    private sealed class FixedOfficeZone(ResolvedTimeZone zone) : IOfficeTimeZone
    {
        public Task<ResolvedTimeZone> ResolveAsync(Guid tenantId, CancellationToken ct) => Task.FromResult(zone);
    }

    private sealed class CapturingRepository : IInvoiceRepository
    {
        public int? Take { get; private set; }
        public bool Called { get; private set; }
        public OpenInvoicesFilter? Open { get; private set; }

        public Task<Invoice?> GetByIdAsync(
            Guid tenantId,
            Guid invoiceId,
            CancellationToken ct = default,
            Guid? assignedToUserId = null
        ) => Task.FromResult<Invoice?>(null);

        public Task<IReadOnlyList<Invoice>> ListByTenantAsync(
            Guid tenantId,
            int take,
            CancellationToken ct = default,
            Guid? assignedToUserId = null,
            Guid? customerId = null,
            OpenInvoicesFilter? open = null
        )
        {
            (Take, Called, Open) = (take, true, open);
            return Task.FromResult<IReadOnlyList<Invoice>>([]);
        }

        public Task AddAsync(Invoice invoice, CancellationToken ct = default) => Task.CompletedTask;
    }
}
