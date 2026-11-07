using BuildingBlocks.Results;
using BuildingBlocks.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Application.Abstractions.Payments;
using TaxVision.PaymentClient.Application.TenantPayments.Commands.RecordTenantPaymentCharge;
using TaxVision.PaymentClient.Application.TenantPayments.Common;
using TaxVision.PaymentClient.Domain.TenantPayments;
using TaxVision.PaymentClient.Domain.ValueObjects;
using Wolverine;
using Wolverine.Attributes;

namespace TaxVision.PaymentClient.Application.TenantPayments.Commands.ExecuteTenantPaymentCharge;

/// <summary>Paso 2 del cobro (BB-8): llama al proveedor con lo que la intención dejó en la fila. Con
/// <paramref name="PaymentLinkId"/>, el resultado también cierra el canje de ese link.</summary>
public sealed record ExecuteTenantPaymentChargeCommand(
    Guid TenantId,
    Guid TenantPaymentId,
    Guid ActorUserId,
    Guid? PaymentLinkId = null
);

/// <summary>Estado del pago tras el cobro. <paramref name="ClientSecret"/>: solo en 3DS de Stripe, para la página de
/// pago; no se guarda ni se escribe en logs.</summary>
public sealed record TenantPaymentChargeView(
    Guid TenantPaymentId,
    PaymentStatus Status,
    string? NextActionType,
    string? NextActionUrl,
    string? FailureCode,
    string? FailureReason,
    string? ClientSecret = null
)
{
    public static TenantPaymentChargeView From(TenantPayment payment, string? clientSecret = null) =>
        new(
            payment.Id,
            payment.Status,
            payment.NextActionType,
            payment.NextActionUrl,
            payment.FailureCode,
            payment.FailureReason,
            clientSecret
        );

    // El secreto no sale en ToString (logs).
    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append($"TenantPaymentId = {TenantPaymentId}, Status = {Status}");
        return true;
    }
}

public static class ExecuteTenantPaymentChargeHandler
{
    /// <summary>El proveedor pudo cobrar y no hay resultado (no lo dijo, o no se guardó): la fila sigue y la cierra la
    /// reconciliación.</summary>
    public const string OutcomeUnknownCode = "TenantPayment.ChargeOutcomeUnknown";

    // Sin transacción: la llamada al proveedor no se revierte, así que tampoco debe retener una abierta.
    [NonTransactional]
    public static async Task<Result<TenantPaymentChargeView>> Handle(
        ExecuteTenantPaymentChargeCommand command,
        ITenantPaymentRepository payments,
        ITenantPaymentConfigRepository configs,
        ITenantConnectAccountRepository connectAccounts,
        IPaymentAdapterFactory providerFactory,
        ISecretProtector secretProtector,
        IOptions<PlatformStripeCredentials> platformCredentials,
        IMessageBus bus,
        ILogger<TenantPayment> logger,
        CancellationToken ct
    )
    {
        var payment = await payments.GetByIdAsync(command.TenantPaymentId, command.TenantId, ct);
        if (payment is null)
            return Result.Failure<TenantPaymentChargeView>(
                new Error("TenantPayment.NotFound", "TenantPayment does not exist.")
            );

        // Un webhook o la reconciliación ya lo resolvieron.
        if (payment.Status != PaymentStatus.Pending)
            return Result.Success(TenantPaymentChargeView.From(payment));

        var charge = await ChargeAsync(
            payment,
            configs,
            connectAccounts,
            providerFactory,
            secretProtector,
            platformCredentials.Value,
            ct
        );

        // D-PAY-13: sin resultado no se cierra. Un Failed abriría otro intento con clave nueva.
        if (charge.IsSuccess && charge.Value.OutcomeUnknown)
        {
            logger.LogWarning(
                "TenantPayment {TenantPaymentId} charge has no known outcome ({FailureCode}); reconciliation will resolve it.",
                command.TenantPaymentId,
                charge.Value.FailureCode
            );
            return Result.Failure<TenantPaymentChargeView>(
                new Error(OutcomeUnknownCode, "The provider did not confirm the charge yet.")
            );
        }

        var record = new RecordTenantPaymentChargeCommand(
            command.TenantId,
            command.TenantPaymentId,
            charge.IsSuccess ? charge.Value : null,
            charge.IsFailure ? charge.Error : null,
            command.ActorUserId,
            command.PaymentLinkId
        );

        try
        {
            return await bus.InvokeAsync<Result<TenantPaymentChargeView>>(record, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(
                ex,
                "TenantPayment {TenantPaymentId} was sent to the provider but its result could not be saved; reconciliation will resolve it.",
                command.TenantPaymentId
            );
            return Result.Failure<TenantPaymentChargeView>(
                new Error(OutcomeUnknownCode, "The charge result could not be saved yet.")
            );
        }
    }

    private static async Task<Result<ChargeAuthorizationResult>> ChargeAsync(
        TenantPayment payment,
        ITenantPaymentConfigRepository configs,
        ITenantConnectAccountRepository connectAccounts,
        IPaymentAdapterFactory providerFactory,
        ISecretProtector secretProtector,
        PlatformStripeCredentials platformCredentials,
        CancellationToken ct
    )
    {
        var access = await TenantChargeAccess.ResolveAsync(
            payment,
            configs,
            connectAccounts,
            secretProtector,
            platformCredentials,
            ct
        );
        if (access.IsFailure)
            return Result.Failure<ChargeAuthorizationResult>(access.Error);

        return await providerFactory
            .Resolve(payment.ProviderCode)
            .AuthorizeChargeAsync(access.Value.Credentials, BuildChargeRequest(payment, access.Value.OnBehalfOf), ct);
    }

    // Todo sale de la fila: un reintento manda exactamente lo mismo con la misma clave.
    private static ChargeAuthorizationRequest BuildChargeRequest(TenantPayment payment, string? onBehalfOf) =>
        new(
            PaymentMethod: new PaymentMethodToken(payment.ChargeMethodReference!),
            Amount: payment.Amount,
            IdempotencyKey: payment.IdempotencyKey,
            Descriptor: payment.StatementDescriptor,
            ReceiptEmail: payment.Payer?.ReceiptEmail,
            Metadata: new Dictionary<string, string>
            {
                ["tenantId"] = payment.TenantId.ToString("N"),
                ["tenantPaymentId"] = payment.Id.ToString("N"),
            },
            OnBehalfOf: onBehalfOf,
            ApplicationFee: payment.SplitPayment is { } split
                ? Money.Create(split.PlatformFeeAmountCents, payment.Amount.Currency).Value
                : null
        );
}
