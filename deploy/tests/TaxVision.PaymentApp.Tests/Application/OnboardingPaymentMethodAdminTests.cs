using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Options;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Application.Admin.Commands;
using TaxVision.PaymentApp.Domain.Audit;
using TaxVision.PaymentApp.Domain.PaymentMethods;
using TaxVision.PaymentApp.Domain.ValueObjects;
using TaxVision.PaymentApp.Infrastructure.Providers.Catalog;
using TaxVision.PaymentApp.Infrastructure.Providers.PayPal;
using TaxVision.PaymentApp.Infrastructure.Providers.Stripe;

namespace TaxVision.PaymentApp.Tests.Application;

public sealed class OnboardingPaymentMethodAdminTests
{
    [Fact]
    public async Task Set_availability_disables_method_and_audits_change()
    {
        var overrides = new InMemoryOnboardingPaymentMethodOverrideRepository();
        var catalog = CreateCatalog(overrides);
        var audit = new FakePaymentAuditLogWriter();
        var unitOfWork = new FakeUnitOfWork();
        var actorUserId = Guid.NewGuid();

        var result = await SetOnboardingPaymentMethodAvailabilityHandler.Handle(
            new SetOnboardingPaymentMethodAvailabilityCommand(
                PaymentProviderCode.Stripe,
                PaymentMethodKind.Card,
                Enabled: false,
                DisabledReason: "Maintenance",
                actorUserId
            ),
            catalog,
            overrides,
            audit,
            unitOfWork,
            new FakeCorrelationContext(),
            new FixedTimeProvider(DateTimeOffset.Parse("2026-09-04T12:00:00Z")),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.Enabled);
        Assert.Equal("Maintenance", result.Value.DisabledReason);
        Assert.True(result.Value.HasOverride);
        Assert.Equal(1, unitOfWork.Saves);
        var entry = Assert.Single(audit.Entries);
        Assert.Equal(PaymentAuditAction.OnboardingPaymentMethodAvailabilityChanged, entry.Action);
        Assert.Equal("test-correlation", entry.CorrelationId);

        var checkoutGate = await catalog.EnsureEnabledAsync(
            PaymentProviderCode.Stripe,
            PaymentMethodKind.Card,
            Guid.NewGuid(),
            "Monthly",
            "USD"
        );
        Assert.True(checkoutGate.IsFailure);
        Assert.Equal("PaymentMethod.Disabled", checkoutGate.Error.Code);
    }

    private static ConfiguredOnboardingPaymentMethodCatalog CreateCatalog(
        IOnboardingPaymentMethodOverrideRepository overrides
    ) =>
        new(
            Options.Create(new PaymentMethodCatalogOptions()),
            Options.Create(new StripeOptions { SecretKey = "sk_test_123", WebhookSecret = "whsec_123" }),
            Options.Create(
                new PayPalOptions
                {
                    ClientId = "paypal-client-id",
                    ClientSecret = "paypal-client-secret",
                    WebhookId = "paypal-webhook-id",
                }
            ),
            overrides
        );

    private sealed class InMemoryOnboardingPaymentMethodOverrideRepository : IOnboardingPaymentMethodOverrideRepository
    {
        private readonly List<OnboardingPaymentMethodOverride> _overrides = [];

        public Task<IReadOnlyList<OnboardingPaymentMethodOverride>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<OnboardingPaymentMethodOverride>>(_overrides);

        public Task<OnboardingPaymentMethodOverride?> GetAsync(
            PaymentProviderCode providerCode,
            string method,
            CancellationToken ct = default
        ) =>
            Task.FromResult(
                _overrides.FirstOrDefault(x =>
                    x.ProviderCode == providerCode
                    && string.Equals(x.Method, method, StringComparison.OrdinalIgnoreCase)
                )
            );

        public Task AddAsync(OnboardingPaymentMethodOverride paymentMethodOverride, CancellationToken ct = default)
        {
            _overrides.Add(paymentMethodOverride);
            return Task.CompletedTask;
        }
    }

    private sealed class FakePaymentAuditLogWriter : IPaymentAuditLogWriter
    {
        public List<PaymentAuditEntry> Entries { get; } = [];

        public Task AppendAsync(PaymentAuditEntry entry, CancellationToken ct = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int Saves { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            Saves++;
            return Task.FromResult(1);
        }
    }

    private sealed class FakeCorrelationContext : ICorrelationContext
    {
        public string CorrelationId { get; private set; } = "test-correlation";

        public void Set(string correlationId) => CorrelationId = correlationId;

        public IDisposable Push(string correlationId)
        {
            var previous = CorrelationId;
            CorrelationId = correlationId;
            return new Restore(() => CorrelationId = previous);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class Restore(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }
}
