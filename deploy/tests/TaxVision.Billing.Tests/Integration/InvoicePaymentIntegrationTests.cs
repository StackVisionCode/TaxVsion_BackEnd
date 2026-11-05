using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TaxVision.Billing.Domain.Invoices;
using TaxVision.Billing.Domain.ValueObjects;
using TaxVision.Billing.Infrastructure.Persistence;
using TaxVision.Billing.Infrastructure.Persistence.Repositories;
using Xunit;

namespace TaxVision.Billing.Tests.Integration;

/// <summary>
/// Contra el SQL Server real (BILLING_DB_CONNECTION o user-secrets de Billing): dos contextos aplican
/// pagos sobre la misma factura. El RowVersion de la factura y el índice único de InvoicePayments
/// impiden que un pago se pierda o se aplique dos veces.
/// </summary>
public sealed class InvoicePaymentIntegrationTests : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);
    private readonly Guid _tenantId = Guid.NewGuid();
    private Guid _invoiceId;

    private readonly string _connectionString =
        Environment.GetEnvironmentVariable("BILLING_DB_CONNECTION")
        ?? new ConfigurationBuilder()
            .AddUserSecrets("e4b1c9d2-7f3a-4a86-9b2d-billing000001")
            .Build()
            .GetConnectionString("Default")
        ?? throw new InvalidOperationException("Set BILLING_DB_CONNECTION or the Billing user-secrets to run this test.");

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => true;

        public void SetTenant(Guid id) { }
    }

    private BillingDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<BillingDbContext>().UseSqlServer(_connectionString).Options,
            new FixedTenant(_tenantId)
        );

    public async Task InitializeAsync()
    {
        var invoice = Invoice
            .CreateDraft(
                _tenantId,
                Guid.NewGuid(),
                new CustomerSnapshot(Guid.NewGuid(), "Client", "client@example.com", null, null, null),
                "USD",
                [new DraftInvoiceLine("Tax preparation", 1, 15_000, 0)],
                null,
                Now
            )
            .Value;
        Assert.True(invoice.Issue("INV-2026-00001", Now, Now.AddDays(30), Guid.NewGuid()).IsSuccess);
        _invoiceId = invoice.Id;

        await using var db = NewContext();
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var db = NewContext();
        // Cascada a pagos, líneas y rastro de estado: solo los datos de este tenant de prueba.
        await db.Invoices.IgnoreQueryFilters().Where(i => i.TenantId == _tenantId).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task SameTenantPaymentIdFromTwoContexts_OnlyOneRow()
    {
        var paymentId = Guid.NewGuid().ToString();
        await using var first = NewContext();
        await using var second = NewContext();
        var a = await LoadAsync(first);
        var b = await LoadAsync(second);

        Assert.True(Apply(a, paymentId, 5_000).IsSuccess);
        Assert.True(Apply(b, paymentId, 5_000).IsSuccess);
        await first.SaveChangesAsync();
        await Assert.ThrowsAsync<ConflictException>(() => second.SaveChangesAsync());

        // Reintento del perdedor (lo que hace Wolverine): ve el pago ya aplicado y no hace nada.
        await using var retry = NewContext();
        var c = await LoadAsync(retry);
        Assert.True(Apply(c, paymentId, 5_000).IsSuccess);
        await retry.SaveChangesAsync();

        var (paidCents, rows) = await ReadAsync();
        Assert.Equal(5_000, paidCents);
        Assert.Equal(1, rows);
    }

    [Fact]
    public async Task TwoDistinctPaymentsInParallel_AfterRetry_BothApplied()
    {
        await using var first = NewContext();
        await using var second = NewContext();
        var a = await LoadAsync(first);
        var b = await LoadAsync(second);

        Assert.True(Apply(a, "payment-a", 5_000).IsSuccess);
        Assert.True(Apply(b, "payment-b", 10_000).IsSuccess);
        await first.SaveChangesAsync();
        await Assert.ThrowsAsync<ConflictException>(() => second.SaveChangesAsync());

        await using var retry = NewContext();
        var c = await LoadAsync(retry);
        Assert.True(Apply(c, "payment-b", 10_000).IsSuccess);
        await retry.SaveChangesAsync();

        var (paidCents, rows) = await ReadAsync();
        Assert.Equal(15_000, paidCents);
        Assert.Equal(2, rows);
    }

    private async Task<Invoice> LoadAsync(BillingDbContext db) =>
        (await new InvoiceRepository(db).GetByIdAsync(_tenantId, _invoiceId))!;

    private static Result Apply(Invoice invoice, string reference, long cents) =>
        invoice.ApplyPayment(
            InvoicePaymentSource.Online,
            reference,
            Money.Create(cents, "USD").Value,
            PaymentMethod.Online,
            Now,
            Guid.Empty
        );

    private async Task<(long PaidCents, int Rows)> ReadAsync()
    {
        await using var db = NewContext();
        var invoice = await new InvoiceRepository(db).GetByIdAsync(_tenantId, _invoiceId);
        var rows = await db.Set<InvoicePayment>().CountAsync(p => p.InvoiceId == _invoiceId);
        return (invoice!.AmountPaid.AmountCents, rows);
    }
}
