using System.Reflection;
using BuildingBlocks.Results;
using BuildingBlocks.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.PaymentClient.Application.Abstractions.Payments;
using TaxVision.PaymentClient.Application.Recurring.Commands.ExecuteRecurringSchedule;
using TaxVision.PaymentClient.Application.TenantPayments.Commands.ChargeTenantPayment;
using TaxVision.PaymentClient.Domain.Recurring;
using TaxVision.PaymentClient.Domain.TenantPaymentConfigs;
using TaxVision.PaymentClient.Domain.TenantPayments;
using TaxVision.PaymentClient.Domain.ValueObjects;
using TaxVision.PaymentClient.Infrastructure.Persistence;
using TaxVision.PaymentClient.Infrastructure.Persistence.Repositories;
using Wolverine;

namespace TaxVision.PaymentClient.Tests.Application;

/// <summary>Cada intento de una cuota recurrente llega al proveedor con su propia clave.</summary>
public sealed class ExecuteRecurringScheduleHandlerTests
{
    private static readonly DateTime NowUtc = DateTime.UtcNow;

    private readonly string _databaseName = Guid.NewGuid().ToString();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly RecordingPaymentProvider _stripe = new(
        new(PaymentProviderCode.Stripe, "Stripe", "Publishable key", "Secret key", true, false)
    );

    [Fact]
    public async Task Retry_AfterDeclinedAttempt_CallsProviderAgain()
    {
        var (planId, scheduleId) = await SeedAsync();

        _stripe.Status = PaymentStatus.Failed;
        Assert.True((await ExecuteAsync(planId, scheduleId)).IsSuccess);
        _stripe.Status = PaymentStatus.Succeeded;
        Assert.True((await ExecuteAsync(planId, scheduleId)).IsSuccess);

        Assert.Equal(2, _stripe.Requests.Count);
        Assert.NotEqual(_stripe.Requests[0].IdempotencyKey.Value, _stripe.Requests[1].IdempotencyKey.Value);
        await using var read = InMemoryPaymentClient.CreateDb(_databaseName);
        var plan = await read
            .TenantRecurringPayments.IgnoreQueryFilters()
            .Include(p => p.Schedules)
            .SingleAsync(p => p.Id == planId);
        Assert.Equal(RecurringScheduleStatus.Executed, plan.Schedules.Single(s => s.Id == scheduleId).Status);
        Assert.Equal(0, plan.FailureCount);
    }

    [Fact]
    public async Task ChargeWithoutRecordedResult_DoesNotChargeAgainWithANewKey()
    {
        var (planId, scheduleId) = await SeedAsync();
        await SeedPendingChargeAsync(planId, scheduleId);

        var result = await ExecuteAsync(planId, scheduleId);

        Assert.True(result.IsSuccess);
        Assert.Empty(_stripe.Requests);
        await using var read = InMemoryPaymentClient.CreateDb(_databaseName);
        var plan = await read
            .TenantRecurringPayments.IgnoreQueryFilters()
            .Include(p => p.Schedules)
            .SingleAsync(p => p.Id == planId);
        var schedule = plan.Schedules.Single(s => s.Id == scheduleId);
        Assert.Equal(RecurringScheduleStatus.Pending, schedule.Status);
        Assert.Equal(0, schedule.RetryCount);
    }

    // D-PAY-13: el cobro sigue abierto en el proveedor. Contarlo como fallo abriría un reintento con clave nueva.
    [Theory]
    [InlineData(PaymentStatus.Processing)]
    [InlineData(PaymentStatus.RequiresAction)]
    public async Task ChargeStillOpenAtTheProvider_IsNotAFailure(PaymentStatus status)
    {
        var (planId, scheduleId) = await SeedAsync();
        _stripe.Status = status;

        await Assert.ThrowsAsync<InvalidOperationException>(() => ExecuteAsync(planId, scheduleId));
        Assert.True((await ExecuteAsync(planId, scheduleId)).IsSuccess);

        Assert.Single(_stripe.Requests);
        var plan = await LoadPlanAsync(planId);
        var schedule = plan.Schedules.Single(s => s.Id == scheduleId);
        Assert.Equal(RecurringScheduleStatus.Pending, schedule.Status);
        Assert.Equal(0, schedule.RetryCount);
        Assert.Equal(0, plan.FailureCount);
    }

    [Fact]
    public async Task ChargeResolvedAfterProcessing_IsRecordedWithTheSameKey()
    {
        var (planId, scheduleId) = await SeedAsync();
        _stripe.Status = PaymentStatus.Processing;
        await Assert.ThrowsAsync<InvalidOperationException>(() => ExecuteAsync(planId, scheduleId));

        await using (var db = InMemoryPaymentClient.CreateDb(_databaseName))
        {
            var payment = await db.TenantPayments.IgnoreQueryFilters().SingleAsync();
            payment.MarkSucceeded(NowUtc, Guid.Empty);
            await db.SaveChangesAsync();
        }

        Assert.True((await ExecuteAsync(planId, scheduleId)).IsSuccess);

        Assert.Single(_stripe.Requests);
        var plan = await LoadPlanAsync(planId);
        Assert.Equal(RecurringScheduleStatus.Executed, plan.Schedules.Single(s => s.Id == scheduleId).Status);
    }

    private async Task<TenantRecurringPayment> LoadPlanAsync(Guid planId)
    {
        await using var read = InMemoryPaymentClient.CreateDb(_databaseName);
        return await read
            .TenantRecurringPayments.IgnoreQueryFilters()
            .Include(p => p.Schedules)
            .SingleAsync(p => p.Id == planId);
    }

