using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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
    private readonly string _connectionString = PaymentAppWolverineHost.Connection;

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
            + "DELETE FROM dbo.RefundRequests WHERE TenantId = @tenant; "
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
        // Primer guardado: la intención (antes del proveedor). Segundo: el registro, que falla una vez.
        var interceptor = new ThrowOnSaveInterceptor(failOnCall: 2);
        var actor = Guid.NewGuid();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            HandleAsync(payment.Id, provider, actor, interceptor)
        );
        var result = await HandleAsync(payment.Id, provider, actor, interceptor);
        Assert.True(result.IsSuccess);

        Assert.Equal(1, provider.EffectiveRefunds);
        Assert.Equal(2, provider.Calls);
        await using var read = CreateContext();
        Assert.Equal(
            1,
            await read.Set<RefundLine>().IgnoreQueryFilters().CountAsync(r => r.SaaSPaymentId == payment.Id)
        );
    }

    [Fact]
    public async Task IdenticalRequestWithoutHeaderAfterCommit_RefundsOnce()
    {
        var payment = SucceededPayment();
        await using (var seed = CreateContext())
        {
            seed.SaaSPayments.Add(payment);
            await seed.SaveChangesAsync();
        }

        var provider = new IdempotentRefundProvider();
        var actor = Guid.NewGuid();

        // Dos peticiones iguales; la primera ya confirmada.
        Assert.True((await HandleAsync(payment.Id, provider, actor)).IsSuccess);
        Assert.True((await HandleAsync(payment.Id, provider, actor)).IsSuccess);

        Assert.Equal(1, provider.EffectiveRefunds);
        await using var read = CreateContext();
        Assert.Equal(
            1,
            await read.Set<RefundLine>().IgnoreQueryFilters().CountAsync(r => r.SaaSPaymentId == payment.Id)
        );
    }

    // Cada paso con su propio contexto, como el scope de cada InvokeAsync.
    private Task<Result> HandleAsync(
        Guid paymentId,
        IdempotentRefundProvider provider,
        Guid actor,
        IInterceptor? interceptor = null
    )
    {
        var contexts = new List<PaymentAppDbContext>();
        var adapters = new FakePaymentAdapterFactory(provider);
        var bus = TestRefundExecution.Bus(
            () =>
            {
                var db = CreateContext(interceptor);
                contexts.Add(db);
                return (new SaaSPaymentRepository(db), db);
            },
            adapters
        );
        return RefundSaaSPaymentHandler.Handle(
            new RefundSaaSPaymentCommand(paymentId, 1000, "customer request", actor),
            bus,
            adapters,
            new NoAdminThrottle(),
            TestRefundExecution.RefundOptions(),
            NullLogger<SaaSPayment>.Instance,
            CancellationToken.None
        );
    }

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

    // Falla un solo guardado, como una caída justo después de que el proveedor aceptó.
    private sealed class ThrowOnSaveInterceptor(int failOnCall) : SaveChangesInterceptor
    {
        private int _calls;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        ) =>
            Interlocked.Increment(ref _calls) == failOnCall
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

    private sealed class TestTenantContext : ITenantContext
    {
        private Guid? _tenantId;
        public Guid TenantId => _tenantId ?? Guid.Empty;
        public bool HasTenant => _tenantId.HasValue;

        public void SetTenant(Guid tenantId) => _tenantId = tenantId;
    }
}
