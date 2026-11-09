using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.SaaSPayments.Common;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Application.SaaSPayments.Commands.ClaimSaaSPaymentCharge;

/// <summary>Graba el intento en una transacción corta, antes de llamar al provider. La <c>RowVersion</c> de esa
/// escritura separa este cobro de la reconciliación: solo uno de los dos sigue adelante.</summary>
public sealed record ClaimSaaSPaymentChargeCommand(Guid TenantId, Guid SaaSPaymentId);

/// <summary>Lo que el provider tiene que recibir. Sale de la fila, no del comando: es el mismo en cada reintento.</summary>
public sealed record SaaSPaymentChargeAttempt(
    PaymentProviderCode Provider,
    Money Amount,
    IdempotencyKey ProviderKey,
    StatementDescriptor Descriptor
);

public static class ClaimSaaSPaymentChargeHandler
{
    public static async Task<Result<SaaSPaymentChargeAttempt?>> Handle(
        ClaimSaaSPaymentChargeCommand command,
        ISaaSPaymentRepository payments,
        IUnitOfWork unitOfWork,
        IOptions<SaaSPaymentChargeOptions> options,
        ILogger<SaaSPayment> logger,
        CancellationToken ct
    )
    {
        var payment = await payments.GetByIdAsync(command.SaaSPaymentId, command.TenantId, ct);
        if (payment is null)
            return Result.Failure<SaaSPaymentChargeAttempt?>(
                new Error("SaaSPayment.NotFound", "SaaSPayment does not exist.")
            );

        // Solo Pending: en Processing ya hay un intento registrado y su clave cambió; repetir cobraría de nuevo.
        if (payment.Status != PaymentStatus.Pending)
            return Result.Success<SaaSPaymentChargeAttempt?>(null);

        if (payment.ReviewReasonCode == SaaSPayment.ReviewReasonChargeOutcomeUnknown)
            return Result.Success<SaaSPaymentChargeAttempt?>(null);

        var nowUtc = DateTime.UtcNow;
        if (payment.ChargeAttemptedAtUtc is { } attemptedAt && nowUtc - attemptedAt > options.Value.ProviderKeyWindow)
        {
            logger.LogWarning(
                "SaaSPayment {SaaSPaymentId} was attempted at {AttemptedAtUtc}, past the provider key window; reconciliation closes it, it is not charged again.",
                payment.Id,
                attemptedAt
            );
            return Result.Success<SaaSPaymentChargeAttempt?>(null);
        }

        var marked = payment.MarkChargeAttempted(nowUtc);
        if (marked.IsFailure)
            return Result.Failure<SaaSPaymentChargeAttempt?>(marked.Error);

        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success<SaaSPaymentChargeAttempt?>(
            new SaaSPaymentChargeAttempt(
                payment.ProviderCode,
                payment.Amount,
                payment.ProviderIdempotencyKeyForNextAttempt(),
                payment.StatementDescriptor
            )
        );
    }
}
