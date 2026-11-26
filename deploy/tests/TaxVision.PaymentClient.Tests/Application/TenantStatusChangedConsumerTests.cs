using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.TenantIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Tenancy;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Application.Tenants.IntegrationEvents;
using TaxVision.PaymentClient.Domain.Recurring;
using TaxVision.PaymentClient.Domain.Tenants;
using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Tests.Application;

/// <summary>TEN-4: los avisos de estado se ordenan por la versión de la oficina.</summary>
public sealed class TenantStatusChangedConsumerTests
{
    private readonly Tenant _tenant = Tenant
        .Register(Guid.NewGuid(), "Acme Tax Office", "acme-tax", TenantKind.Customer, "Etc/UTC", DateTime.UtcNow)
        .Value;

    private readonly RecurringPlans _plans = new();

    [Fact]
    public async Task Handle_OlderVersionAfterNewer_IsIgnored()
    {
        await HandleAsync(Status(isActive: true, version: 6));
        await HandleAsync(Status(isActive: false, version: 5));

        Assert.True(_tenant.IsActive);
        Assert.Equal("Active", _tenant.Status);
    }

    [Fact]
    public async Task Handle_NewerVersion_IsApplied()
    {
        await HandleAsync(Status(isActive: true, version: 5));
        await HandleAsync(Status(isActive: false, version: 6));

        Assert.False(_tenant.IsActive);
        Assert.Equal("Suspended", _tenant.Status);
    }

    [Fact]
    public async Task Handle_MessageWithoutVersion_KeepsApplyingAsBefore()
    {
        await HandleAsync(Status(isActive: true, version: 6));
        await HandleAsync(Status(isActive: false, version: null));

        Assert.False(_tenant.IsActive);
    }

    [Fact]
    public async Task Handle_StatusOlderThanARename_IsStillApplied()
    {
        // La versión es una sola por oficina: el renombre v6 no dice nada del estado v5.
        await TenantUpdatedConsumer.Handle(
            new TenantUpdatedIntegrationEvent
            {
                TenantId = _tenant.Id,
                UpdatedTenantId = _tenant.Id,
                Name = "Renamed",
                DefaultTimeZoneId = "Etc/UTC",
                UpdatedAtUtc = DateTime.UtcNow,
                TenantVersion = 6,
            },
            new ApplyingRegistry(_tenant),
            new NoSaves(),
            new NoOpCorrelationContext(),
            CancellationToken.None
        );

        await HandleAsync(Status(isActive: false, version: 5));

        Assert.False(_tenant.IsActive);
    }

    [Fact]
    public async Task Handle_Reactivation_SkipsThePeriodsDueDuringTheSuspension()
    {
        // L2-01: al reactivar no se cobran en cadena los períodos atrasados; se cobra desde el siguiente.
        await HandleAsync(Status(isActive: false, version: 5));
        var plan = MonthlyPlanStartingAt(DateTime.UtcNow.AddMonths(-3).AddDays(-1));
        var overdue = plan.Schedules.Single();
        _plans.Plans.Add(plan);

        await HandleAsync(Status(isActive: true, version: 6));

        Assert.Equal(RecurringScheduleStatus.Skipped, overdue.Status);
        Assert.DoesNotContain(
            plan.Schedules,
            s => s.Status == RecurringScheduleStatus.Pending && s.ScheduledDate <= DateTime.UtcNow
        );
        var next = Assert.Single(plan.Schedules, s => s.Status == RecurringScheduleStatus.Pending);
        Assert.True(next.ScheduledDate > DateTime.UtcNow);
        Assert.Equal(4, plan.Schedules.Count(s => s.Status == RecurringScheduleStatus.Skipped));
    }

