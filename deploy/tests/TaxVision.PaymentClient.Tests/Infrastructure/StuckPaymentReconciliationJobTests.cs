using System.Reflection;
using BuildingBlocks.Results;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Application.TenantPayments.Commands.ReconcileTenantPayment;
using TaxVision.PaymentClient.Domain.TenantPayments;
using TaxVision.PaymentClient.Domain.ValueObjects;
using TaxVision.PaymentClient.Infrastructure;
using TaxVision.PaymentClient.Infrastructure.Persistence.Repositories;
using TaxVision.PaymentClient.Infrastructure.Scheduling;
using TaxVision.PaymentClient.Tests.Application;
using Wolverine;

namespace TaxVision.PaymentClient.Tests.Infrastructure;

/// <summary>Una vuelta de la revisión de atascados (con fakes), la cola que la alimenta y sus opciones.</summary>
public sealed class StuckPaymentReconciliationJobTests
{
    private static readonly DateTime NowUtc = new(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Each_payment_is_reconciled_on_its_own_and_one_failure_does_not_stop_the_batch()
    {
        var resolved = new AwaitingProviderPayment(Guid.NewGuid(), Guid.NewGuid());
        var throws = new AwaitingProviderPayment(Guid.NewGuid(), Guid.NewGuid());
        var fails = new AwaitingProviderPayment(Guid.NewGuid(), Guid.NewGuid());
        var pending = new AwaitingProviderPayment(Guid.NewGuid(), Guid.NewGuid());
        var repository = new QueueRepository([throws, resolved, fails, pending]);
        var bus = ReconcileBus.Create(command =>
            command.TenantPaymentId == throws.TenantPaymentId ? throw new InvalidOperationException("transient")
            : command.TenantPaymentId == fails.TenantPaymentId
                ? Result.Failure<TenantPaymentReconciliation>(new Error("Stripe.ChargeStatus.Failed", "down"))
            : command.TenantPaymentId == pending.TenantPaymentId
                ? Result.Success(TenantPaymentReconciliation.StillPending)
            : Result.Success(TenantPaymentReconciliation.Resolved)
        );
        var settings = new StuckPaymentReconciliationOptions
        {
            StuckAfterMinutes = 30,
            GiveUpAfterDays = 30,
            BatchSize = 10,
        };

        var summary = await StuckPaymentReconciliationJob.ReconcileBatchAsync(
            repository,
            bus,
            settings,
            NullLogger.Instance,
            NowUtc,
            CancellationToken.None
        );

        Assert.Equal(new StuckPaymentReconciliationSummary(4, 1, 1, 2), summary);
        var proxy = (ReconcileBus)(object)bus;
        Assert.Equal(4, proxy.Commands.Count);
        // Cada comando sale con el tenant de su pago.
        Assert.Equal(
            [throws.TenantId, resolved.TenantId, fails.TenantId, pending.TenantId],
            proxy.TenantsAtInvoke.Select(Guid.Parse)
        );
        Assert.Equal((NowUtc.AddMinutes(-30), NowUtc.AddDays(-30), 10), repository.LastQuery);
    }

    [Fact]
    public async Task The_queue_takes_unresolved_payments_in_the_window_never_checked_first()
    {
        await using var db = InMemoryPaymentClient.CreateDb();
        var tenantId = Guid.NewGuid();
        var checkedEarlier = Stuck(tenantId, NowUtc.AddHours(-5));
        checkedEarlier.RecordProviderCheck(NowUtc.AddMinutes(-20));
        var neverChecked = Stuck(tenantId, NowUtc.AddHours(-1));
        var requiresAction = Stuck(tenantId, NowUtc.AddHours(-3));
        requiresAction.MarkRequiresAction("redirect_to_url", "https://3ds.test", Guid.Empty, NowUtc.AddHours(-3));
        requiresAction.RecordProviderCheck(NowUtc.AddMinutes(-40));
        var tooRecent = Stuck(tenantId, NowUtc.AddMinutes(-5));
        var tooOld = Stuck(tenantId, NowUtc.AddDays(-40));
        var resolved = Stuck(tenantId, NowUtc.AddHours(-2));
        resolved.MarkSucceeded(NowUtc.AddHours(-2), Guid.Empty);
        db.TenantPayments.AddRange(checkedEarlier, neverChecked, requiresAction, tooRecent, tooOld, resolved);
        await db.SaveChangesAsync();

        var queue = await new TenantPaymentRepository(db).GetAwaitingProviderConfirmationAsync(
            NowUtc.AddMinutes(-30),
            NowUtc.AddDays(-30),
            batchSize: 10
        );

        Assert.Equal(
            [neverChecked.Id, requiresAction.Id, checkedEarlier.Id],
            queue.Select(item => item.TenantPaymentId)
        );
        Assert.All(queue, item => Assert.Equal(tenantId, item.TenantId));
    }

    [Fact]
    public void The_defaults_are_valid_and_bad_values_fail_at_startup()
    {
        Assert.Equal(15, Resolve([]).IntervalMinutes);
        Assert.True(Resolve([]).Enabled);

        Assert.Throws<OptionsValidationException>(() => Resolve([new("BatchSize", "0")]));
        Assert.Throws<OptionsValidationException>(() =>
            Resolve([new("BatchSize", (StuckPaymentReconciliationOptions.MaxBatchSize + 1).ToString())])
        );
        Assert.Throws<OptionsValidationException>(() => Resolve([new("IntervalMinutes", "0")]));
        Assert.Throws<OptionsValidationException>(() => Resolve([new("StuckAfterMinutes", "1")]));
        Assert.Throws<OptionsValidationException>(() => Resolve([new("GiveUpAfterDays", "0")]));
    }

    [Fact]
    public void RecordProviderCheck_rotates_the_queue_without_pretending_the_payment_changed()
    {
        var payment = Stuck(Guid.NewGuid(), NowUtc.AddHours(-1));
        var updatedAt = payment.UpdatedAtUtc;

        payment.RecordProviderCheck(NowUtc);

        Assert.Equal(NowUtc, payment.LastProviderCheckAtUtc);
        Assert.Equal(updatedAt, payment.UpdatedAtUtc);
        Assert.True(payment.IsAwaitingProviderConfirmation);
    }

    private static StuckPaymentReconciliationOptions Resolve(KeyValuePair<string, string?>[] overrides)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                overrides
                    .Select(pair => new KeyValuePair<string, string?>(
                        $"{StuckPaymentReconciliationOptions.SectionName}:{pair.Key}",
                        pair.Value
                    ))
                    .Append(new("ConnectionStrings:Default", "Server=unused;Database=unused"))
            )
            .Build();
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddPaymentClientInfrastructure(configuration)
            .BuildServiceProvider();
        return provider.GetRequiredService<IOptions<StuckPaymentReconciliationOptions>>().Value;
    }

    private static TenantPayment Stuck(Guid tenantId, DateTime atUtc)
    {
        var payment = TenantPayment
            .Create(
                tenantId,
                IdempotencyKey.Create($"stuck-{Guid.NewGuid():N}").Value,
                Money.Create(2500, "USD").Value,
                null,
                PaymentPurpose.Create(PaymentPurposeKind.Other, null).Value,
                PaymentProviderCode.Stripe,
                StatementDescriptor.Create("ACME TAX SVC").Value,
                Guid.Empty,
                atUtc
            )
            .Value;
        payment.MarkProcessing(
            ExternalPaymentReference.Create(PaymentProviderCode.Stripe, $"pi_{Guid.NewGuid():N}").Value,
            null,
            null,
            Guid.Empty,
            atUtc
        );
        return payment;
    }

    /// <summary>Solo la cola; el resto del repositorio no lo usa el job.</summary>
    private sealed class QueueRepository(IReadOnlyList<AwaitingProviderPayment> queue)
        : QueueOnlyTenantPaymentRepository
    {
        public (DateTime StuckBefore, DateTime CreatedAfter, int Batch) LastQuery { get; private set; }

        public override Task<IReadOnlyList<AwaitingProviderPayment>> GetAwaitingProviderConfirmationAsync(
            DateTime stuckBeforeUtc,
            DateTime createdAfterUtc,
            int batchSize,
            CancellationToken ct = default
        )
        {
            LastQuery = (stuckBeforeUtc, createdAfterUtc, batchSize);
            return Task.FromResult(queue);
        }
    }
}

