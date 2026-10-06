using System.Reflection;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;
using TaxVision.PaymentApp.Infrastructure.Providers.PayPal;
using TaxVision.PaymentApp.Infrastructure.Scheduling;
using TaxVision.PaymentApp.Tests.TestDoubles;
using Wolverine;

namespace TaxVision.PaymentApp.Tests.Infrastructure;

public sealed class PaymentAppSchedulingTests
{
    [Fact]
    public async Task Periodic_job_continues_after_dependency_cancellation()
    {
        using var services = new ServiceCollection()
            .AddSingleton<IHostApplicationLifetime, StartedApplicationLifetime>()
            .BuildServiceProvider();
        var job = new TimeoutThrowingJob(
            services.GetRequiredService<IServiceScopeFactory>(),
            new AlwaysAcquiredLockFactory()
        );

        await job.StartAsync(CancellationToken.None);
        await job.SecondRun.WaitAsync(TimeSpan.FromSeconds(2));
        await job.StopAsync(CancellationToken.None);

        Assert.True(job.RunCount >= 2);
    }

    [Fact]
    public async Task Pending_reconciliation_marks_old_provider_not_found_payment_failed_without_retry()
    {
        var nowUtc = DateTime.UtcNow;
        var payment = CreateProcessingOnboardingPayment(nowUtc.AddHours(-1));
        var repository = new StuckPaymentRepository([payment]);
        var unitOfWork = new FakeUnitOfWork();
        var bus = new CapturingMessageBus();
        using var services = BuildReconciliationServices(
            repository,
            unitOfWork,
            bus,
            new StatusPaymentProvider(
                Result.Failure<ChargeAuthorizationResult>(
                    new Error("PayPal.ChargeStatus.NotFound", "PayPal returned HTTP 404.")
                )
            )
        );
        var job = CreateReconciliationJob(services);

        await InvokeRunOnceAsync(job, services);

        Assert.Equal(PaymentStatus.Failed, payment.Status);
        Assert.Equal("PayPal.ChargeStatus.NotFound", payment.FailureCode);
        Assert.Null(payment.NextRetryAtUtc);
        Assert.Equal(1, unitOfWork.SaveCalls);

        var published = Assert.Single(bus.Published.OfType<OnboardingPaymentFailedIntegrationEvent>());
        Assert.Equal(payment.Id, published.SaaSPaymentId);
        Assert.Equal("PayPal.ChargeStatus.NotFound", published.FailureCode);
    }

    [Fact]
    public async Task Pending_reconciliation_keeps_recent_provider_not_found_payment_processing()
    {
        var payment = CreateProcessingOnboardingPayment(DateTime.UtcNow);
        var repository = new StuckPaymentRepository([payment]);
        var unitOfWork = new FakeUnitOfWork();
        var bus = new CapturingMessageBus();
        using var services = BuildReconciliationServices(
            repository,
            unitOfWork,
            bus,
            new StatusPaymentProvider(
                Result.Failure<ChargeAuthorizationResult>(
                    new Error("PayPal.ChargeStatus.NotFound", "PayPal returned HTTP 404.")
                )
            )
        );
        var job = CreateReconciliationJob(services);

        await InvokeRunOnceAsync(job, services);

        Assert.Equal(PaymentStatus.Processing, payment.Status);
        Assert.Empty(bus.Published);
    }

    private static PendingChargeReconciliationJob CreateReconciliationJob(ServiceProvider services) =>
        new(
            services.GetRequiredService<IServiceScopeFactory>(),
            new AlwaysAcquiredLockFactory(),
            NullLogger<PendingChargeReconciliationJob>.Instance
        );

    private static ServiceProvider BuildReconciliationServices(
        ISaaSPaymentRepository repository,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        IPaymentProvider provider
    ) =>
        new ServiceCollection()
            .AddSingleton(repository)
            .AddSingleton(unitOfWork)
            .AddSingleton(bus)
            .AddSingleton<IPaymentAdapterFactory>(new FakePaymentAdapterFactory(provider))
            .AddSingleton<ICorrelationContext>(new FakeCorrelationContext())
            .AddSingleton<ITenantRegistry>(new FakeTenantRegistry("Acme Tax"))
            .AddSingleton<ILogger<PendingChargeReconciliationJob>>(NullLogger<PendingChargeReconciliationJob>.Instance)
            .BuildServiceProvider();

    private static async Task InvokeRunOnceAsync(PendingChargeReconciliationJob job, ServiceProvider services)
    {
        var method = typeof(PendingChargeReconciliationJob).GetMethod(
            "RunOnceAsync",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;

        var task = (Task)method.Invoke(job, [services, CancellationToken.None])!;
        await task;
    }

    private static SaaSPayment CreateProcessingOnboardingPayment(DateTime nowUtc)
    {
        var payment = SaaSPayment
            .CreateForOnboarding(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                IdempotencyKey.Create($"onboarding-{Guid.NewGuid():N}").Value,
                Money.Create(4900, "USD").Value,
                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                PaymentProviderCode.PayPal,
                StatementDescriptor.Create("TAXVISION SAAS").Value,
                nowUtc
            )
            .Value;

        payment.RecordHostedCheckoutSession(
            "ORDER-404",
            ExternalPaymentReference.Create(PaymentProviderCode.PayPal, "ORDER-404").Value,
            "https://paypal.example/checkout",
            nowUtc
        );

        return payment;
    }

    private sealed class TimeoutThrowingJob(IServiceScopeFactory scopeFactory, IDistributedLockFactory lockFactory)
        : PeriodicPaymentAppJob(
            scopeFactory,
            lockFactory,
            NullLogger<TimeoutThrowingJob>.Instance,
            TimeSpan.FromMilliseconds(10),
            TimeSpan.FromSeconds(1)
        )
    {
        private readonly TaskCompletionSource _secondRun = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task SecondRun => _secondRun.Task;
        public int RunCount { get; private set; }
        protected override string JobName => "timeout-test";

        protected override Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
        {
            RunCount++;
            if (RunCount >= 2)
                _secondRun.TrySetResult();

            throw new TaskCanceledException("Provider timeout.");
        }
    }

    private sealed class AlwaysAcquiredLockFactory : IDistributedLockFactory
    {
        public Task<IAsyncDisposable?> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken ct = default) =>
            Task.FromResult<IAsyncDisposable?>(new NoopLock());
    }

