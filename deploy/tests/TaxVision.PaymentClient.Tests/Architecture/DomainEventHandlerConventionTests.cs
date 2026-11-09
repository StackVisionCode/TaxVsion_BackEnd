extern alias BbTesting;

using System.Reflection;
using BbTesting::TaxVision.BuildingBlocks.Testing.Messaging;
using TaxVision.PaymentClient.Application.TenantPayments.Common;
using TaxVision.PaymentClient.Domain.TenantPayments;

namespace TaxVision.PaymentClient.Tests.Architecture;

// Con UseTaxVisionDomainEvents() todo evento de dominio de PaymentClient va a la cola domain-events: cada uno
// necesita su handler.
public sealed class DomainEventHandlerConventionTests : DomainEventHandlerConventionRules
{
    protected override IEnumerable<Assembly> DomainAssemblies => [typeof(TenantPayment).Assembly];

    protected override IEnumerable<Assembly> HandlerAssemblies => [typeof(TenantPaymentSucceededSignal).Assembly];
}
