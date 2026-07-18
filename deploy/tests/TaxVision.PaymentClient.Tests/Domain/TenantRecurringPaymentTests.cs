using TaxVision.PaymentClient.Domain.Recurring;
using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Tests.Domain;

public sealed class TenantRecurringPaymentTests
{
    private static readonly DateTime StartDate = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_with_a_zero_amount_fails()
    {
        var result = TenantRecurringPayment.Create(
            Guid.NewGuid(), Guid.NewGuid(), PaymentProviderCode.Stripe, "pm_123", Money.Zero("USD"), Purpose(),
            BillingCycle.Monthly, null, StartDate, null, null, RetryPolicy.Default, null, null, Guid.Empty, StartDate);

        Assert.True(result.IsFailure);
        Assert.Equal("TenantRecurringPayment.InvalidAmount", result.Error.Code);
    }

    [Fact]
    public void Create_Custom_cycle_without_an_interval_fails()
    {
        var result = TenantRecurringPayment.Create(
            Guid.NewGuid(), Guid.NewGuid(), PaymentProviderCode.Stripe, "pm_123", Money.Create(1999, "USD").Value, Purpose(),
            BillingCycle.Custom, null, StartDate, null, null, RetryPolicy.Default, null, null, Guid.Empty, StartDate);

        Assert.True(result.IsFailure);
        Assert.Equal("TenantRecurringPayment.InvalidInterval", result.Error.Code);
    }

    [Fact]
    public void Create_with_an_EndDate_before_StartDate_fails()
    {
        var result = TenantRecurringPayment.Create(
            Guid.NewGuid(), Guid.NewGuid(), PaymentProviderCode.Stripe, "pm_123", Money.Create(1999, "USD").Value, Purpose(),
            BillingCycle.Monthly, null, StartDate, StartDate.AddDays(-1), null, RetryPolicy.Default, null, null, Guid.Empty, StartDate);

        Assert.True(result.IsFailure);
        Assert.Equal("TenantRecurringPayment.InvalidEndDate", result.Error.Code);
    }

    [Fact]
    public void Create_starts_Active_with_NextExecutionDate_at_StartDate()
    {
        var plan = CreateMonthlyPlan();

        Assert.Equal(RecurringStatus.Active, plan.Status);
        Assert.Equal(StartDate, plan.NextExecutionDate);
        Assert.Empty(plan.Schedules);
    }