    private async Task SeedPendingChargeAsync(Guid planId, Guid scheduleId)
    {
        await using var db = InMemoryPaymentClient.CreateDb(_databaseName);
        var plan = await db
            .TenantRecurringPayments.IgnoreQueryFilters()
            .Include(p => p.Schedules)
            .SingleAsync(p => p.Id == planId);
        var key = plan.Schedules.Single(s => s.Id == scheduleId).ChargeIdempotencyKey(planId);
        db.TenantPayments.Add(
            TenantPayment
                .Create(
                    _tenantId,
                    IdempotencyKey.Create(key).Value,
                    Money.Create(1999, "USD").Value,
                    null,
                    PaymentPurpose.Create(PaymentPurposeKind.Other, null).Value,
                    PaymentProviderCode.Stripe,
                    StatementDescriptor.Create("ACME TAX SVC").Value,
                    Guid.Empty,
                    NowUtc
                )
                .Value
        );
        await db.SaveChangesAsync();
    }

    private async Task<Result> ExecuteAsync(Guid planId, Guid scheduleId)
    {
        // Contexto nuevo por ejecución, como cada pasada del job.
        await using var db = InMemoryPaymentClient.CreateDb(_databaseName);
        return await ExecuteRecurringScheduleHandler.Handle(
            new ExecuteRecurringScheduleCommand(_tenantId, planId, scheduleId),
            new TenantRecurringPaymentRepository(db),
            new TenantPaymentRepository(db),
            new PaymentAuditLogWriter(db),
            db,
            DispatchingBus.Create(ChargeAsync),
            new FixedCorrelationContext(),
            CancellationToken.None
        );
    }

    // El cobro corre con su propio contexto, como el InvokeAsync de Wolverine.
    private async Task<object> ChargeAsync(object message)
    {
        await using var db = InMemoryPaymentClient.CreateDb(_databaseName);
        return await TestChargeFlow.ChargeAsync(
            (ChargeTenantPaymentCommand)message,
            new TenantPaymentRepository(db),
            new TenantPaymentConfigRepository(db),
            new TenantConnectAccountRepository(db),
            InMemoryPaymentClient.FakeAdapters(_stripe),
            new PrefixSecretProtector(),
            Options.Create(
                new PlatformStripeCredentials { PlatformSecretKey = "unused", ConnectWebhookSecret = "unused" }
            ),
            new PaymentAuditLogWriter(db),
            db,
            new NoOpPaymentClientMetrics(),
            new FixedCorrelationContext(),
            NullLogger<TenantPayment>.Instance,
            CancellationToken.None
        );
    }

    private async Task<(Guid PlanId, Guid ScheduleId)> SeedAsync()
    {
        await using var db = InMemoryPaymentClient.CreateDb(_databaseName);
        var stripe = TenantPaymentConfig
            .Create(
                _tenantId,
                PaymentProviderCode.Stripe,
                TenantPaymentMode.DirectApiKeys,
                "pk_test_office",
                StatementDescriptor.Create("ACME TAX SVC").Value,
                NowUtc
            )
            .Value;
        stripe.UpdateSecrets(Encrypted("sk_test_office"), Encrypted("whsec_office"), Guid.Empty, NowUtc);
        stripe.MarkActive(Guid.Empty, NowUtc);
        db.TenantPaymentConfigs.Add(stripe);

        var plan = TenantRecurringPayment
            .Create(
                _tenantId,
                Guid.NewGuid(),
                PaymentProviderCode.Stripe,
                "pm_card_visa",
                Money.Create(1999, "USD").Value,
                PaymentPurpose.Create(PaymentPurposeKind.Other, null).Value,
                BillingCycle.Monthly,
                null,
                NowUtc,
                null,
                null,
                RetryPolicy.Default,
                null,
                null,
                Guid.Empty,
                NowUtc
            )
            .Value;
        if (plan.Schedules.Count == 0)
            plan.GenerateSchedules(1, NowUtc);
        db.TenantRecurringPayments.Add(plan);
        await db.SaveChangesAsync();
        return (plan.Id, plan.Schedules.OrderBy(s => s.ScheduledDate).First().Id);
    }

    private static EncryptedSecret Encrypted(string plaintext) =>
        EncryptedSecret.Create(new PrefixSecretProtector().Protect(plaintext)).Value;
}

/// <summary>Bus que resuelve InvokeAsync con una función (el handler real del comando).</summary>
internal class DispatchingBus : DispatchProxy
{
    private Func<object, Task<object>> _invoke = null!;

    public static IMessageBus Create(Func<object, Task<object>> invoke)
    {
        var bus = DispatchProxy.Create<IMessageBus, DispatchingBus>();
        ((DispatchingBus)(object)bus)._invoke = invoke;
        return bus;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod is { Name: nameof(IMessageBus.InvokeAsync), IsGenericMethod: true })
        {
            var cast = typeof(DispatchingBus)
                .GetMethod(nameof(CastAsync), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(targetMethod.GetGenericArguments()[0]);
            return cast.Invoke(null, [_invoke(args![0]!)]);
        }

        if (targetMethod?.Name is "set_TenantId" or "get_TenantId")
            return null;

        throw new NotSupportedException(targetMethod?.Name);
    }

    private static async Task<T> CastAsync<T>(Task<object> task) => (T)await task;
}
