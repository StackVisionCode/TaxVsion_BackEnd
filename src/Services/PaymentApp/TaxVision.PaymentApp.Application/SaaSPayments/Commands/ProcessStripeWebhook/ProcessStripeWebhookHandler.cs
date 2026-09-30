using BuildingBlocks.Common;
using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Application.Common;
using TaxVision.PaymentApp.Application.SaaSPayments.Commands.ProcessProviderWebhook;
using TaxVision.PaymentApp.Domain.Audit;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;
using TaxVision.PaymentApp.Domain.Webhooks;
using Wolverine;

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
        IMessageBus bus,
        ITenantRegistry tenants,
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
            bus,
            tenants,
            logger,
            ct
        );

    public static async ValueTask PublishOnboardingResultAsync(
        SaaSPayment payment,
        IMessageBus bus,
        string correlationId,
        CancellationToken ct
    )
    {
        if (payment.Status == PaymentStatus.Succeeded)
        {
            await bus.PublishAsync(
                new OnboardingPaymentSucceededIntegrationEvent
                {
                    TenantId = PlatformTenant.Id,
                    OnboardingId = payment.OnboardingId!.Value,
                    SaaSPaymentId = payment.Id,
                    PlanId = payment.TargetAggregateId,
                    AmountPaidCents = payment.Amount.AmountCents,
                    Currency = payment.Amount.Currency,
                    PaidAtUtc = payment.PaidAtUtc ?? DateTime.UtcNow,
                    ProviderPaymentReference = payment.ExternalChargeReference?.Value ?? string.Empty,
                    CorrelationId = correlationId,
                }
            );
        }
        else if (payment.Status is PaymentStatus.Failed or PaymentStatus.Cancelled)
        {
            // Para un onboarding, un cargo cancelado (ej. la sesión de checkout expiró y el proveedor
            // anuló el intento) es tan terminal como uno declinado: el comprador no pagó. Se notifica igual.
            var cancelled = payment.Status == PaymentStatus.Cancelled;
            await bus.PublishAsync(
                new OnboardingPaymentFailedIntegrationEvent
                {
                    TenantId = PlatformTenant.Id,
                    OnboardingId = payment.OnboardingId!.Value,
                    SaaSPaymentId = payment.Id,
                    FailureCode = payment.FailureCode ?? (cancelled ? "Provider.Cancelled" : "Unknown"),
                    FailureReason =
                        payment.FailureReason ?? (cancelled ? "The payment was cancelled." : "The charge failed."),
                    CorrelationId = correlationId,
                }
            );
        }
    }
}
