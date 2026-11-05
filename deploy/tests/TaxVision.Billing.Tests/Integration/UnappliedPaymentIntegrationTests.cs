using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TaxVision.Billing.Domain.Payments;
using TaxVision.Billing.Infrastructure.Persistence;
using TaxVision.Billing.Infrastructure.Persistence.Repositories;
using Xunit;

namespace TaxVision.Billing.Tests.Integration;

/// <summary>
/// Contra el SQL Server real (BILLING_DB_CONNECTION o user-secrets de Billing): el índice único
/// (TenantId, TenantPaymentId) impide registrar dos veces el mismo cobro no aplicado.
/// </summary>
public sealed class UnappliedPaymentIntegrationTests : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);
    private readonly Guid _tenantId = Guid.NewGuid();

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

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var db = NewContext();
        await db.UnappliedPayments.IgnoreQueryFilters().Where(p => p.TenantId == _tenantId).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task SameTenantPaymentIdTwice_OneRow()
    {
        var paymentId = Guid.NewGuid();
        await using var first = NewContext();
        await using var second = NewContext();
        var a = new UnappliedPaymentRepository(first);
        var b = new UnappliedPaymentRepository(second);

        // Los dos ven que no existe (carrera entre dos entregas del mismo evento).
        Assert.False(await a.ExistsAsync(_tenantId, paymentId));
        Assert.False(await b.ExistsAsync(_tenantId, paymentId));
        await a.AddAsync(NewPayment(paymentId));
        await b.AddAsync(NewPayment(paymentId));

        await first.SaveChangesAsync();
        await Assert.ThrowsAsync<ConflictException>(() => second.SaveChangesAsync());

        await using var read = NewContext();
        var rows = await read
            .UnappliedPayments.IgnoreQueryFilters()
            .CountAsync(p => p.TenantId == _tenantId && p.TenantPaymentId == paymentId);
        Assert.Equal(1, rows);
        Assert.True(await new UnappliedPaymentRepository(read).ExistsAsync(_tenantId, paymentId));
    }

    private UnappliedPayment NewPayment(Guid paymentId) =>
        UnappliedPayment.Record(
            _tenantId,
            paymentId,
            Guid.NewGuid(),
            Guid.NewGuid().ToString(),
            10_000,
            "USD",
            UnappliedPaymentReason.InvoiceVoided,
            Now
        );
}
