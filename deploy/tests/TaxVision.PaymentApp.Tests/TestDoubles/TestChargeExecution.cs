using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Application.SaaSPayments.Commands.ClaimSaaSPaymentCharge;
using TaxVision.PaymentApp.Application.SaaSPayments.Commands.ExecuteSaaSPaymentCharge;
using TaxVision.PaymentApp.Application.SaaSPayments.Commands.RecordSaaSPaymentCharge;
using TaxVision.PaymentApp.Application.SaaSPayments.Common;
using TaxVision.PaymentApp.Domain.SaaSPayments;

namespace TaxVision.PaymentApp.Tests.TestDoubles;

/// <summary>Corre en línea lo que en producción hace la cola durable: toma los comandos de cobro publicados y ejecuta
/// el intento, la llamada al provider y el registro del resultado, con las dependencias de la prueba.</summary>
internal static class TestChargeExecution
{
    public static async Task DrainAsync(
        CapturingMessageBus bus,
        FakeSaaSPaymentRepository payments,
        IOffSessionChargerFactory? offSession = null,
        IPaymentAdapterFactory? adapters = null,
        FakePaymentAppMetrics? metrics = null
    )
    {
        payments.ById ??= payments.Added;
        var usedMetrics = metrics ?? new FakePaymentAppMetrics();
        bus.Route = message =>
            message switch
            {
                ClaimSaaSPaymentChargeCommand claim => ClaimSaaSPaymentChargeHandler.Handle(
                    claim,
                    payments,
                    new FakeUnitOfWork(),
                    Options.Create(new SaaSPaymentChargeOptions()),
                    NullLogger<SaaSPayment>.Instance,
                    CancellationToken.None
                ),
                RecordSaaSPaymentChargeCommand record => RecordSaaSPaymentChargeHandler.Handle(
                    record,
                    payments,
                    new FakePaymentAuditLogWriter(),
                    new FakeUnitOfWork(),
                    usedMetrics,
                    new FakeCorrelationContext(),
                    bus,
                    NullLogger<SaaSPayment>.Instance,
                    CancellationToken.None
                ),
                _ => throw new NotSupportedException(message.GetType().Name),
            };

        foreach (var command in bus.Published.OfType<ExecuteSaaSPaymentChargeCommand>().ToList())
            await ExecuteSaaSPaymentChargeHandler.Handle(
                command,
                bus,
                adapters ?? new FakePaymentAdapterFactory(new FakePaymentProvider()),
                offSession!,
                new FakeCorrelationContext(),
                NullLogger<SaaSPayment>.Instance,
                CancellationToken.None
            );
    }
}
