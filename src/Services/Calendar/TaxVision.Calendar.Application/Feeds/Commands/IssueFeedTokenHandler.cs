using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Calendar.Application.Feeds.Abstractions;
using TaxVision.Calendar.Domain.Feeds;

namespace TaxVision.Calendar.Application.Feeds.Commands;

public sealed record IssueFeedTokenCommand(Guid TenantId, Guid UserId);

/// <summary>El valor crudo vuelve una sola vez. Después sólo quedan los últimos cuatro caracteres.</summary>
public sealed record IssuedFeedToken(string Url, string Last4, DateTime CreatedAtUtc);

public static class IssueFeedTokenHandler
{
    public static async Task<Result<IssuedFeedToken>> Handle(
        IssueFeedTokenCommand command,
        ICalendarFeedTokenRepository tokens,
        ICalendarFeedCache cache,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var nowUtc = DateTime.UtcNow;

        // Emitir uno nuevo revoca el anterior: es lo que hace útil el botón de regenerar cuando la URL
        // vieja se compartió de más.
        var existing = await tokens.FindActiveForUserAsync(command.TenantId, command.UserId, ct);
        if (existing is not null)
        {
            existing.Revoke(nowUtc);
            await cache.RemoveAsync(Convert.ToHexString(existing.TokenHash), ct);
        }

        var (token, plainValue) = CalendarFeedToken.Issue(command.TenantId, command.UserId, nowUtc);
        tokens.Add(token);

        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(
            new IssuedFeedToken($"calendar/feed/{command.UserId:D}/{plainValue}.ics", token.TokenLast4, nowUtc)
        );
    }
}
