using BuildingBlocks.Messaging.AuthIntegrationEvents;
using TaxVision.Notes.Application.Projections.AuthEvents;

namespace TaxVision.Notes.Tests.Application;

/// <summary>
/// Registra al empleado retirado en la proyección local (habilita el manage-override). Idempotente:
/// offboard es terminal, un redelivery no duplica ni relanza. Fakes de mano.
/// </summary>
public sealed class NotesUserOffboardedConsumerTests
{
    private static UserOffboardedIntegrationEvent Event(Guid tenantId, Guid userId) =>
        new()
        {
            TenantId = tenantId,
            UserId = userId,
            Email = "leaver@example.com",
            ActorType = "TenantEmployee",
            RemovedAtUtc = DateTime.UtcNow,
        };

    [Fact]
    public async Task Registers_the_offboarded_user_when_none_exists()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var offboarded = new FakeOffboardedStaffRepository();
        var uow = new NoOpUnitOfWork();

        await NotesUserOffboardedConsumer.Handle(
            Event(tenantId, userId),
            offboarded,
            uow,
            new NoOpCorrelationContext(),
            CancellationToken.None
        );

        Assert.True(await offboarded.IsOffboardedAsync(tenantId, userId));
        Assert.Equal(1, offboarded.AddCount);
        Assert.Equal(1, uow.SaveCount);
    }

    [Fact]
    public async Task Is_idempotent_when_the_user_is_already_registered()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var offboarded = new FakeOffboardedStaffRepository();
        offboarded.MarkOffboarded(tenantId, userId);
        var uow = new NoOpUnitOfWork();

        await NotesUserOffboardedConsumer.Handle(
            Event(tenantId, userId),
            offboarded,
            uow,
            new NoOpCorrelationContext(),
            CancellationToken.None
        );

        Assert.Equal(0, offboarded.AddCount); // MarkOffboarded no incrementa AddCount; el consumer no re-agrega
        Assert.Equal(0, uow.SaveCount);
    }
}
