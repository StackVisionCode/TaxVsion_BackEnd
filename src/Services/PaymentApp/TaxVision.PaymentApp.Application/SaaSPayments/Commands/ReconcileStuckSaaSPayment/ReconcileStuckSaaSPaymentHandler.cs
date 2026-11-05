using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Application.SaaSPayments.Common;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.PaymentApp.Application.SaaSPayments.Commands.ReconcileStuckSaaSPayment;

/// <summary>
/// Resuelve un pago atascado en <see cref="PaymentStatus.Processing"/> consultando al provider
/// (nunca asume nada sobre un cobro que no se confirmó localmente). Guarda y después publica por el
/// outbox del handler: si el guardado choca con el webhook, no sale ningún evento.
/// </summary>
public static class ReconcileStuckSaaSPaymentHandler
{
    private static readonly TimeSpan ProviderNotFoundGrace = TimeSpan.FromMinutes(30);

    public static async Task<Result> Handle(
        ReconcileStuckSaaSPaymentCommand command,
        ISaaSPaymentRepository payments,
        IPaymentAdapterFactory providerFactory,
        ITenantRegistry tenants,
        ITenantProviderCustomerRepository customers,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<SaaSPayment> logger,
        CancellationToken ct
    )
    {
        var payment = await payments.GetByIdAsync(command.SaaSPaymentId, command.TenantId, ct);
        if (payment is null)
            return Result.Failure(new Error("SaaSPayment.NotFound", "SaaSPayment does not exist."));

        // El webhook pudo resolverlo entre la búsqueda del job y este comando.
        if (payment.Status != PaymentStatus.Processing || payment.ExternalChargeReference is null)
            return Result.Success();

        // Un pago en revisión lo resuelve el admin, no el job (evita confirmar un monto distinto).
        if (payment.ReviewRequiredAtUtc is not null)
            return Result.Success();

        var change = await ResolveAsync(payment, providerFactory.Resolve(payment.ProviderCode), logger, ct);
        if (change == Change.None)
            return Result.Success();

        await unitOfWork.SaveChangesAsync(ct);

        if (change == Change.Transitioned)
        {
            await SaaSPaymentResultPublisher.PublishAsync(
                payment,
                bus,
                correlation.CorrelationId,
                ct,
                tenants,
                customers
            );
            logger.LogInformation(
                "Stuck SaaSPayment {SaaSPaymentId} reconciled to {Status}.",
                payment.Id,
                payment.Status
            );
        }

        return Result.Success();
    }

    private enum Change
    {
        None,
        ReferenceOnly,
        Transitioned,
    }

    private static async Task<Change> ResolveAsync(
        SaaSPayment payment,
        IPaymentProvider adapter,
        ILogger logger,
        CancellationToken ct
    )
    {
        // Un pago por hosted checkout guarda el id de la SESIÓN (cs_...): hay que finalizarla para conocer
        // su cargo real; GetChargeStatusAsync espera un PaymentIntent.
        Result<ChargeAuthorizationResult> statusResult;
        try
        {
            statusResult = !string.IsNullOrWhiteSpace(payment.ProviderCheckoutSessionId)
                ? await adapter.FinalizeHostedCheckoutAsync(payment.ExternalChargeReference!.Value, payment.Amount, ct)
                : await adapter.GetChargeStatusAsync(payment.ExternalChargeReference!.Value, ct);
        }
        catch (Exception ex) when (IsProviderTransportFailure(ex, ct))
        {
            logger.LogWarning(
                ex,
                "Could not confirm status for stuck SaaSPayment {SaaSPaymentId}: provider request failed.",
                payment.Id
            );
            return Change.None;
        }

        var nowUtc = DateTime.UtcNow;
        if (statusResult.IsFailure)
        {
            if (ShouldFailProviderNotFound(payment, statusResult.Error, nowUtc))
            {
                var notFound = payment.MarkFailed(
                    statusResult.Error.Code,
                    statusResult.Error.Message,
                    willRetry: false,
                    nextRetryAtUtc: null,
                    Guid.Empty,
                    nowUtc
                );
                return notFound.IsSuccess ? Change.Transitioned : Change.None;
            }

            logger.LogWarning(
                "Could not confirm status for stuck SaaSPayment {SaaSPaymentId}: {Error}",
                payment.Id,
                statusResult.Error.Message
            );
            return Change.None;
        }

        var outcome = statusResult.Value;
        var change = Change.None;

        // Mismo criterio que checkout.session.completed: si el provider ya resolvió una referencia más
        // autoritativa (el PaymentIntent detrás de la sesión), se guarda para refund/dispute futuros.
        if (outcome.ProviderChargeReference != payment.ExternalChargeReference!.Value)
        {
            var referenceResult = ExternalPaymentReference.Create(
                payment.ProviderCode,
                outcome.ProviderChargeReference
            );
            if (
                referenceResult.IsSuccess
                && payment.ReconcileProviderChargeReference(referenceResult.Value, nowUtc).IsSuccess
            )
                change = Change.ReferenceOnly;
        }

        switch (outcome.Status)
        {
            case PaymentStatus.Succeeded:
                return payment.MarkSucceeded(nowUtc, Guid.Empty).IsSuccess ? Change.Transitioned : change;

            case PaymentStatus.Failed
            or PaymentStatus.Cancelled:
                var nextRetryAtUtc = SaaSPaymentChargeOutcome.ComputeNextRetryAtUtc(
                    payment,
                    nowUtc,
                    failedAttemptRecorded: true
                );
                var failed = payment.MarkFailed(
                    outcome.FailureCode ?? "Provider.Unknown",
                    outcome.FailureMessage ?? "The provider reported the charge as failed.",
                    willRetry: nextRetryAtUtc is not null,
                    nextRetryAtUtc,
                    Guid.Empty,
                    nowUtc
                );
                return failed.IsSuccess ? Change.Transitioned : change;

            default:
                // Sigue Processing/RequiresAction en el provider: la próxima corrida vuelve a preguntar.
                return change;
        }
    }

    private static bool ShouldFailProviderNotFound(SaaSPayment payment, Error error, DateTime nowUtc) =>
        IsProviderNotFound(error) && payment.UpdatedAtUtc <= nowUtc - ProviderNotFoundGrace;

    private static bool IsProviderNotFound(Error error) =>
        error.Code.EndsWith(".ChargeStatus.NotFound", StringComparison.Ordinal)
        || error.Message.Contains("HTTP 404", StringComparison.OrdinalIgnoreCase);

    private static bool IsProviderTransportFailure(Exception ex, CancellationToken ct) =>
        !ct.IsCancellationRequested && ex is OperationCanceledException or HttpRequestException or TimeoutException;
}
