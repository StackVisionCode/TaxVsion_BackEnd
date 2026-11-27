using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.PaymentClient.Domain.Recurring;
using TaxVision.PaymentClient.Domain.Tenants;
using TaxVision.PaymentClient.Domain.ValueObjects;
using TaxVision.PaymentClient.Infrastructure.Persistence.Repositories;

namespace TaxVision.PaymentClient.Tests.Integration;

/// <summary>
/// Contra el SQL Server real: los jobs de cobro recurrente no toman los schedules de una oficina que no puede
/// operar, y el filtro va en la consulta para que esos schedules no llenen el lote.
/// </summary>
public sealed class TenantRecurringPaymentDueSchedulesIntegrationTests : IAsyncLifetime
{
    private static readonly DateTime StartDate = DateTime.UtcNow.AddDays(-2);

    private readonly Guid _suspendedTenantId = Guid.NewGuid();
    private readonly Guid _activeTenantId = Guid.NewGuid();
    private readonly Guid _unknownTenantId = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        await using var db = PaymentClientTestDatabase.NewContext(Guid.Empty);
        var suspended = Tenant
            .Register(
                _suspendedTenantId,
                "Suspended Office",
                $"s-{_suspendedTenantId:N}",
                TenantKind.Customer,
                "Etc/UTC",
                StartDate
            )
            .Value;
        suspended.ApplyStatusChange("Suspended", isActive: false, StartDate);
        var active = Tenant
            .Register(
                _activeTenantId,
                "Active Office",
                $"a-{_activeTenantId:N}",
                TenantKind.Customer,
                "Etc/UTC",
                StartDate
            )
            .Value;
        db.Tenants.AddRange(suspended, active);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        Guid[] tenants = [_suspendedTenantId, _activeTenantId, _unknownTenantId];
        await using var db = PaymentClientTestDatabase.NewContext(Guid.Empty);
        await db
            .TenantRecurringPayments.IgnoreQueryFilters()
            .Where(p => tenants.Contains(p.TenantId))
            .ExecuteDeleteAsync();
        await db.Tenants.Where(t => tenants.Contains(t.Id)).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task PendingSchedules_OfASuspendedTenant_AreNotSelected()
    {
        var suspendedPlan = await SavePlanAsync(_suspendedTenantId);
        var activePlan = await SavePlanAsync(_activeTenantId);
        var unknownPlan = await SavePlanAsync(_unknownTenantId);

        var due = await DueAsync(RecurringScheduleStatus.Pending, DateTime.UtcNow);

        Assert.DoesNotContain(suspendedPlan, due);
        Assert.Contains(activePlan, due);
        // Sin proyección de la oficina todavía: se sigue cobrando.
        Assert.Contains(unknownPlan, due);
    }

    [Fact]
    public async Task Reactivation_LoadsOnlyThatTenantsPlansWithDuePendingSchedules()
    {
        var suspendedPlan = await SavePlanAsync(_suspendedTenantId);
        await SavePlanAsync(_activeTenantId);

        await using var db = PaymentClientTestDatabase.NewContext(Guid.Empty);
        var plans = await new TenantRecurringPaymentRepository(db).GetActiveWithDuePendingSchedulesByTenantAsync(
            _suspendedTenantId,
            DateTime.UtcNow
        );

        var plan = Assert.Single(plans);
        Assert.Equal(suspendedPlan, plan.Id);
        Assert.Single(plan.Schedules);
    }

    [Fact]
    public async Task RetryPendingSchedules_OfASuspendedTenant_AreNotSelected()
    {
        var suspendedPlan = await SavePlanAsync(_suspendedTenantId, failOnce: true);
        var activePlan = await SavePlanAsync(_activeTenantId, failOnce: true);

        var due = await DueAsync(RecurringScheduleStatus.RetryPending, DateTime.UtcNow.AddDays(30));

        Assert.DoesNotContain(suspendedPlan, due);
        Assert.Contains(activePlan, due);
    }

    private async Task<Guid> SavePlanAsync(Guid tenantId, bool failOnce = false)
    {
        var plan = TenantRecurringPayment
            .Create(
                tenantId,
                Guid.NewGuid(),
                PaymentProviderCode.Stripe,
                "pm_123",
                Money.Create(1999, "USD").Value,
                PaymentPurpose.Create(PaymentPurposeKind.RetainerPayment, "plan-001").Value,
                BillingCycle.Monthly,
                null,
                StartDate,
                null,
                null,
                RetryPolicy.Default,
                null,
                null,
                Guid.Empty,
                StartDate
            )
            .Value;
        var scheduleId = plan.GenerateSchedules(1, StartDate).Value.Single();
        if (failOnce)
        {
            plan.MarkScheduleProcessing(scheduleId, Guid.Empty, StartDate);
            plan.RecordFailure(scheduleId, "card_declined", Guid.Empty, StartDate);
        }

        await using var db = PaymentClientTestDatabase.NewContext(tenantId);
        db.TenantRecurringPayments.Add(plan);
        await db.SaveChangesAsync();
        return plan.Id;
    }

    private static async Task<IReadOnlyList<Guid>> DueAsync(RecurringScheduleStatus status, DateTime cutoffUtc)
    {
        await using var db = PaymentClientTestDatabase.NewContext(Guid.Empty);
        var plans = await new TenantRecurringPaymentRepository(db).GetWithDueSchedulesAsync(status, cutoffUtc, 1000);
        return plans.Select(p => p.Id).ToList();
    }
}
