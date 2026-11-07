using BuildingBlocks.Common;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Application.SaaSPayments.Commands.ClaimSaaSPaymentCharge;
using TaxVision.PaymentApp.Application.SaaSPayments.Commands.RecordSaaSPaymentCharge;
using TaxVision.PaymentApp.Application.SaaSPayments.Common;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;
using Wolverine;
using Wolverine.Attributes;

namespace TaxVision.PaymentApp.Application.SaaSPayments.Commands.ExecuteSaaSPaymentCharge;

/// <summary>
/// Paso 2 del cobro (patrón BB-8): sin transacción abierta. Graba el intento, llama al provider y registra el
/// resultado en otra transacción. Si el registro falla, la excepción sale y Wolverine reintenta: el intento sigue sin
/// resultado, así que se manda la misma clave y el provider devuelve el mismo cargo.
/// </summary>
public static class ExecuteSaaSPaymentChargeHandler
{
    [NonTransactional]
    public static async Task Handle(
        ExecuteSaaSPaymentChargeCommand command,
        IMessageBus bus,
        IPaymentAdapterFactory providerFactory,
        IOffSessionChargerFactory offSessionFactory,
        ICorrelationContext correlation,
        ILogger<SaaSPayment> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(command.CorrelationId ?? command.SaaSPaymentId.ToString("N")))
        {
            var claim = await bus.InvokeAsync<Result<SaaSPaymentChargeAttempt?>>(
                new ClaimSaaSPaymentChargeCommand(command.TenantId, command.SaaSPaymentId),
                ct
            );
            if (claim.IsFailure)
            {
                logger.LogWarning(
                    "SaaSPayment {SaaSPaymentId} charge not started: {Code}.",
                    command.SaaSPaymentId,
                    claim.Error.Code
                );
                return;
            }

            if (claim.Value is not { } attempt)
                return;

            var record =
                command.Strategy == SaaSPaymentChargeStrategy.OffSession
                    ? await ChargeOffSessionAsync(command, attempt, offSessionFactory, ct)
                    : await AuthorizeAsync(command, attempt, providerFactory, ct);

            await bus.InvokeAsync<Result>(record, ct);
        }
    }

    private static async Task<RecordSaaSPaymentChargeCommand> AuthorizeAsync(
        ExecuteSaaSPaymentChargeCommand command,
        SaaSPaymentChargeAttempt attempt,
        IPaymentAdapterFactory providerFactory,
        CancellationToken ct
    )
    {
        var chargeRequest = new ChargeAuthorizationRequest(
            Customer: new ProviderCustomerToken(command.CustomerReference!, attempt.Provider),
            Amount: attempt.Amount,
            IdempotencyKey: attempt.ProviderKey,
            Descriptor: attempt.Descriptor,
            Metadata: new Dictionary<string, string>
            {
                ["tenantId"] = command.TenantId.ToString("N"),
                // Mismos datos que el primer cobro: con otra metadata, Stripe rechaza la clave reusada.
                ["saaSPaymentId"] = command.SaaSPaymentId.ToString("N"),
            },
            SpecificPaymentMethod: new PaymentMethodToken(command.MethodReference)
        );

        var charge = await providerFactory.Resolve(attempt.Provider).AuthorizeChargeAsync(chargeRequest, ct);
        return Recording(command, charge.IsSuccess ? charge.Value : null, null, charge.IsFailure ? charge.Error : null);
    }

    private static async Task<RecordSaaSPaymentChargeCommand> ChargeOffSessionAsync(
        ExecuteSaaSPaymentChargeCommand command,
        SaaSPaymentChargeAttempt attempt,
        IOffSessionChargerFactory offSessionFactory,
        CancellationToken ct
    )
    {
        var charger = offSessionFactory.For(attempt.Provider.ToString());
        if (charger is null)
            return Recording(
                command,
                null,
                null,
                new Error("OffSession.ChargerUnavailable", $"No off-session charger for {attempt.Provider}.")
            );

        var charge = await charger.ChargeAsync(
            new OffSessionChargeRequest(
                TenantId: command.TenantId,
                SaaSPaymentId: command.SaaSPaymentId,
                ProviderCode: attempt.Provider.ToString(),
                ProviderToken: command.MethodReference,
                AmountCents: attempt.Amount.AmountCents,
                Currency: attempt.Amount.Currency,
                IdempotencyKey: attempt.ProviderKey.Value,
                PayerEmail: SyntheticPayer.EmailFor(command.TenantId)
            ),
            ct
        );
        return Recording(command, null, charge.IsSuccess ? charge.Value : null, charge.IsFailure ? charge.Error : null);
    }

    private static RecordSaaSPaymentChargeCommand Recording(
        ExecuteSaaSPaymentChargeCommand command,
        ChargeAuthorizationResult? authorization,
        OffSessionChargeOutcome? offSession,
        Error? failure
    ) =>
        new(
            command.TenantId,
            command.SaaSPaymentId,
            command.Strategy,
            authorization,
            offSession,
            failure,
            command.ActorUserId,
            command.CorrelationId
        );
}
