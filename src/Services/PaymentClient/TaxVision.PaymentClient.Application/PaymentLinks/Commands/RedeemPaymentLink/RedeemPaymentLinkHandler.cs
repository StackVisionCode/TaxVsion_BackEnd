using BuildingBlocks.Results;
using TaxVision.PaymentClient.Application.PaymentLinks.Commands.PrepareLinkRedemption;
using TaxVision.PaymentClient.Application.TenantPayments.Commands.ExecuteTenantPaymentCharge;
using TaxVision.PaymentClient.Domain.PaymentLinks;
using TaxVision.PaymentClient.Domain.TenantPayments;
using Wolverine;
using Wolverine.Attributes;

namespace TaxVision.PaymentClient.Application.PaymentLinks.Commands.RedeemPaymentLink;

/// <summary>
/// Cobra el monto fijado en el <c>PaymentLink</c> contra el método de pago que el taxpayer
/// tokenizó en la página de checkout. Tras un intento fallido (tarjeta declinada) se crea un
/// <c>TenantPayment</c> nuevo para reintentar con otra tarjeta. Resguardos contra doble cobro:
/// <see cref="PaymentLink.IsRedeemable"/>, <see cref="PaymentLink.AttachPaymentAttempt"/> (no abre
/// otro intento si hay uno vivo o cobrado), la clave fija por intento
/// (<c>paymentlink-redeem:{linkId}:attempt:{n}</c>) y el <c>RowVersion</c> del link. El intento se
/// confirma antes de llamar al proveedor (patrón BB-8): sin transacción abierta durante el cobro.
/// </summary>
public static class RedeemPaymentLinkHandler
{
    [NonTransactional]
    public static async Task<Result<RedeemPaymentLinkResponse>> Handle(
        RedeemPaymentLinkCommand command,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var prepared = await bus.InvokeAsync<Result<LinkRedemptionPlan>>(new PrepareLinkRedemptionCommand(command), ct);
        if (prepared.IsFailure)
            return Result.Failure<RedeemPaymentLinkResponse>(prepared.Error);

        var plan = prepared.Value;
        if (plan.Finished is { } finished)
            return Result.Success(finished);

        var charged = await bus.InvokeAsync<Result<TenantPaymentChargeView>>(
            new ExecuteTenantPaymentChargeCommand(plan.TenantId, plan.TenantPaymentId, Guid.Empty, plan.PaymentLinkId),
            ct
        );

        // Resultado sin guardar: para el pagador es un intento en curso; lo cierra la reconciliación.
        if (charged.IsFailure)
            return Result.Failure<RedeemPaymentLinkResponse>(
                charged.Error.Code == ExecuteTenantPaymentChargeHandler.OutcomeUnknownCode
                    ? PaymentLink.AttemptInProgressError
                    : charged.Error
            );

        var view = charged.Value;
        return Result.Success(
            new RedeemPaymentLinkResponse(
                view.TenantPaymentId,
                view.Status.ToString(),
                view.NextActionType,
                view.NextActionUrl,
                view.FailureCode,
                view.FailureReason,
                view.Status == PaymentStatus.RequiresAction ? view.ClientSecret : null
            )
        );
    }

    public static RedeemPaymentLinkResponse ToResponse(TenantPayment payment, string? clientSecret) =>
        new(
            payment.Id,
            payment.Status.ToString(),
            payment.NextActionType,
            payment.NextActionUrl,
            payment.FailureCode,
            payment.FailureReason,
            payment.Status == PaymentStatus.RequiresAction ? clientSecret : null
        );
}
