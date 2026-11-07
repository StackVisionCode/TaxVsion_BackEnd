using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Onboarding.Application;
using TaxVision.Auth.Onboarding.Application.Abstractions;
using TaxVision.Auth.Onboarding.Application.Consumers;
using TaxVision.Auth.Onboarding.Application.TenantOnboardings.Commands;
using TaxVision.Auth.Onboarding.Application.TenantOnboardings.Services;
using TaxVision.Auth.Onboarding.Domain.TenantOnboardings;
using TaxVision.Auth.Onboarding.Infrastructure.Concurrency;
using TaxVision.Auth.Infrastructure.Security;
using TaxVision.Auth.Tests.Application;

namespace TaxVision.Auth.Tests.Onboarding;

/// <summary>
/// BB-16: el camino de éxito dispara el correo, la factura y el recibo. Sale después del guardado en los tres
/// llamadores, y si el guardado falla no sale nada. <c>SaveBeforePublishTests</c> no lo ve: lee el texto del método.
/// </summary>
public sealed class OnboardingSuccessCompleterTests
{
    private static readonly OnboardingOptions RegistrationOptions = new()
    {
        RegistrationUrlBase = "https://app.example.com",
    };

    private static readonly Type[] SuccessNotices =
    [
        typeof(OnboardingRegistrationReadyIntegrationEvent),
        typeof(OnboardingFinalizeCommand),
        typeof(RequestOnboardingReceiptCommand),
    ];

    [Fact]
    public async Task Payment_consumer_publishes_after_saving()
    {
        var world = new World();

        await world.ConsumePaymentAsync();

        world.AssertPublishedAfterSaving();
    }

    [Fact]
    public async Task Payment_consumer_publishes_nothing_when_the_save_fails()
    {
        var world = new World { FailSave = true };

        await Assert.ThrowsAsync<TimeoutException>(world.ConsumePaymentAsync);

        Assert.Empty(world.Bus.Published);
    }

    [Fact]
    public async Task Reconcile_publishes_after_saving()
    {
        var world = new World();

        var result = await world.ReconcileAsync();

        Assert.True(result.IsSuccess);
        world.AssertPublishedAfterSaving();
    }

    [Fact]
    public async Task Reconcile_publishes_nothing_when_the_save_fails()
    {
        var world = new World { FailSave = true };

        await Assert.ThrowsAsync<TimeoutException>(world.ReconcileAsync);

        Assert.Empty(world.Bus.Published);
    }

