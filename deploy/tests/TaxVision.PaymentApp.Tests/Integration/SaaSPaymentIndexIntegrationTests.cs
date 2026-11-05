using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;
using TaxVision.PaymentApp.Infrastructure.Persistence;

namespace TaxVision.PaymentApp.Tests.Integration;

/// <summary>PAY-4 contra el SQL Server local: la referencia del proveedor no se repite y las nulas no chocan.</summary>
public sealed class SaaSPaymentIndexIntegrationTests : IAsyncLifetime
{
    private readonly string _connectionString =
        Environment.GetEnvironmentVariable("PAYMENTAPP_DB_CONNECTION")
        ?? new ConfigurationBuilder()
            .AddUserSecrets("e4b1c9d2-7f3a-4a86-9b2d-paymentapp001")
            .Build()
            .GetConnectionString("Default")
        ?? throw new InvalidOperationException(
            "Set PAYMENTAPP_DB_CONNECTION or the PaymentApp user-secrets to run this test."
        );

    private readonly Guid _tenantId = Guid.NewGuid();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        // Solo las filas de este tenant.
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "DELETE a FROM dbo.SaaSPaymentAttempts a JOIN dbo.SaaSPayments p ON p.Id = a.SaaSPaymentId WHERE p.TenantId = @tenant; "
            + "DELETE FROM dbo.SaaSPayments WHERE TenantId = @tenant;";
        command.Parameters.AddWithValue("@tenant", _tenantId);
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Duplicate_external_charge_reference_is_rejected()
    {
        var reference = $"pi_dup_{Guid.NewGuid():N}";

        await using (var first = CreateContext())
        {
            first.SaaSPayments.Add(Processing(reference));
            await first.SaveChangesAsync();
        }

        await using var second = CreateContext();
        second.SaaSPayments.Add(Processing(reference));
        await Assert.ThrowsAsync<ConflictException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task Duplicate_checkout_session_is_rejected()
    {
        var session = $"cs_dup_{Guid.NewGuid():N}";

        await using (var first = CreateContext())
        {
            first.SaaSPayments.Add(HostedCheckout(session, $"pi_a_{Guid.NewGuid():N}"));
            await first.SaveChangesAsync();
        }

        await using var second = CreateContext();
        second.SaaSPayments.Add(HostedCheckout(session, $"pi_b_{Guid.NewGuid():N}"));
        await Assert.ThrowsAsync<ConflictException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task Null_references_do_not_collide()
    {
        await using (var first = CreateContext())
        {
            first.SaaSPayments.Add(Pending());
            await first.SaveChangesAsync();
        }

        await using (var second = CreateContext())
        {
            second.SaaSPayments.Add(Pending());
            await second.SaveChangesAsync();
        }

        await using var read = CreateContext();
        Assert.Equal(2, await read.SaaSPayments.IgnoreQueryFilters().CountAsync(p => p.TenantId == _tenantId));
    }

    private SaaSPayment Pending() =>
        SaaSPayment
            .Create(
                _tenantId,
                IdempotencyKey.Create($"pay4-index-{Guid.NewGuid():N}").Value,
                Money.Create(4900, "USD").Value,
                SaaSPaymentType.SeatsPurchaseCharge,
                Guid.NewGuid(),
                PaymentProviderCode.Stripe,
                StatementDescriptor.Create("TAXVISION SAAS").Value,
                Guid.Empty,
                DateTime.UtcNow
            )
            .Value;

    private SaaSPayment Processing(string reference)
    {
        var payment = Pending();
        payment.MarkProcessing(
            ExternalPaymentReference.Create(PaymentProviderCode.Stripe, reference).Value,
            "processing",
            null,
            Guid.Empty,
            DateTime.UtcNow
        );
        return payment;
    }

    private SaaSPayment HostedCheckout(string session, string reference)
    {
        var payment = Pending();
        payment.RecordHostedCheckoutSession(
            session,
            ExternalPaymentReference.Create(PaymentProviderCode.Stripe, reference).Value,
            "https://checkout.stripe.test/session",
            DateTime.UtcNow
        );
        return payment;
    }

    private PaymentAppDbContext CreateContext()
    {
        var tenant = new TestTenantContext();
        tenant.SetTenant(_tenantId);
        return new PaymentAppDbContext(
            new DbContextOptionsBuilder<PaymentAppDbContext>().UseSqlServer(_connectionString).Options,
            tenant
        );
    }

    private sealed class TestTenantContext : ITenantContext
    {
        private Guid? _tenantId;
        public Guid TenantId => _tenantId ?? Guid.Empty;
        public bool HasTenant => _tenantId.HasValue;

        public void SetTenant(Guid tenantId) => _tenantId = tenantId;
    }
}
