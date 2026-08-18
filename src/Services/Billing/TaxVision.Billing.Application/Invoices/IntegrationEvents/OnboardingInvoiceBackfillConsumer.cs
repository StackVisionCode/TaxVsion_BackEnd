using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Domain.Invoices;

namespace TaxVision.Billing.Application.Invoices.IntegrationEvents;

/// <summary>
/// Backfill del tenant real en la factura de onboarding. La factura nació bajo <c>PlatformTenant.Id</c>
/// (pre-tenant); cuando la saga crea el Tenant real (<see cref="TenantCreatedForOnboardingIntegrationEvent"/>,
/// que ya trae el tenant real), se re-hospeda para que aparezca en el historial de facturación del tenant.
/// Idempotente: si ya está re-hospedada (o no hay factura de onboarding) es no-op. El middleware ya
/// restauró el tenant real del sobre → coincide con el nuevo dueño que estampamos.
/// </summary>
public static class OnboardingInvoiceBackfillConsumer
{
    public static async Task Handle(
        TenantCreatedForOnboardingIntegrationEvent evt,
        IInvoiceRepository invoices,
        IUnitOfWork unitOfWork,
        TimeProvider clock,
        ICorrelationContext correlation,
        ILogger<Invoice> logger,
        CancellationToken ct
    )
    {
        using var _ = correlation.Push(
            string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId
        );

        var invoice = await invoices.GetByOnboardingIdAsync(evt.OnboardingId, ct);
        if (invoice is null)
            return; // Onboarding sin factura (aún no asentada o flujo sin código); nada que re-hospedar.

        var result = invoice.RehomeToTenant(evt.CreatedTenantId, clock.GetUtcNow().UtcDateTime);
        if (result.IsFailure)
        {
            logger.LogWarning(
                "Could not re-home onboarding invoice {InvoiceId} to tenant {TenantId}: {Code} - {Message}",
                invoice.Id,
                evt.CreatedTenantId,
                result.Error.Code,
                result.Error.Message
            );
            return;
        }

        await unitOfWork.SaveChangesAsync(ct);
        logger.LogInformation(
            "Onboarding invoice {InvoiceId} re-homed to tenant {TenantId}.",
            invoice.Id,
            evt.CreatedTenantId
        );
    }
}