/// <summary>Base que lanza en todo salvo la cola (lo que no debería tocarse en estos tests).</summary>
internal abstract class QueueOnlyTenantPaymentRepository : ITenantPaymentRepository
{
    public abstract Task<IReadOnlyList<AwaitingProviderPayment>> GetAwaitingProviderConfirmationAsync(
        DateTime stuckBeforeUtc,
        DateTime createdAfterUtc,
        int batchSize,
        CancellationToken ct = default
    );

    public Task<TenantPayment?> GetByIdAsync(Guid tenantPaymentId, Guid tenantId, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<TenantPayment?> GetByIdempotencyKeyAsync(
        Guid tenantId,
        string idempotencyKey,
        CancellationToken ct = default
    ) => throw new NotSupportedException();

    public Task<TenantPayment?> GetByExternalReferenceAsync(
        Guid tenantId,
        PaymentProviderCode code,
        string providerChargeReference,
        CancellationToken ct = default
    ) => throw new NotSupportedException();

    public Task<IReadOnlyList<TenantPayment>> GetDueForRetryAsync(
        DateTime nowUtc,
        int batchSize,
        CancellationToken ct = default
    ) => throw new NotSupportedException();

    public Task<IReadOnlyList<TenantPayment>> SearchAdminAsync(
        Guid? tenantId,
        PaymentStatus? status,
        DateTime? from,
        DateTime? to,
        int page,
        int pageSize,
        CancellationToken ct = default
    ) => throw new NotSupportedException();

    public Task<bool> AnyForProviderAsync(Guid tenantId, PaymentProviderCode code, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlySet<PaymentProviderCode>> GetConfiguredProvidersWithPaymentsAsync(
        Guid tenantId,
        CancellationToken ct = default
    ) => throw new NotSupportedException();

    public Task AddAsync(TenantPayment payment, CancellationToken ct = default) => throw new NotSupportedException();
}

/// <summary>Bus que solo atiende <c>InvokeAsync</c> del comando de revisión y anota el tenant de cada uno.</summary>
internal class ReconcileBus : DispatchProxy
{
    private Func<ReconcileTenantPaymentCommand, Result<TenantPaymentReconciliation>> _handle = null!;
    private string? _tenantId;

    public List<ReconcileTenantPaymentCommand> Commands { get; } = [];
    public List<string> TenantsAtInvoke { get; } = [];

    public static IMessageBus Create(Func<ReconcileTenantPaymentCommand, Result<TenantPaymentReconciliation>> handle)
    {
        var bus = Create<IMessageBus, ReconcileBus>();
        ((ReconcileBus)(object)bus)._handle = handle;
        return bus;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        switch (targetMethod?.Name)
        {
            case "set_TenantId":
                _tenantId = (string?)args![0];
                return null;
            case "get_TenantId":
                return _tenantId;
            case nameof(IMessageBus.InvokeAsync) when targetMethod.IsGenericMethod:
                var command = (ReconcileTenantPaymentCommand)args![0]!;
                Commands.Add(command);
                TenantsAtInvoke.Add(_tenantId!);
                try
                {
                    return Task.FromResult(_handle(command));
                }
                catch (Exception ex)
                {
                    return Task.FromException<Result<TenantPaymentReconciliation>>(ex);
                }
            default:
                throw new NotSupportedException(targetMethod?.Name);
        }
    }
}
