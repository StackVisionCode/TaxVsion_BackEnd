extern alias BbTesting;

using System.Reflection;
using BbTesting::TaxVision.BuildingBlocks.Testing.Messaging;
using TaxVision.Codes.Domain.Reservations;
using TaxVision.Growth.Api.IntegrationEvents;
using TaxVision.Referrals.Domain.Programs;

namespace TaxVision.Growth.Tests.Architecture;

// Con UseTaxVisionDomainEvents() todo evento de dominio de Growth va a la cola domain-events.
public sealed class DomainEventHandlerConventionTests : DomainEventHandlerConventionRules
{
    protected override IEnumerable<Assembly> DomainAssemblies =>
        [typeof(CodeReservation).Assembly, typeof(ReferralProgram).Assembly];

    protected override IEnumerable<Assembly> HandlerAssemblies => [typeof(PaymentSucceededConsumer).Assembly];
}
