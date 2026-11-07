using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Application.SaaSPayments.Common;
using TaxVision.PaymentApp.Domain.SaaSPayments;

namespace TaxVision.PaymentApp.Application.SaaSPayments.Commands.ReconcilePendingSaaSPayment;

/// <summary>
/// Cierra un pago <c>Pending</c> cuyo comando de cobro se perdió o no dejó resultado. Regla de BB-8: con intento
/// grabado nunca se reenvía a ciegas. Se pregunta al provider y solo sin cargo confirmado pasa a <c>Failed</c>, para
/// que el dunning reintente con una clave nueva. Si el provider no permite buscar, queda para revisión manual.
/// </summary>
public sealed record ReconcilePendingSaaSPaymentCommand(Guid TenantId, Guid SaaSPaymentId);

public static class ReconcilePendingSaaSPaymentHandler
{
    private static readonly Error NotCharged = new(
        "SaaSPayment.ChargeNotConfirmed",
        "The charge never reached the provider; it will be retried."
    );

    public static async Task<Result> Handle(
        ReconcilePendingSaaSPaymentCommand command,
        ISaaSPaymentRepository payments,
        IPaymentAdapterFactory providerFactory,
        IUnitOfWork unitOfWork,
        IPaymentAppMetrics metrics,
        IOptions<SaaSPaymentChargeOptions> options,
        ILogger<SaaSPayment> logger,
        CancellationToken ct
    )
    {
        var payment = await payments.GetByIdAsync(command.SaaSPaymentId, command.TenantId, ct);
        if (payment is null)
            return Result.Failure(new Error("SaaSPayment.NotFound", "SaaSPayment does not exist."));

        if (payment.Status != PaymentStatus.Pending || payment.ReviewRequiredAtUtc is not null)
            return Result.Success();

        // El comando pudo grabar el intento entre la búsqueda del job y este comando: sigue en curso.
        var nowUtc = DateTime.UtcNow;
        if (payment.UpdatedAtUtc > nowUtc - options.Value.PendingChargeThreshold)
            return Result.Success();

        var nextRetryAtUtc = SaaSPaymentChargeOutcome.ComputeNextRetryAtUtc(payment, nowUtc);

        if (payment.ChargeAttemptedAtUtc is not { } attemptedAt)
        {
            SaaSPaymentChargeOutcome.FailPayment(payment, NotCharged, Guid.Empty, nextRetryAtUtc, metrics);
            await unitOfWork.SaveChangesAsync(ct);
            return Result.Success();
        }

        ChargeLookup lookup;
        try
        {
            var found = await providerFactory
                .Resolve(payment.ProviderCode)
                .FindChargeAsync(payment.Id, attemptedAt - options.Value.ProviderClockSkew, ct);
            if (found.IsFailure)
            {
                logger.LogWarning(
                    "Could not look up the charge of pending SaaSPayment {SaaSPaymentId}: {Error}",
                    payment.Id,
                    found.Error.Message
                );
                return Result.Success();
            }
            lookup = found.Value;
        }
        catch (Exception ex) when (IsProviderTransportFailure(ex, ct))
        {
            logger.LogWarning(ex, "Could not look up the charge of pending SaaSPayment {SaaSPaymentId}.", payment.Id);
            return Result.Success();
        }

        switch (lookup.Outcome)
        {
            case ChargeLookupOutcome.Found:
                SaaSPaymentChargeOutcome.ApplyChargeOutcome(
                    payment,
                    lookup.Charge!,
                    Guid.Empty,
                    nextRetryAtUtc,
                    metrics
                );
                break;
            case ChargeLookupOutcome.NotFound:
                SaaSPaymentChargeOutcome.FailPayment(payment, NotCharged, Guid.Empty, nextRetryAtUtc, metrics);
                break;
            default:
                LateProviderSuccessPolicy.Flag(
                    payment,
                    SaaSPayment.ReviewReasonChargeOutcomeUnknown,
                    providerReference: null,
                    paidAmountCents: null,
                    paidCurrency: null,
                    metrics,
                    logger,
                    nowUtc
                );
                break;
        }

        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation(
            "Pending SaaSPayment {SaaSPaymentId} reconciled: lookup {Outcome}, status {Status}.",
            payment.Id,
            lookup.Outcome,
            payment.Status
        );
        return Result.Success();
    }

    private static bool IsProviderTransportFailure(Exception ex, CancellationToken ct) =>
        !ct.IsCancellationRequested && ex is OperationCanceledException or HttpRequestException or TimeoutException;
}