    private sealed class NoopLock : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class StartedApplicationLifetime : IHostApplicationLifetime
    {
        private readonly CancellationTokenSource _started = new();
        private readonly CancellationTokenSource _stopping = new();
        private readonly CancellationTokenSource _stopped = new();

        public StartedApplicationLifetime()
        {
            _started.Cancel();
        }

        public CancellationToken ApplicationStarted => _started.Token;
        public CancellationToken ApplicationStopping => _stopping.Token;
        public CancellationToken ApplicationStopped => _stopped.Token;

        public void StopApplication() => _stopping.Cancel();
    }

    private sealed class StuckPaymentRepository(IReadOnlyList<SaaSPayment> stuck) : ISaaSPaymentRepository
    {
        public Task<IReadOnlyList<SaaSPayment>> GetStuckProcessingAsync(
            DateTime cutoffUtc,
            int batchSize,
            CancellationToken ct = default
        ) => Task.FromResult(stuck);

        public Task<SaaSPayment?> GetByIdAsync(Guid saaSPaymentId, Guid tenantId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<SaaSPayment?> GetByIdAsync(Guid saaSPaymentId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<SaaSPayment?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<SaaSPayment?> GetByExternalReferenceAsync(
            PaymentProviderCode code,
            string providerChargeReference,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<IReadOnlyList<SaaSPayment>> GetSucceededWithoutReceiptAsync(
            DateTime cutoffUtc,
            int batchSize,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<IReadOnlyList<SaaSPayment>> GetDueForRetryAsync(
            DateTime nowUtc,
            int batchSize,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<int> CountDueForRetryAsync(DateTime nowUtc, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<long> SumSucceededAmountCentsAsync(
            SaaSPaymentType type,
            DateTime sinceUtc,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<SaaSPayment?> GetByOnboardingIdAsync(Guid onboardingId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<(IReadOnlyList<SaaSPayment> Items, int TotalCount)> SearchForTenantAsync(
            Guid tenantId,
            int page,
            int pageSize,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<IReadOnlyList<SaaSPayment>> SearchAdminAsync(
            Guid? tenantId,
            PaymentStatus? status,
            SaaSPaymentType? type,
            DateTime? from,
            DateTime? to,
            int page,
            int pageSize,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task AddAsync(SaaSPayment payment, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class StatusPaymentProvider(Result<ChargeAuthorizationResult> result) : IPaymentProvider
    {
        public PaymentProviderCode Code => PaymentProviderCode.PayPal;
        public ProviderCapabilities Capabilities => PayPalCapabilities.Instance;

        public Task<Result<ChargeAuthorizationResult>> GetChargeStatusAsync(
            string providerChargeReference,
            CancellationToken ct
        ) => Task.FromResult(result);

        public Task<Result<ChargeAuthorizationResult>> FinalizeHostedCheckoutAsync(
            string providerChargeReference,
            Money amount,
            CancellationToken ct
        ) => Task.FromResult(result);

        public Task<Result<ProviderCustomerToken>> GetOrCreateCustomerAsync(
            Guid tenantId,
            string email,
            string? name,
            CancellationToken ct
        ) => throw new NotSupportedException();

        public Task<Result<ChargeAuthorizationResult>> AuthorizeChargeAsync(
            ChargeAuthorizationRequest request,
            CancellationToken ct
        ) => throw new NotSupportedException();

        public Task<Result<CaptureResult>> CaptureAsync(
            string providerChargeReference,
            Money amount,
            CancellationToken ct
        ) => throw new NotSupportedException();

        public Task<Result<RefundResult>> RefundAsync(
            string providerChargeReference,
            Money amount,
            string reason,
            CancellationToken ct
        ) => throw new NotSupportedException();

        public Task<Result<WebhookVerificationResult>> VerifyWebhookSignatureAsync(
            ProviderWebhookVerificationRequest request,
            CancellationToken ct
        ) => throw new NotSupportedException();

        public Task<Result<WebhookEventPayload>> ParseWebhookEventAsync(
            string rawPayload,
            string eventType,
            CancellationToken ct
        ) => throw new NotSupportedException();

        public Task<Result<SetupIntentInfo>> CreateSetupIntentAsync(
            ProviderCustomerToken customer,
            CancellationToken ct
        ) => throw new NotSupportedException();

        public Task<Result<SavedPaymentMethodInfo>> AttachPaymentMethodAsync(
            ProviderCustomerToken customer,
            string paymentMethodReference,
            CancellationToken ct
        ) => throw new NotSupportedException();

        public Task<Result> DetachPaymentMethodAsync(string paymentMethodReference, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<Result<HostedCheckoutSessionResult>> CreateHostedCheckoutSessionAsync(
            HostedCheckoutSessionRequest request,
            CancellationToken ct
        ) => throw new NotSupportedException();
    }
}
