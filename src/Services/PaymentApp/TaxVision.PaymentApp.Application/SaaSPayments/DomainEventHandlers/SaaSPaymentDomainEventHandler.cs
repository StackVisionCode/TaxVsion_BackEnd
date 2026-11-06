using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.SaaSPayments.Common;
using TaxVision.PaymentApp.Domain.SaaSPayments.Events;
using Wolverine;

namespace TaxVision.PaymentApp.Application.SaaSPayments.DomainEventHandlers;

/// <summary>
/// Resultado de un SaaSPayment: devuelve los avisos de integración (cascada por el outbox). Corre después del
/// commit, desde la cola domain-events. Lo que llevan los avisos sale de la foto del evento; solo se leen el
/// perfil de la oficina (recibo) y la tarjeta guardada (correo de cobro fallido), con el tenant explícito.
/// </summary>
public static class SaaSPaymentDomainEventHandler
{
    public static Task<OutgoingMessages> Handle(
        SaaSPaymentSucceeded succeeded,
        ITenantRegistry tenants,
        ITenantProviderCustomerRepository customers,
        Envelope envelope,
        CancellationToken ct
    ) => ResultAsync(succeeded.Payment, tenants, customers, envelope, ct);

    public static Task<OutgoingMessages> Handle(
        SaaSPaymentFailed failed,
        ITenantRegistry tenants,
        ITenantProviderCustomerRepository customers,
        Envelope envelope,
        CancellationToken ct
    ) => ResultAsync(failed.Payment, tenants, customers, envelope, ct);

    // La cancelación que pidió el dueño del cobro no se le anuncia: ya la conoce.
    public static Task<OutgoingMessages> Handle(
        SaaSPaymentCancelled cancelled,
        ITenantRegistry tenants,
        ITenantProviderCustomerRepository customers,
        Envelope envelope,
        CancellationToken ct
    ) =>
        cancelled.PropagatedFromOwner
            ? Task.FromResult(new OutgoingMessages())
            : ResultAsync(cancelled.Payment, tenants, customers, envelope, ct);

    // Sin consumidor a propósito: la revisión se ve en el panel del admin (métrica y log en el comando).
    public static void Handle(SaaSPaymentReviewRequired reviewRequired) { }

    private static async Task<OutgoingMessages> ResultAsync(
        SaaSPaymentSnapshot payment,
        ITenantRegistry tenants,
        ITenantProviderCustomerRepository customers,
        Envelope envelope,
        CancellationToken ct
    )
    {
        var messages = new OutgoingMessages();
        messages.AddRange(
            await SaaSPaymentResultMessages.BuildAsync(
                payment,
                envelope.CorrelationId ?? string.Empty,
                ct,
                tenants,
                customers
            )
        );
        return messages;
    }
}
