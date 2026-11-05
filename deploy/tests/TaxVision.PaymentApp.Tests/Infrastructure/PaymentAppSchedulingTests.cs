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
using TaxVision.PaymentApp.Application.SaaSPayments.Commands.ReconcileStuckSaaSPayment;
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
    public async Task PendingChargeReconciliationJob_invokes_one_command_per_stuck_payment()
    {
        var stuck = Enumerable
            .Range(0, 3)
            .Select(_ => CreateProcessingRenewalPayment(Guid.NewGuid(), DateTime.UtcNow.AddMinutes(-5)))
            .ToList();
        var unitOfWork = new FakeUnitOfWork();
        var bus = new CapturingMessageBus();
        using var services = BuildReconciliationServices(
            new StuckPaymentRepository(stuck),
            unitOfWork,
            bus,
            new StatusPaymentProvider(
                Result.Success(
                    new ChargeAuthorizationResult("pi_done", PaymentStatus.Succeeded, null, null, null, null)
                )
            )
        );

        await InvokeRunOnceAsync(CreateReconciliationJob(services), services);

        // Un comando por pago, con su tenant; el job no publica ni guarda nada por su cuenta.
        var invoked = bus.Invoked.Where(i => i.Message is ReconcileStuckSaaSPaymentCommand).ToList();
        Assert.Equal(3, invoked.Count);
        Assert.Equal(stuck.Select(p => p.TenantId.ToString()), invoked.Select(i => i.TenantId));
        Assert.Equal(
            stuck.Select(p => p.Id),
            invoked.Select(i => ((ReconcileStuckSaaSPaymentCommand)i.Message).SaaSPaymentId)
        );
        Assert.Empty(bus.Published);
        Assert.Equal(0, unitOfWork.SaveCalls);
        Assert.All(stuck, payment => Assert.Equal(PaymentStatus.Processing, payment.Status));
    }

    private static SaaSPayment CreateProcessingRenewalPayment(Guid tenantId, DateTime nowUtc)
    {
        var payment = SaaSPayment
            .Create(
                tenantId,
                IdempotencyKey.Create($"renewal-{Guid.NewGuid():N}").Value,
                Money.Create(4900, "USD").Value,
                SaaSPaymentType.SubscriptionRenewal,
                Guid.NewGuid(),
                PaymentProviderCode.PayPal,
                StatementDescriptor.Create("TAXVISION SAAS").Value,
                Guid.Empty,
                nowUtc
            )
            .Value;
        payment.MarkProcessing(
            ExternalPaymentReference.Create(PaymentProviderCode.PayPal, $"pi_{Guid.NewGuid():N}").Value,
            "processing",
            null,
            Guid.Empty,
            nowUtc
        );
        return payment;
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
        public Task<IReadOnlyList<(Guid Id, Guid TenantId)>> GetStuckProcessingIdsAsync(
            DateTime cutoffUtc,
            int batchSize,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<(Guid Id, Guid TenantId)>>(stuck.Select(p => (p.Id, p.TenantId)).ToList());

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

        public Task<SaaSPayment?> GetByProviderCheckoutSessionIdAsync(
            string providerSessionId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<(IReadOnlyList<SaaSPayment> Items, int TotalCount)> SearchForTenantAsync(
            Guid tenantId,
            int page,
            int pageSize,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<
            IReadOnlyDictionary<(SaaSPaymentType Type, Guid TargetAggregateId), DateTime>
        > GetLatestSucceededCreatedAtByTargetsAsync(
            Guid tenantId,
            IReadOnlyCollection<(SaaSPaymentType Type, Guid TargetAggregateId)> targets,
            CancellationToken ct = default
        ) =>
            Task.FromResult<IReadOnlyDictionary<(SaaSPaymentType, Guid), DateTime>>(
                new Dictionary<(SaaSPaymentType, Guid), DateTime>()
            );

        public Task<IReadOnlyDictionary<SaaSPaymentType, DateTime>> GetLatestSucceededCreatedAtByTypesAsync(
            Guid tenantId,
            IReadOnlyCollection<SaaSPaymentType> types,
            CancellationToken ct = default
        ) =>
            Task.FromResult<IReadOnlyDictionary<SaaSPaymentType, DateTime>>(
                new Dictionary<SaaSPaymentType, DateTime>()
            );

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
}