    [Fact]
    public async Task Handle_StatusChangeOfAnActiveTenant_LeavesDueSchedulesAlone()
    {
        // Solo la reactivación omite períodos; un aviso repetido de una oficina activa no toca nada.
        await HandleAsync(Status(isActive: true, version: 5));
        var plan = MonthlyPlanStartingAt(DateTime.UtcNow.AddDays(-1));
        _plans.Plans.Add(plan);

        await HandleAsync(Status(isActive: true, version: 6));

        Assert.Equal(RecurringScheduleStatus.Pending, plan.Schedules.Single().Status);
    }

    private TenantRecurringPayment MonthlyPlanStartingAt(DateTime startDate)
    {
        var plan = TenantRecurringPayment
            .Create(
                _tenant.Id,
                Guid.NewGuid(),
                PaymentProviderCode.Stripe,
                "pm_123",
                Money.Create(1999, "USD").Value,
                PaymentPurpose.Create(PaymentPurposeKind.RetainerPayment, "plan-001").Value,
                BillingCycle.Monthly,
                null,
                startDate,
                null,
                null,
                RetryPolicy.Default,
                null,
                null,
                Guid.Empty,
                startDate
            )
            .Value;
        plan.GenerateSchedules(1, startDate);
        return plan;
    }

    private TenantStatusChangedIntegrationEvent Status(bool isActive, long? version) =>
        new()
        {
            TenantId = _tenant.Id,
            ChangedTenantId = _tenant.Id,
            Status = isActive ? "Active" : "Suspended",
            IsActive = isActive,
            TenantVersion = version,
        };

    private Task HandleAsync(TenantStatusChangedIntegrationEvent evt) =>
        TenantStatusChangedConsumer.Handle(
            evt,
            new ApplyingRegistry(_tenant),
            _plans,
            new NoSaves(),
            new NoOpCorrelationContext(),
            CancellationToken.None
        );

    private sealed class ApplyingRegistry(Tenant tenant) : ITenantRegistry
    {
        public Task<Tenant?> GetByIdAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<Tenant?>(tenant);

        public Task UpsertCreatedAsync(
            Guid tenantId,
            string name,
            string subDomain,
            TenantKind kind,
            string defaultTimeZoneId,
            DateTime nowUtc,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task UpdateStatusAsync(
            Guid tenantId,
            string status,
            bool isActive,
            DateTime nowUtc,
            long? tenantVersion,
            CancellationToken ct = default
        )
        {
            tenant.ApplyStatusChange(status, isActive, nowUtc, tenantVersion);
            return Task.CompletedTask;
        }
    }

    private sealed class RecurringPlans : ITenantRecurringPaymentRepository
    {
        public List<TenantRecurringPayment> Plans { get; } = [];

        public Task<IReadOnlyList<TenantRecurringPayment>> GetActiveWithDuePendingSchedulesByTenantAsync(
            Guid tenantId,
            DateTime cutoffUtc,
            CancellationToken ct = default
        ) =>
            Task.FromResult<IReadOnlyList<TenantRecurringPayment>>(
                Plans
                    .Where(p =>
                        p.TenantId == tenantId
                        && p.Status == RecurringStatus.Active
                        && p.Schedules.Any(s =>
                            s.Status == RecurringScheduleStatus.Pending && s.ScheduledDate <= cutoffUtc
                        )
                    )
                    .ToList()
            );

        public Task<TenantRecurringPayment?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<TenantRecurringPayment>> SearchByTenantAsync(
            Guid tenantId,
            Guid? taxpayerId,
            RecurringStatus? status,
            int page,
            int pageSize,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<IReadOnlyList<TenantRecurringPayment>> GetWithDueSchedulesAsync(
            RecurringScheduleStatus scheduleStatus,
            DateTime cutoffUtc,
            int batchSize,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task AddAsync(TenantRecurringPayment plan, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class NoSaves : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class NoOpCorrelationContext : ICorrelationContext
    {
        public string CorrelationId => "test-correlation-id";

        public void Set(string correlationId) { }

        public IDisposable Push(string correlationId) => new NoOpScope();

        private sealed class NoOpScope : IDisposable
        {
            public void Dispose() { }
        }
    }
}
