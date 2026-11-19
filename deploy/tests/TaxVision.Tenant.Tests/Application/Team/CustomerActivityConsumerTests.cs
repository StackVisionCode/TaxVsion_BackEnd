using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using TaxVision.Tenant.Application.Team.Consumers;
using TaxVision.Tenant.Domain.Team;
using TaxVision.Tenant.Tests.TestSupport;

namespace TaxVision.Tenant.Tests.Application.Team;

public sealed class CustomerActivityConsumerTests : ActivityConsumerContract
{
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly Guid CustomerId = Guid.NewGuid();

    protected override IEnumerable<ActivityCase> Cases()
    {
        var created = Stamp(
            new CustomerCreatedIntegrationEvent
            {
                CustomerId = CustomerId,
                Kind = "Individual",
                DisplayName = "Pérez",
                PrimaryEmail = null,
                Language = "En",
                CreatedByUserId = Actor,
            }
        );
        yield return new(
            created,
            created with
            {
                CreatedByUserId = Guid.Empty,
            },
            (e, r, u, c) => CustomerActivityConsumer.Handle((CustomerCreatedIntegrationEvent)e, r, u, c, default),
            Actor,
            ActivityVerbs.CustomerCreated,
            CustomerId,
            CustomerId,
            ActivityVisibility.Team
        );

        // El actor es quien asignó, no el preparador asignado.
        var assigned = Stamp(
            new CustomerPreparerAssignedIntegrationEvent
            {
                CustomerId = CustomerId,
                PreparerUserId = Guid.NewGuid(),
                AssignedByUserId = Actor,
            }
        );
        yield return new(
            assigned,
            assigned with
            {
                AssignedByUserId = Guid.Empty,
            },
            (e, r, u, c) =>
                CustomerActivityConsumer.Handle((CustomerPreparerAssignedIntegrationEvent)e, r, u, c, default),
            Actor,
            ActivityVerbs.CustomerPreparerAssigned,
            CustomerId,
            CustomerId,
            ActivityVisibility.Team
        );
    }
}
