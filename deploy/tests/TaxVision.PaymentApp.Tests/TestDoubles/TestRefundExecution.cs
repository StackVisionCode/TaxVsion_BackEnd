using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Application.SaaSPayments.Commands.RefundSaaSPayment;
using TaxVision.PaymentApp.Application.SaaSPayments.Common;
using TaxVision.PaymentApp.Domain.SaaSPayments;

namespace TaxVision.PaymentApp.Tests.TestDoubles;

/// <summary>Corre en línea los pasos del reembolso (preparar y registrar) con los handlers reales. Cada paso pide su
/// propio repositorio y unidad de trabajo, como el scope nuevo de cada <c>InvokeAsync</c>.</summary>
internal static class TestRefundExecution
{
    public static CapturingMessageBus Bus(
        Func<(ISaaSPaymentRepository Payments, IUnitOfWork UnitOfWork)> scope,
        IPaymentAdapterFactory adapters,
        IPaymentAuditLogWriter? audit = null,
        FakePaymentAppMetrics? metrics = null,
        SaaSPaymentChargeOptions? chargeOptions = null
    )
    {
        var bus = new CapturingMessageBus();
        var usedAudit = audit ?? new FakePaymentAuditLogWriter();
        var usedMetrics = metrics ?? new FakePaymentAppMetrics();
        bus.Route = message =>
        {
            var (payments, unitOfWork) = scope();
            return message switch
            {
                PrepareSaaSPaymentRefundCommand prepare => PrepareSaaSPaymentRefundHandler.Handle(
                    prepare,
                    payments,
                    adapters,
                    new NoAdminThrottle(),
                    usedAudit,
                    unitOfWork,
                    usedMetrics,
                    Options.Create(chargeOptions ?? new SaaSPaymentChargeOptions()),
                    new FakeCorrelationContext(),
                    NullLogger<SaaSPayment>.Instance,
                    CancellationToken.None
                ),
                RecordSaaSPaymentRefundCommand record => RecordSaaSPaymentRefundHandler.Handle(
                    record,
                    payments,
                    usedAudit,
                    unitOfWork,
                    usedMetrics,
                    new FakeCorrelationContext(),
                    NullLogger<SaaSPayment>.Instance,
                    CancellationToken.None
                ),
                _ => throw new NotSupportedException(message.GetType().Name),
            };
        };
        return bus;
    }

    public static IOptions<SaaSPaymentRefundOptions> RefundOptions(TimeSpan? window = null) =>
        Options.Create(new SaaSPaymentRefundOptions { DuplicateRequestWindow = window ?? TimeSpan.FromMinutes(15) });
}

internal sealed class NoAdminThrottle : IPaymentAttemptThrottle
{
    public Task<bool> IsWebhookThrottledAsync(Guid scopeId, CancellationToken ct = default) => Task.FromResult(false);

    public Task RegisterWebhookAttemptAsync(Guid scopeId, CancellationToken ct = default) => Task.CompletedTask;

    public Task<bool> IsAdminActionThrottledAsync(Guid tenantId, CancellationToken ct = default) =>
        Task.FromResult(false);

    public Task RegisterAdminActionAttemptAsync(Guid tenantId, CancellationToken ct = default) => Task.CompletedTask;
}
