using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Application.Common;
using TaxVision.PaymentApp.Application.Common.HostedCheckout;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Application.WalletTopUpCheckouts.Commands;

/// <summary>
/// Crea la sesión de checkout hosteada para una recarga de monedero por redirect. Los pasos comunes
/// (resolver proveedor, idempotencia/replay, crear la sesión, auditar) los pone
/// <see cref="HostedCheckoutPipeline"/>; acá solo vive lo propio de la recarga. El webhook (source of truth)
/// confirma el pago y <see cref="Common.SaaSPaymentResultPublisher"/> publica
/// <c>WalletTopUpPaymentSucceeded</c>, que el Wallet consume para acreditar — SIN cambios en esa cola.
/// </summary>
public static class CreateWalletTopUpCheckoutHandler
{
    private const string DefaultStatementDescriptor = "TAXVISION WALLET";

    public static async Task<Result<WalletTopUpCheckoutResponse>> Handle(
        CreateWalletTopUpCheckoutCommand command,
        ISaaSPaymentRepository payments,
        IPaymentAdapterFactory providerFactory,
        IPaymentAuditLogWriter audit,
        IUnitOfWork unitOfWork,
        IPaymentAppMetrics metrics,
        ICorrelationContext correlation,
        ILogger<SaaSPayment> logger,
        CancellationToken ct
    )
    {
        var request = new HostedCheckoutRequest(
            command.IdempotencyKey,
            command.PayerEmail,
            command.SuccessUrl,
            command.CancelUrl,
            command.Provider,
            command.Method
        );

        var result = await HostedCheckoutPipeline.RunAsync(
            request,
            PolicyFor(command),
            payments,
            providerFactory,
            audit,
            unitOfWork,
            metrics,
            correlation,
            logger,
            ct
        );

        return result.IsFailure
            ? Result.Failure<WalletTopUpCheckoutResponse>(result.Error)
            : Result.Success(
                new WalletTopUpCheckoutResponse(
                    result.Value.PaymentId,
                    result.Value.CheckoutUrl,
                    result.Value.ProviderSessionId,
                    result.Value.ExpiresAtUtc
                )
            );
    }

    private static HostedCheckoutPolicy PolicyFor(CreateWalletTopUpCheckoutCommand command) =>
        new()
        {
            PaymentType = SaaSPaymentType.WalletTopUp,
            Subject = "Wallet top-up checkout",
            ReferenceId = command.TopUpId,
            StatementDescriptor = DefaultStatementDescriptor,
            // El Wallet (dueño del saldo) ya fijó el monto de la recarga.
            ResolveAmount = _ => Task.FromResult(Money.Create(command.AmountCents, command.Currency)),
            CreatePayment = (key, amount, descriptor, nowUtc) =>
                SaaSPayment.Create(
                    command.TenantId,
                    key,
                    amount,
                    SaaSPaymentType.WalletTopUp,
                    command.TopUpId,
                    command.Provider,
                    descriptor,
                    actorUserId: Guid.Empty,
                    nowUtc
                ),
            DecideOnExisting = DecideOnExisting,
            BuildMetadata = payment => new Dictionary<string, string>
            {
                ["tenantId"] = command.TenantId.ToString("N"),
                ["walletTopUpId"] = command.TopUpId.ToString("N"),
                ["saaSPaymentId"] = payment.Id.ToString("N"),
            },
            BuildAuditPayload = (payment, session) =>
                new
                {
                    payment.Status,
                    WalletTopUpId = command.TopUpId,
                    session.ProviderSessionId,
                },
        };

    // Clave única por orden de recarga: un re-submit de la MISMA recarga replaya su sesión; una recarga nueva
    // crea un pago nuevo. Una recarga fallida se reintenta como una recarga nueva, con su propia clave.
    private static Result<ExistingPaymentDecision> DecideOnExisting(SaaSPayment existing, DateTime nowUtc) =>
        HostedCheckoutPipeline.TryBuildResponse(existing) is { } replay
            ? Result.Success(ExistingPaymentDecision.Replay(replay))
            : Result.Failure<ExistingPaymentDecision>(
                new Error(
                    "WalletTopUp.Checkout.NotReplayable",
                    "A checkout already exists for this top-up but has no usable session."
                )
            );
}
