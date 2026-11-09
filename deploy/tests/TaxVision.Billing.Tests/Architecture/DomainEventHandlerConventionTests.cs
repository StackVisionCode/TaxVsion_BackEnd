extern alias BbTesting;

using System.Reflection;
using BbTesting::TaxVision.BuildingBlocks.Testing.Messaging;
using TaxVision.Billing.Application.Invoices.IssueInvoice;

namespace TaxVision.Billing.Tests.Architecture;

// Con UseTaxVisionDomainEvents() todo evento de dominio de Billing va a la cola domain-events.
public sealed class DomainEventHandlerConventionTests : DomainEventHandlerConventionRules
{
    protected override IEnumerable<Assembly> DomainAssemblies =>
        [typeof(TaxVision.Billing.Domain.Invoices.Invoice).Assembly];

    protected override IEnumerable<Assembly> HandlerAssemblies => [typeof(IssueInvoiceHandler).Assembly];
}
