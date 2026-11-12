using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Domain.Invoices;
using TaxVision.Billing.Domain.Payments;
using TaxVision.Billing.Domain.Settings;
using DomainIssuerProfile = TaxVision.Billing.Domain.Invoices.IssuerProfile;

namespace TaxVision.Billing.Tests.Fakes;

/// <summary>Perfiles de emisor en memoria (uno por tenant), como el repo real con IgnoreQueryFilters.</summary>
internal sealed class InMemoryIssuerProfileRepository : IIssuerProfileRepository
{
    public List<DomainIssuerProfile> Rows { get; } = [];

    public Task<DomainIssuerProfile?> GetByTenantAsync(Guid tenantId, CancellationToken ct = default) =>
        Task.FromResult(Rows.FirstOrDefault(p => p.TenantId == tenantId));

    public Task AddAsync(DomainIssuerProfile profile, CancellationToken ct = default)
    {
        Rows.Add(profile);
        return Task.CompletedTask;
    }

    public Task<(IReadOnlyList<DomainIssuerProfile> Items, int TotalCount)> ListPageAsync(
        int page,
        int size,
        CancellationToken ct = default
    )
    {
        IReadOnlyList<DomainIssuerProfile> items = Rows.OrderBy(p => p.TenantId)
            .Skip((page - 1) * size)
            .Take(size)
            .ToList();
        return Task.FromResult((items, Rows.Count));
    }
}

internal sealed class InMemoryTenantBillingSettingsRepository : ITenantBillingSettingsRepository
{
    public List<TenantBillingSettings> Rows { get; } = [];

    public Task<TenantBillingSettings?> GetByTenantAsync(Guid tenantId, CancellationToken ct = default) =>
        Task.FromResult(Rows.FirstOrDefault(s => s.TenantId == tenantId));

    public Task AddAsync(TenantBillingSettings settings, CancellationToken ct = default)
    {
        Rows.Add(settings);
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryUnappliedPaymentRepository : IUnappliedPaymentRepository
{
    public List<UnappliedPayment> Rows { get; } = [];

    public Task<bool> ExistsAsync(Guid tenantId, Guid tenantPaymentId, CancellationToken ct = default) =>
        Task.FromResult(Rows.Any(p => p.TenantId == tenantId && p.TenantPaymentId == tenantPaymentId));

    public Task<UnappliedPayment?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
        Task.FromResult(Rows.FirstOrDefault(p => p.TenantId == tenantId && p.Id == id));

    public Task<IReadOnlyList<UnappliedPayment>> ListByTenantAsync(
        Guid tenantId,
        bool includeResolved,
        int take,
        CancellationToken ct = default
    ) =>
        Task.FromResult<IReadOnlyList<UnappliedPayment>>(
            Rows.Where(p => p.TenantId == tenantId && (includeResolved || p.ResolvedAtUtc is null)).Take(take).ToList()
        );

    public Task AddAsync(UnappliedPayment payment, CancellationToken ct = default)
    {
        Rows.Add(payment);
        return Task.CompletedTask;
    }
}

internal sealed class CountingUnitOfWork : IUnitOfWork
{
    public int Saves { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        Saves++;
        return Task.FromResult(1);
    }
}

internal sealed class NoopCorrelationContext : ICorrelationContext
{
    public string CorrelationId { get; private set; } = string.Empty;

    public void Set(string correlationId) => CorrelationId = correlationId;

    public IDisposable Push(string correlationId)
    {
        CorrelationId = correlationId;
        return new Noop();
    }

    private sealed class Noop : IDisposable
    {
        public void Dispose() { }
    }
}

internal sealed class FixedClock(DateTime utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
}

/// <summary>Inventory falso: registra verificaciones y commits, y aplica el commit por delta como el real.</summary>
internal sealed class RecordingInventoryClient : IInventoryStockClient
{
    public List<(Guid InvoiceId, IReadOnlyList<InvoiceSaleLine> Lines)> Commits { get; } = [];
    public List<(Guid InvoiceId, IReadOnlyList<InvoiceSaleLine> Lines)> Checks { get; } = [];
    public Result CommitResult { get; set; } = Result.Success();
    public Result CheckResult { get; set; } = Result.Success();

    /// <summary>Cantidades descontadas por factura y cuántas veces cambiaron (idempotencia por delta).</summary>
    public Dictionary<Guid, Dictionary<Guid, int>> Discounted { get; } = [];
    public int Moves { get; private set; }

    public Task<Result> CommitInvoiceSaleAsync(
        Guid tenantId,
        Guid invoiceId,
        IReadOnlyList<InvoiceSaleLine> lines,
        CancellationToken ct = default
    )
    {
        Commits.Add((invoiceId, lines));
        if (CommitResult.IsFailure)
            return Task.FromResult(CommitResult);

        var desired = lines.GroupBy(l => l.CatalogItemId).ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));
        var current = Discounted.GetValueOrDefault(invoiceId) ?? [];
        if (!desired.OrderBy(p => p.Key).SequenceEqual(current.OrderBy(p => p.Key)))
            Moves++;
        Discounted[invoiceId] = desired;
        return Task.FromResult(Result.Success());
    }

    public Task<Result> CheckInvoiceSaleAsync(
        Guid tenantId,
        Guid invoiceId,
        IReadOnlyList<InvoiceSaleLine> lines,
        CancellationToken ct = default
    )
    {
        Checks.Add((invoiceId, lines));
        return Task.FromResult(CheckResult);
    }
}

internal sealed class RecordingBillingMetrics : IBillingMetrics
{
    public int StockSyncInsufficient { get; private set; }

    public void RecordOverpayment() { }

    public void RecordPaymentRejected(string reason) { }

    public void RecordStockSyncInsufficient() => StockSyncInsufficient++;
}

/// <summary>Facturas en memoria por Id.</summary>
internal sealed class InMemoryInvoiceRepository(params Invoice[] seed) : IInvoiceRepository
{
    public List<Invoice> Rows { get; } = [.. seed];

    public Task<Invoice?> GetByIdAsync(
        Guid tenantId,
        Guid invoiceId,
        CancellationToken ct = default,
        Guid? assignedToUserId = null
    ) => Task.FromResult(Rows.FirstOrDefault(i => i.Id == invoiceId));

    public Task<IReadOnlyList<Invoice>> ListByTenantAsync(
        Guid tenantId,
        int take,
        CancellationToken ct = default,
        Guid? assignedToUserId = null,
        Guid? customerId = null
    ) => Task.FromResult<IReadOnlyList<Invoice>>(Rows.Take(take).ToList());

    public Task AddAsync(Invoice value, CancellationToken ct = default)
    {
        Rows.Add(value);
        return Task.CompletedTask;
    }
}
