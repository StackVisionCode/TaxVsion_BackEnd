using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Application.Abstractions.Payments;
using TaxVision.PaymentClient.Application.TenantPayments.Commands.ReconcileTenantPayment;
using TaxVision.PaymentClient.Domain.TenantPayments;
using Wolverine;

namespace TaxVision.PaymentClient.Application.PaymentLinks.Commands.GetCheckoutPaymentStatus;

public static class GetCheckoutPaymentStatusHandler
{
    private static readonly Error NotFound = new("PaymentLink.NotFound", "PaymentLink does not exist.");

    public static async Task<Result<CheckoutPaymentStatusResponse>> Handle(
        GetCheckoutPaymentStatusCommand command,
        IPaymentLinkRepository links,
        ITenantPaymentRepository payments,
        ITenantPaymentConfigRepository configs,
        ITenantConnectAccountRepository connectAccounts,
        IPaymentAdapterFactory providerFactory,
        ISecretProtector secretProtector,
        IOptions<PlatformStripeCredentials> platformCredentials,
        IPaymentAuditLogWriter audit,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        IPaymentClientMetrics metrics,
        ICorrelationContext correlation,
        ILogger<TenantPayment> logger,
        CancellationToken ct
    )
    {
        var link = await links.GetByTokenAsync(command.LinkToken, ct);
        if (link is null || link.RelatedTenantPaymentId != command.TenantPaymentId)
            return Result.Failure<CheckoutPaymentStatusResponse>(NotFound);

        var payment = await payments.GetByIdAsync(command.TenantPaymentId, link.TenantId, ct);
        if (payment is null)
            return Result.Failure<CheckoutPaymentStatusResponse>(NotFound);

        // Sin TTL: desde la página nunca se cancela un intento; solo se aplica lo que diga el proveedor.
        if (payment.IsAwaitingProviderConfirmation)
        {
            var reconciled = await ReconcileTenantPaymentHandler.Handle(
                new ReconcileTenantPaymentCommand(link.TenantId, payment.Id, AbandonRequiresActionAfter: null),
                payments,
                configs,
                connectAccounts,
                providerFactory,
                secretProtector,
                platformCredentials,
                links,
                audit,
                unitOfWork,
                bus,
                metrics,
                correlation,
                logger,
                ct
            );
            if (reconciled.IsFailure)
                logger.LogInformation(
                    "Checkout status check for TenantPayment {TenantPaymentId} could not reach the provider: {ErrorCode}.",
                    payment.Id,
                    reconciled.Error.Code
                );
        }

        return Result.Success(new CheckoutPaymentStatusResponse(payment.Status.ToString()));
    }
}
