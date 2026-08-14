using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Calendar.Application.Feeds.Abstractions;
using TaxVision.Calendar.Domain.Feeds;

namespace TaxVision.Calendar.Application.Feeds.Commands;

public sealed record RevokeFeedTokenCommand(Guid TenantId, Guid UserId);

public static class RevokeFeedTokenHandler
{
    public static async Task<Result> Handle(
        RevokeFeedTokenCommand command,
        ICalendarFeedTokenRepository tokens,
        ICalendarFeedCache cache,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var token = await tokens.FindActiveForUserAsync(command.TenantId, command.UserId, ct);
        if (token is null)
            return Result.Failure(FeedErrors.NotFound);

        var revoked = token.Revoke(DateTime.UtcNow);
        if (revoked.IsFailure)
            return revoked;

        await unitOfWork.SaveChangesAsync(ct);

        // Sin esto, revocar no serviría de nada durante una caída: el camino degradado seguiría
        // entregando la agenda desde la copia.
        await cache.RemoveAsync(Convert.ToHexString(token.TokenHash), ct);

        return Result.Success();
    }
}
