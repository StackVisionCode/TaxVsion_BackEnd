using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Application.SaaSPayments.Commands.RefundSaaSPayment;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;
using TaxVision.PaymentApp.Infrastructure.Persistence;
using TaxVision.PaymentApp.Infrastructure.Persistence.Repositories;
using TaxVision.PaymentApp.Tests.Application;
using TaxVision.PaymentApp.Tests.TestDoubles;

namespace TaxVision.PaymentApp.Tests.Integration;

/// <summary>PAY-3 (patrón BB-8) contra el SQL Server local: el proveedor aceptó, falló el guardado y el
/// reintento no crea un segundo reembolso.</summary>
public sealed class RefundSaaSPaymentIntegrationTests : IAsyncLifetime
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
            "DELETE FROM dbo.RefundLines WHERE TenantId = @tenant; "
            + "DELETE a FROM dbo.SaaSPaymentAttempts a JOIN dbo.SaaSPayments p ON p.Id = a.SaaSPaymentId WHERE p.TenantId = @tenant; "
            + "DELETE FROM dbo.SaaSPayments WHERE TenantId = @tenant;";
        command.Parameters.AddWithValue("@tenant", _tenantId);
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task FailureAfterProviderAccepted_DoesNotResend()
    {
        var payment = SucceededPayment();
        await using (var seed = CreateContext())
        {
            seed.SaaSPayments.Add(payment);
            await seed.SaveChangesAsync();
        }

        var provider = new IdempotentRefundProvider();
        var interceptor = new ThrowOnceSaveInterceptor();

        await using (var first = CreateContext(interceptor))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => HandleAsync(first, payment.Id, provider));
        }

        await using (var retry = CreateContext(interceptor))
        {
            var result = await HandleAsync(retry, payment.Id, provider);
            Assert.True(result.IsSuccess);
        }

        Assert.Equal(1, provider.EffectiveRefunds);
        Assert.Equal(2, provider.Calls);
        await using var read = CreateContext();
        Assert.Equal(
            1,
            await read.Set<RefundLine>().IgnoreQueryFilters().CountAsync(r => r.SaaSPaymentId == payment.Id)
        );
    }

    private static Task<Result> HandleAsync(
        PaymentAppDbContext db,
        Guid paymentId,
        IdempotentRefundProvider provider
    ) =>
        RefundSaaSPaymentHandler.Handle(
            new RefundSaaSPaymentCommand(paymentId, 1000, "customer request", Guid.NewGuid()),
            new SaaSPaymentRepository(db),
            new FakePaymentAdapterFactory(provider),
            new FakePaymentAuditLogWriter(),
            db,
            new FakePaymentAppMetrics(),
            new NoThrottle(),
            new FakeCorrelationContext(),
            NullLogger<SaaSPayment>.Instance,
            CancellationToken.None
        );

    private SaaSPayment SucceededPayment()
    {
        var nowUtc = DateTime.UtcNow;
        var payment = SaaSPayment
            .Create(
                _tenantId,
                IdempotencyKey.Create($"pay3-refund-{Guid.NewGuid():N}").Value,
                Money.Create(4900, "USD").Value,
                SaaSPaymentType.SeatsPurchaseCharge,
                Guid.NewGuid(),
                PaymentProviderCode.Stripe,
                StatementDescriptor.Create("TAXVISION SAAS").Value,
                Guid.Empty,
                nowUtc
            )
            .Value;
        payment.MarkProcessing(
            ExternalPaymentReference.Create(PaymentProviderCode.Stripe, $"pi_pay3_{Guid.NewGuid():N}").Value,
            "processing",
            null,
            Guid.Empty,
            nowUtc
        );
        payment.MarkSucceeded(nowUtc, Guid.Empty);
        return payment;
    }

    private PaymentAppDbContext CreateContext(IInterceptor? interceptor = null)
    {
        var tenant = new TestTenantContext();
        tenant.SetTenant(_tenantId);
        var options = new DbContextOptionsBuilder<PaymentAppDbContext>().UseSqlServer(_connectionString);
        if (interceptor is not null)
            options.AddInterceptors(interceptor);
        return new PaymentAppDbContext(options.Options, tenant);
    }

    // Falla el primer guardado, como una caída justo después de que el proveedor aceptó.
    private sealed class ThrowOnceSaveInterceptor : SaveChangesInterceptor
    {
        private int _calls;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        ) =>
            Interlocked.Increment(ref _calls) == 1
                ? throw new InvalidOperationException("Simulated save failure.")
                : base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    // Honra la idempotencia como Stripe: la misma clave devuelve el mismo reembolso.
    private sealed class IdempotentRefundProvider : RefundRecordingProviderBase
    {
        private readonly Dictionary<string, string> _refundsByKey = [];
        public int EffectiveRefunds => _refundsByKey.Count;

        protected override string Refund(string idempotencyKey)
        {
            if (!_refundsByKey.TryGetValue(idempotencyKey, out var refundId))
                _refundsByKey[idempotencyKey] = refundId = $"re_{_refundsByKey.Count + 1}";
            return refundId;
        }
    }

    private sealed class NoThrottle : IPaymentAttemptThrottle
    {
        public Task<bool> IsWebhookThrottledAsync(Guid scopeId, CancellationToken ct = default) =>
            Task.FromResult(false);

        public Task RegisterWebhookAttemptAsync(Guid scopeId, CancellationToken ct = default) => Task.CompletedTask;

        public Task<bool> IsAdminActionThrottledAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(false);

        public Task RegisterAdminActionAttemptAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class TestTenantContext : ITenantContext
    {
        private Guid? _tenantId;
        public Guid TenantId => _tenantId ?? Guid.Empty;
        public bool HasTenant => _tenantId.HasValue;

        public void SetTenant(Guid tenantId) => _tenantId = tenantId;
    }
}
