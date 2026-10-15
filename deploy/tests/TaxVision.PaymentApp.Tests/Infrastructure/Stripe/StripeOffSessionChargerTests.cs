using Microsoft.Extensions.Logging.Abstractions;
using Stripe;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Domain.ProviderCustomers;
using TaxVision.PaymentApp.Domain.ValueObjects;
using TaxVision.PaymentApp.Infrastructure.Providers.Stripe;
using TaxVision.PaymentApp.Tests.TestDoubles;

namespace TaxVision.PaymentApp.Tests.Infrastructure.Stripe;

/// <summary>
/// Verifica el mapeo del PaymentIntent de Stripe a <see cref="OffSessionChargeOutcome"/>:
/// succeeded, requires_action (SCA), fallo genérico y <see cref="StripeException"/> con
/// <c>authentication_required</c>.
/// </summary>
public sealed class StripeOffSessionChargerTests
{
    [Fact]
    public async Task Succeeded_intent_maps_to_Succeeded_with_intent_id()
    {
        var charger = BuildCharger(
            (opts, _, _) => Task.FromResult(new PaymentIntent { Id = "pi_ok", Status = "succeeded" })
        );

        var result = await charger.ChargeAsync(SampleRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(OffSessionChargeStatus.Succeeded, result.Value.Status);
        Assert.Equal("pi_ok", result.Value.ProviderPaymentReference);
    }

    [Fact]
    public async Task Requires_action_intent_maps_to_RequiresUserAction()
    {
        var charger = BuildCharger(
            (_, _, _) =>
                Task.FromResult(
                    new PaymentIntent
                    {
                        Id = "pi_sca",
                        Status = "requires_action",
                        LastPaymentError = new StripeError { Code = "authentication_required" },
                    }
                )
        );

        var result = await charger.ChargeAsync(SampleRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(OffSessionChargeStatus.RequiresUserAction, result.Value.Status);
        Assert.Equal("pi_sca", result.Value.ProviderPaymentReference);
        Assert.Equal("authentication_required", result.Value.FailureCode);
    }

    [Fact]
    public async Task Authentication_required_exception_maps_to_RequiresUserAction()
    {
        var ex = new StripeException(
            System.Net.HttpStatusCode.PaymentRequired,
            new StripeError
            {
                Code = "authentication_required",
                Message = "The customer must authenticate the payment.",
                PaymentIntent = new PaymentIntent { Id = "pi_requires_sca" },
            },
            "sca"
        );
        var charger = BuildCharger((_, _, _) => Task.FromException<PaymentIntent>(ex));

        var result = await charger.ChargeAsync(SampleRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(OffSessionChargeStatus.RequiresUserAction, result.Value.Status);
        Assert.Equal("authentication_required", result.Value.FailureCode);
        Assert.Equal("pi_requires_sca", result.Value.ProviderPaymentReference);
    }

    [Fact]
    public async Task Generic_stripe_exception_maps_to_Failed()
    {
        var ex = new StripeException(
            System.Net.HttpStatusCode.PaymentRequired,
            new StripeError { Code = "card_declined", Message = "Your card was declined." },
            "declined"
        );
        var charger = BuildCharger((_, _, _) => Task.FromException<PaymentIntent>(ex));

        var result = await charger.ChargeAsync(SampleRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(OffSessionChargeStatus.Failed, result.Value.Status);
        Assert.Equal("card_declined", result.Value.FailureCode);
    }

    [Fact]
    public async Task Missing_tenant_customer_returns_CustomerMissing_error()
    {
        var charger = new StripeOffSessionCharger(
            (_, _, _) => Task.FromResult(new PaymentIntent { Status = "succeeded", Id = "pi" }),
            new InMemoryTenantProviderCustomerRepository(seeded: null),
            NullLogger<StripeOffSessionCharger>.Instance,
            new FakePaymentAppMetrics()
        );

        var result = await charger.ChargeAsync(SampleRequest(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Stripe.OffSession.CustomerMissing", result.Error.Code);
    }

    [Fact]
    public async Task Forwards_idempotency_key_and_customer_to_stripe()
    {
        RequestOptions? captured = null;
        PaymentIntentCreateOptions? capturedBody = null;
        var charger = BuildCharger(
            (opts, reqOpts, _) =>
            {
                captured = reqOpts;
                capturedBody = opts;
                return Task.FromResult(new PaymentIntent { Id = "pi_ok", Status = "succeeded" });
            }
        );

        await charger.ChargeAsync(SampleRequest(), CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal("idem-123", captured!.IdempotencyKey);
        Assert.NotNull(capturedBody);
        Assert.Equal("cus_abc", capturedBody!.Customer);
        Assert.Equal("pm_xyz", capturedBody.PaymentMethod);
        Assert.True(capturedBody.Confirm);
        Assert.True(capturedBody.OffSession);
    }

    private static OffSessionChargeRequest SampleRequest() =>
        new(
            TenantId: TenantId,
            SaaSPaymentId: Guid.NewGuid(),
            ProviderCode: "Stripe",
            ProviderToken: "pm_xyz",
            AmountCents: 1999,
            Currency: "USD",
            IdempotencyKey: "idem-123",
            PayerEmail: "payer@example.com"
        );

    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static StripeOffSessionCharger BuildCharger(
        Func<PaymentIntentCreateOptions, RequestOptions, CancellationToken, Task<PaymentIntent>> create
    )
    {
        var reference = ProviderCustomerReference.Create(PaymentProviderCode.Stripe, "cus_abc").Value;
        var customer = TenantProviderCustomer
            .Register(TenantId, PaymentProviderCode.Stripe, reference, "payer@example.com", DateTime.UtcNow)
            .Value;
        return new StripeOffSessionCharger(
            create,
            new InMemoryTenantProviderCustomerRepository(seeded: customer),
            NullLogger<StripeOffSessionCharger>.Instance,
            new FakePaymentAppMetrics()
        );
    }

    private sealed class InMemoryTenantProviderCustomerRepository(TenantProviderCustomer? seeded)
        : ITenantProviderCustomerRepository
    {
        public Task<TenantProviderCustomer?> GetByTenantAndProviderAsync(
            Guid tenantId,
            PaymentProviderCode code,
            CancellationToken ct = default
        ) => Task.FromResult(seeded);

        public Task<TenantProviderCustomer?> GetByIdAsync(
            Guid tenantProviderCustomerId,
            Guid tenantId,
            CancellationToken ct = default
        ) => Task.FromResult<TenantProviderCustomer?>(null);

        public Task<IReadOnlyList<TenantProviderCustomer>> GetWithMethodsExpiringBeforeAsync(
            DateTime cutoffUtc,
            int batchSize,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<TenantProviderCustomer>>(Array.Empty<TenantProviderCustomer>());

        public Task AddAsync(TenantProviderCustomer customer, CancellationToken ct = default) => Task.CompletedTask;
    }
}