    [Fact]
    public void GenerateSchedules_advances_NextExecutionDate_by_the_billing_cycle()
    {
        var plan = CreateMonthlyPlan();

        var result = plan.GenerateSchedules(3, StartDate);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.Count);
        Assert.Equal(3, plan.Schedules.Count);
        Assert.Equal(StartDate.AddMonths(3), plan.NextExecutionDate);
        Assert.All(plan.Schedules, s => Assert.Equal(RecurringScheduleStatus.Pending, s.Status));
    }

    [Fact]
    public void GenerateSchedules_stops_at_EndDate()
    {
        var plan = CreateMonthlyPlan(endDate: StartDate.AddMonths(2).AddDays(1));

        var result = plan.GenerateSchedules(12, StartDate);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, plan.Schedules.Count); // Jan, Feb, Mar — Apr would exceed EndDate
    }

    [Fact]
    public void GenerateSchedules_stops_at_MaxExecutions()
    {
        var plan = CreateMonthlyPlan(maxExecutions: 2);

        var result = plan.GenerateSchedules(12, StartDate);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, plan.Schedules.Count);
    }

    [Fact]
    public void GenerateSchedules_on_a_paused_plan_fails()
    {
        var plan = CreateMonthlyPlan();
        plan.Pause(Guid.Empty, StartDate);

        var result = plan.GenerateSchedules(1, StartDate);

        Assert.True(result.IsFailure);
        Assert.Equal("TenantRecurringPayment.NotActive", result.Error.Code);
    }

    [Fact]
    public void RecordSuccess_marks_the_schedule_executed_and_increments_ExecutionCount()
    {
        var plan = CreateMonthlyPlan();
        var scheduleId = plan.GenerateSchedules(1, StartDate).Value.Single();
        plan.MarkScheduleProcessing(scheduleId, Guid.Empty, StartDate);
        var tenantPaymentId = Guid.NewGuid();

        var result = plan.RecordSuccess(scheduleId, tenantPaymentId, "succeeded", Guid.Empty, StartDate);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, plan.ExecutionCount);
        Assert.Equal(0, plan.FailureCount);
        Assert.Single(plan.Executions);
        Assert.True(plan.Executions.Single().Succeeded);
    }

    [Fact]
    public void RecordSuccess_completes_the_plan_once_MaxExecutions_is_reached()
    {
        var plan = CreateMonthlyPlan(maxExecutions: 1);
        var scheduleId = plan.GenerateSchedules(1, StartDate).Value.Single();
        plan.MarkScheduleProcessing(scheduleId, Guid.Empty, StartDate);

        var result = plan.RecordSuccess(scheduleId, Guid.NewGuid(), "succeeded", Guid.Empty, StartDate);

        Assert.True(result.IsSuccess);
        Assert.Equal(RecurringStatus.Completed, plan.Status);
    }

    [Fact]
    public void RecordFailure_schedules_a_retry_while_backoffs_remain()
    {
        var plan = CreateMonthlyPlan();
        var scheduleId = plan.GenerateSchedules(1, StartDate).Value.Single();
        plan.MarkScheduleProcessing(scheduleId, Guid.Empty, StartDate);

        var result = plan.RecordFailure(scheduleId, "card_declined", Guid.Empty, StartDate);

        Assert.True(result.IsSuccess);
        var schedule = plan.Schedules.Single();
        Assert.Equal(RecurringScheduleStatus.RetryPending, schedule.Status);
        Assert.Equal(StartDate.Add(RetryPolicy.Default.Backoffs[0]), schedule.NextRetryAtUtc);
        Assert.Equal(0, plan.FailureCount); // el plan solo cuenta fallos DEFINITIVOS de schedule, no cada retry
    }

    [Fact]
    public void RecordFailure_after_exhausting_backoffs_marks_the_schedule_Failed_and_increments_plan_FailureCount()
    {
        var plan = CreateMonthlyPlan();
        var scheduleId = plan.GenerateSchedules(1, StartDate).Value.Single();

        ExhaustRetries(plan, scheduleId);

        var schedule = plan.Schedules.Single();
        Assert.Equal(RecurringScheduleStatus.Failed, schedule.Status);
        Assert.Equal(1, plan.FailureCount);
        Assert.Equal(RecurringStatus.Active, plan.Status);
    }

    [Fact]
    public void RecordFailure_auto_suspends_the_plan_once_FailureCount_reaches_MaxFailures()
    {
        var plan = CreateMonthlyPlan();

        for (var i = 0; i < RetryPolicy.Default.MaxFailures; i++)
        {
            var scheduleId = plan.GenerateSchedules(1, StartDate).Value.Single();
            ExhaustRetries(plan, scheduleId);
        }

        Assert.Equal(RecurringStatus.Suspended, plan.Status);
    }

    [Fact]
    public void Pause_from_Active_succeeds_and_Resume_returns_to_Active()
    {
        var plan = CreateMonthlyPlan();

        var pauseResult = plan.Pause(Guid.Empty, StartDate);

        Assert.True(pauseResult.IsSuccess);
        Assert.Equal(RecurringStatus.Paused, plan.Status);

        var resumeResult = plan.Resume(Guid.Empty, StartDate);

        Assert.True(resumeResult.IsSuccess);
        Assert.Equal(RecurringStatus.Active, plan.Status);
    }

    [Fact]
    public void Resume_from_Suspended_reactivates_and_clears_FailureCount()
    {
        var plan = CreateMonthlyPlan();
        plan.Suspend("manual hold", Guid.Empty, StartDate);

        var result = plan.Resume(Guid.Empty, StartDate);

        Assert.True(result.IsSuccess);
        Assert.Equal(RecurringStatus.Active, plan.Status);
        Assert.Equal(0, plan.FailureCount);
    }

    [Fact]
    public void Suspend_with_no_reason_fails()
    {
        var plan = CreateMonthlyPlan();

        var result = plan.Suspend("  ", Guid.Empty, StartDate);

        Assert.True(result.IsFailure);
        Assert.Equal("TenantRecurringPayment.InvalidReason", result.Error.Code);
    }

    [Fact]
    public void Cancel_from_a_terminal_status_is_rejected()
    {
        var plan = CreateMonthlyPlan(maxExecutions: 1);
        var scheduleId = plan.GenerateSchedules(1, StartDate).Value.Single();
        plan.MarkScheduleProcessing(scheduleId, Guid.Empty, StartDate);
        plan.RecordSuccess(scheduleId, Guid.NewGuid(), "succeeded", Guid.Empty, StartDate);

        var result = plan.Cancel("no longer needed", Guid.Empty, StartDate);

        Assert.True(result.IsFailure);
        Assert.Equal("TenantRecurringPayment.InvalidTransition", result.Error.Code);
    }

    [Fact]
    public void Cancel_from_Active_succeeds()
    {
        var plan = CreateMonthlyPlan();

        var result = plan.Cancel("taxpayer requested", Guid.Empty, StartDate);

        Assert.True(result.IsSuccess);
        Assert.Equal(RecurringStatus.Cancelled, plan.Status);
    }

    [Fact]
    public void SkipSchedule_marks_a_pending_schedule_Skipped()
    {
        var plan = CreateMonthlyPlan();
        var scheduleId = plan.GenerateSchedules(1, StartDate).Value.Single();

        var result = plan.SkipSchedule(scheduleId, "taxpayer paid manually", Guid.Empty, StartDate);

        Assert.True(result.IsSuccess);
        Assert.Equal(RecurringScheduleStatus.Skipped, plan.Schedules.Single().Status);
    }

    private static void ExhaustRetries(TenantRecurringPayment plan, Guid scheduleId)
    {
        for (var attempt = 0; attempt <= RetryPolicy.Default.Backoffs.Count; attempt++)
        {
            plan.MarkScheduleProcessing(scheduleId, Guid.Empty, StartDate);
            plan.RecordFailure(scheduleId, "card_declined", Guid.Empty, StartDate);
        }
    }

    private static PaymentPurpose Purpose() => PaymentPurpose.Create(PaymentPurposeKind.RetainerPayment, "plan-001").Value;

    private static TenantRecurringPayment CreateMonthlyPlan(DateTime? endDate = null, int? maxExecutions = null) =>
        TenantRecurringPayment.Create(
            Guid.NewGuid(), Guid.NewGuid(), PaymentProviderCode.Stripe, "pm_123", Money.Create(1999, "USD").Value, Purpose(),
            BillingCycle.Monthly, null, StartDate, endDate, maxExecutions, RetryPolicy.Default, null, null, Guid.Empty, StartDate).Value;
}
