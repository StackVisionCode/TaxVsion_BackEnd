using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.Auth.Application.Abstractions;

namespace TaxVision.Auth.Application.SlotGating.Consumers;

public static class UserSlotUnlockedConsumer
{
    public static async Task Handle(
        UserSlotUnlockedIntegrationEvent evt,
        IUserRepository users,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var user = await users.GetByIdAsync(evt.UserId, ct);
        if (user is null || user.TenantId != evt.TenantId)
            return;

        user.BumpPermissionsVersion();
        await unitOfWork.SaveChangesAsync(ct);
    }
}