    [Fact]
    public async Task Fully_covered_checkout_publishes_after_saving()
    {
        var world = new World();

        var result = await world.StartFullyCoveredCheckoutAsync();

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.FullyCovered);
        world.AssertPublishedAfterSaving();
    }

    [Fact]
    public async Task Fully_covered_checkout_publishes_nothing_when_the_save_fails()
    {
        var world = new World { FailSave = true };

        await Assert.ThrowsAsync<TimeoutException>(world.StartFullyCoveredCheckoutAsync);

        Assert.Empty(world.Bus.Published);
    }

    [Fact]
    public async Task The_notices_leave_as_the_platform_tenant()
    {
        var world = new World();

        await world.ConsumePaymentAsync();

        Assert.Equal(PlatformTenant.Id.ToString(), world.Bus.TenantIdAtFirstPublish);
    }

    private sealed class World
    {
        private readonly Guid _paymentId = Guid.NewGuid();

        public World()
        {
            var now = DateTime.UtcNow;
            Onboarding = OnboardingTestFactory.NewOnboarding(now);
            Onboardings = new FakeTenantOnboardingRepository { Existing = Onboarding };
            UnitOfWork = new OrderUnitOfWork(this);
        }

        public bool FailSave { get; init; }
        public TenantOnboarding Onboarding { get; }
        public FakeTenantOnboardingRepository Onboardings { get; }
        public FakeMessageBus Bus { get; } = new();
        public OrderUnitOfWork UnitOfWork { get; }

        public void AssertPublishedAfterSaving()
        {
            Assert.Equal(1, UnitOfWork.Saves);
            Assert.Equal(0, UnitOfWork.PublishedAtSave);
            Assert.Equal(SuccessNotices, Bus.Published.Select(message => message.GetType()));
        }

        public Task ConsumePaymentAsync()
        {
            Assert.True(Onboarding.MarkPaymentProcessing(_paymentId, _paymentId.ToString("N")).IsSuccess);
            return OnboardingPaymentSucceededConsumer.Handle(
                new OnboardingPaymentSucceededIntegrationEvent
                {
                    OnboardingId = Onboarding.Id,
                    SaaSPaymentId = _paymentId,
                    PlanId = Onboarding.PlanId,
                    AmountPaidCents = 4900,
                    Currency = "USD",
                    PaidAtUtc = DateTime.UtcNow,
                    ProviderPaymentReference = "pi_123",
                },
                Onboardings,
                Completer(),
                new FakePlanCatalogClient("Enterprise"),
                UnitOfWork,
                Bus,
                new FakeCorrelationContext(),
                NullLogger<OnboardingPaymentSucceededIntegrationEvent>.Instance,
                CancellationToken.None
            );
        }

        public Task<Result<ReconcileOnboardingPaymentResponse>> ReconcileAsync()
        {
            Assert.True(Onboarding.MarkPaymentProcessing(_paymentId, _paymentId.ToString("N")).IsSuccess);
            var paymentApp = new FakePaymentAppOnboardingClient(
                Result.Success(new PaymentAppCheckoutResult(_paymentId, "url", "session", DateTime.UtcNow))
            )
            {
                ReconcileResult = Result.Success(
                    new PaymentAppReconcileResult(
                        _paymentId,
                        OnboardingPaymentStatus.Succeeded,
                        4900,
                        "USD",
                        FailureCode: null,
                        FailureMessage: null,
                        ProviderPaymentReference: "CAPTURE-123",
                        PaidAtUtc: DateTime.UtcNow
                    )
                ),
            };
            return ReconcileOnboardingPaymentHandler.Handle(
                new ReconcileOnboardingPaymentCommand(Onboarding.Id),
                Onboardings,
                paymentApp,
                Completer(),
                new FakePlanCatalogClient("Enterprise"),
                UnitOfWork,
                Bus,
                new FakeCorrelationContext(),
                new InMemoryOnboardingReconcileSerializer(),
                CancellationToken.None
            );
        }

        public Task<Result<StartOnboardingCheckoutResponse>> StartFullyCoveredCheckoutAsync()
        {
            var covered = Onboarding.ApplyOnboardingPricing(
                12900,
                12900,
                0,
                "USD",
                null,
                [new OnboardingCodeReservationInput(Guid.NewGuid(), OnboardingBenefitType.Gift, "WELCOME", 12900, "s")],
                DateTime.UtcNow
            );
            Assert.True(covered.IsSuccess);
            return StartOnboardingCheckoutHandler.Handle(
                new StartOnboardingCheckoutCommand(
                    Onboarding.Id,
                    "",
                    "https://app.example.com/success",
                    "https://app.example.com/cancel"
                ),
                Onboardings,
                new OnboardingCodeReserver(null!, null!),
                Completer(),
                new FakePlanCatalogClient("Enterprise"),
                new FakePaymentAppOnboardingClient(Result.Failure<PaymentAppCheckoutResult>(new Error("Unused", "Unused."))),
                new FakeOnboardingReturnReferenceStore(),
                Options.Create(RegistrationOptions),
                UnitOfWork,
                Bus,
                new FakeCorrelationContext(),
                CancellationToken.None
            );
        }

        private OnboardingSuccessCompleter Completer() =>
            new(
                new SecureTokenService(),
                new FakeTokenReferenceStore(),
                Options.Create(RegistrationOptions),
                FixedPlatformSettings.For("taxoffix.io"),
                NullLogger<OnboardingSuccessCompleter>.Instance
            );

        public sealed class OrderUnitOfWork(World world) : IUnitOfWork
        {
            public int Saves { get; private set; }
            public int PublishedAtSave { get; private set; }

            public Task<int> SaveChangesAsync(CancellationToken ct = default)
            {
                PublishedAtSave = world.Bus.Published.Count;
                if (world.FailSave)
                    throw new TimeoutException("Simulated SQL timeout.");
                Saves++;
                return Task.FromResult(1);
            }
        }
    }
}
