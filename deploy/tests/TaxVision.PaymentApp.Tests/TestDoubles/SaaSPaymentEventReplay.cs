using BuildingBlocks.Domain;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.SaaSPayments.DomainEventHandlers;
using TaxVision.PaymentApp.Domain.ProviderCustomers;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.SaaSPayments.Events;
using TaxVision.PaymentApp.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.PaymentApp.Tests.TestDoubles;

/// <summary>
/// Lo que Wolverine hace con UseTaxVisionDomainEvents(), en memoria: saca los eventos de dominio pendientes del
/// pago, corre <see cref="SaaSPaymentDomainEventHandler"/> y junta lo que devuelve. Así una prueba de comando ve
/// todo lo emitido (publicado a mano + devuelto por el handler de dominio).
/// </summary>
internal static class SaaSPaymentEventReplay
{
    public const string CorrelationId = "corr";

    /// <summary>Publicado a mano por el comando más lo devuelto por el handler de dominio.</summary>
    public static async Task<IReadOnlyList<object>> EmittedAsync(
        IEnumerable<object> published,
        SaaSPayment payment,
        ITenantRegistry? tenants = null,
        ITenantProviderCustomerRepository? customers = null
    ) => [.. published, .. await DispatchAsync(payment, tenants, customers)];

    public static async Task<IReadOnlyList<object>> DispatchAsync(
        SaaSPayment payment,
        ITenantRegistry? tenants = null,
        ITenantProviderCustomerRepository? customers = null
    )
    {
        var events = payment.DomainEvents.ToList();
        payment.ClearDomainEvents();
        return await DispatchEventsAsync(events, tenants, customers);
    }

    public static async Task<IReadOnlyList<object>> DispatchEventsAsync(
        IEnumerable<IDomainEvent> events,
        ITenantRegistry? tenants = null,
        ITenantProviderCustomerRepository? customers = null
    )
    {
        tenants ??= new FakeTenantRegistry("Acme Tax");
        customers ??= new NoProviderCustomers();
        var envelope = new Envelope { CorrelationId = CorrelationId };
        var ct = CancellationToken.None;

        var emitted = new List<object>();
        foreach (var domainEvent in events)
        {
            switch (domainEvent)
            {
                case SaaSPaymentSucceeded e:
                    emitted.AddRange(await SaaSPaymentDomainEventHandler.Handle(e, tenants, customers, envelope, ct));
                    break;
                case SaaSPaymentFailed e:
                    emitted.AddRange(await SaaSPaymentDomainEventHandler.Handle(e, tenants, customers, envelope, ct));
                    break;
                case SaaSPaymentCancelled e:
                    emitted.AddRange(await SaaSPaymentDomainEventHandler.Handle(e, tenants, customers, envelope, ct));
                    break;
                case SaaSPaymentReviewRequired e:
                    SaaSPaymentDomainEventHandler.Handle(e);
                    break;
                default:
                    throw new InvalidOperationException($"Sin handler de dominio para {domainEvent.GetType().Name}.");
            }
        }
        return emitted;
    }

    /// <summary>Sin tarjeta guardada: el aviso de fallo sale sin los últimos 4.</summary>
    public sealed class NoProviderCustomers : ITenantProviderCustomerRepository
    {
        public Task<TenantProviderCustomer?> GetByTenantAndProviderAsync(
            Guid tenantId,
            PaymentProviderCode code,
            CancellationToken ct = default
        ) => Task.FromResult<TenantProviderCustomer?>(null);

        public Task<TenantProviderCustomer?> GetByIdAsync(
            Guid tenantProviderCustomerId,
            Guid tenantId,
            CancellationToken ct = default
        ) => Task.FromResult<TenantProviderCustomer?>(null);

        public Task<IReadOnlyList<TenantProviderCustomer>> GetWithMethodsExpiringBeforeAsync(
            DateTime cutoffUtc,
            int batchSize,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<TenantProviderCustomer>>([]);

        public Task AddAsync(TenantProviderCustomer customer, CancellationToken ct = default) => Task.CompletedTask;
    }
}
