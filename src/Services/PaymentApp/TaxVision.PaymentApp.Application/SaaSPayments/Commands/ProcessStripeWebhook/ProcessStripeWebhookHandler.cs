using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Application.SaaSPayments.Commands.ProcessProviderWebhook;
using TaxVision.PaymentApp.Domain.ValueObjects;
using TaxVision.PaymentApp.Domain.Webhooks;

namespace TaxVision.PaymentApp.Application.SaaSPayments.Commands.ProcessStripeWebhook;

public static class ProcessStripeWebhookHandler
{
    public static Task<Result> Handle(
        ProcessStripeWebhookCommand command,
        IPaymentAdapterFactory providerFactory,
        IProviderWebhookSecrets webhookSecrets,
        IWebhookEventRepository webhookEvents,
        ISaaSPaymentRepository payments,
        IPaymentAuditLogWriter audit,
        IUnitOfWork unitOfWork,
        IPaymentAppMetrics metrics,
        IPaymentAttemptThrottle throttle,
        ICorrelationContext correlation,
        ILogger<WebhookEvent> logger,
        CancellationToken ct
    ) =>
        ProcessProviderWebhookHandler.ProcessAsync(
            PaymentProviderCode.Stripe,
            command.RawPayload,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Stripe-Signature"] = command.SignatureHeader,
            },
            providerFactory,
            webhookSecrets,
            webhookEvents,
            payments,
            audit,
            unitOfWork,
            metrics,
            throttle,
            correlation,
            logger,
            ct
        );
}
