// Alias: el namespace TaxVision.BuildingBlocks taparía los BuildingBlocks.* calificados del proyecto.
extern alias BbTesting;

using System.Reflection;
using BbTesting::TaxVision.BuildingBlocks.Testing.Messaging;
using TaxVision.PaymentApp.Application.SaaSPayments.DomainEventHandlers;
using TaxVision.PaymentApp.Domain.SaaSPayments;

namespace TaxVision.PaymentApp.Tests.Architecture;

// PAY-BB1: con UseTaxVisionDomainEvents() todo evento de dominio de PaymentApp va a la cola domain-events: cada
// uno necesita su handler.
public sealed class DomainEventHandlerConventionTests : DomainEventHandlerConventionRules
{
    protected override IEnumerable<Assembly> DomainAssemblies => [typeof(SaaSPayment).Assembly];

    protected override IEnumerable<Assembly> HandlerAssemblies => [typeof(SaaSPaymentDomainEventHandler).Assembly];
}
