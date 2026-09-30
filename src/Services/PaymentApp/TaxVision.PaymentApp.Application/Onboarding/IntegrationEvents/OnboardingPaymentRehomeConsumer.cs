using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Domain.SaaSPayments;

namespace TaxVision.PaymentApp.Application.Onboarding.IntegrationEvents;

/// <summary>
/// El tenant de un onboarding pago-primero ya existe: el pago inicial deja de vivir bajo el sentinel
/// <c>Guid.Empty</c> y pasa a su dueño real, para que aparezca en su historial. Molde:
/// <c>Billing.OnboardingInvoiceBackfillConsumer</c>, que hace lo mismo con la factura.
/// </summary>
public static class OnboardingPaymentRehomeConsumer
{
    public static async Task Handle(
        TenantCreatedForOnboardingIntegrationEvent evt,
        ISaaSPaymentRepository payments,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<SaaSPayment> logger,
        CancellationToken ct
    )
    {
        var correlationId = string.IsNullOrWhiteSpace(evt.CorrelationId)
            ? evt.EventId.ToString("N")
            : evt.CorrelationId;

        using (correlation.Push(correlationId))
        {
            var payment = await payments.GetByOnboardingIdAsync(evt.OnboardingId, ct);
            if (payment is null)
                return; // Onboarding sin pago (carril gratuito): no hay nada que re-hospedar.

            if (payment.TenantId == evt.CreatedTenantId)
                return; // Redelivery: ya es de ese tenant, no hay nada que escribir.

            var result = payment.RehomeToTenant(evt.CreatedTenantId, DateTime.UtcNow);
            if (result.IsFailure)
            {
                logger.LogWarning(
                    "Could not re-home onboarding payment {PaymentId} to tenant {TenantId}: {Code}.",
                    payment.Id,
                    evt.CreatedTenantId,
                    result.Error.Code
                );
                return;
            }

            await unitOfWork.SaveChangesAsync(ct);

            logger.LogInformation(
                "Onboarding payment {PaymentId} re-homed to tenant {TenantId}.",
                payment.Id,
                evt.CreatedTenantId
            );
        }
    }
}
