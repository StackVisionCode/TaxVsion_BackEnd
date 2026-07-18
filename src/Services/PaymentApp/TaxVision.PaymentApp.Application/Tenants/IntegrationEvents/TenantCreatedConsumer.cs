using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using BuildingBlocks.Persistence;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Domain.ProviderCustomers;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Application.Tenants.IntegrationEvents;

/// <summary>
/// Alimenta la proyección local <see cref="Domain.Tenants.Tenant"/> — PaymentApp nunca llama
/// a Auth/Tenant en el hot path de un cobro (§42.2 del diseño). Además aprovisiona eager un
/// <see cref="TenantProviderCustomer"/> en Stripe con el email real del admin (§D.4) — a
/// diferencia del email sintético que usan los consumers de renovación (Fase A/B), acá sí
/// tenemos el <see cref="TenantCreatedIntegrationEvent.AdminEmail"/> real. El Platform tenant
/// nunca es sujeto de cobro, así que no se le provisiona nada.
/// </summary>
public static class TenantCreatedConsumer
{
    public static async Task Handle(
        TenantCreatedIntegrationEvent evt,
        ITenantRegistry tenants,
        ITenantProviderCustomerRepository customers,
        IPaymentAdapterFactory providerFactory,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<TenantProviderCustomer> logger,
        CancellationToken ct
    )
    {
        var correlationId = string.IsNullOrWhiteSpace(evt.CorrelationId)
            ? evt.EventId.ToString("N")
            : evt.CorrelationId;

        using (correlation.Push(correlationId))
        {
            var kind = Enum.TryParse<TenantKind>(evt.Kind, true, out var parsedKind) ? parsedKind : TenantKind.Customer;
            var nowUtc = DateTime.UtcNow;

            await tenants.UpsertCreatedAsync(
                evt.NewTenantId,
                evt.Name,
                evt.SubDomain,
                kind,
                evt.DefaultTimeZoneId,
                nowUtc,
                ct
            );

            if (kind == TenantKind.Customer)
                await ProvisionStripeCustomerAsync(evt, customers, providerFactory, logger, nowUtc, ct);

            await unitOfWork.SaveChangesAsync(ct);
        }
    }

    /// <summary>Best-effort: si Stripe no responde, el tenant igual se da de alta — el
    /// primer <c>AttachPaymentMethod</c> reintenta el aprovisionamiento (fallback lazy en
    /// <c>AttachPaymentMethodHandler</c>). No bloquear el alta del tenant por esto.</summary>
    private static async Task ProvisionStripeCustomerAsync(
        TenantCreatedIntegrationEvent evt,
        ITenantProviderCustomerRepository customers,
        IPaymentAdapterFactory providerFactory,
        ILogger logger,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var existing = await customers.GetByTenantAndProviderAsync(evt.NewTenantId, PaymentProviderCode.Stripe, ct);
        if (existing is not null)
            return;

        var adapter = providerFactory.Resolve(PaymentProviderCode.Stripe);
        var tokenResult = await adapter.GetOrCreateCustomerAsync(evt.NewTenantId, evt.AdminEmail, evt.Name, ct);
        if (tokenResult.IsFailure)
        {
            logger.LogWarning(
                "Could not eager-provision Stripe customer for tenant {TenantId}: {Error}. Will retry lazily on first payment method attach.",
                evt.NewTenantId,
                tokenResult.Error.Message
            );
            return;
        }

        var referenceResult = ProviderCustomerReference.Create(PaymentProviderCode.Stripe, tokenResult.Value.Token);
        if (referenceResult.IsFailure)
            return;

        var registerResult = TenantProviderCustomer.Register(
            evt.NewTenantId,
            PaymentProviderCode.Stripe,
            referenceResult.Value,
            evt.AdminEmail,
            nowUtc
        );
        if (registerResult.IsFailure)
            return;

        await customers.AddAsync(registerResult.Value, ct);
    }
}
