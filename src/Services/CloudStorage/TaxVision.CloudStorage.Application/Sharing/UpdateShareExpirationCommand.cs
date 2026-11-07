using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Domain.Sharing;

namespace TaxVision.CloudStorage.Application.Sharing;

/// <summary>Fase C3 — extiende o acorta la expiracion de un link activo.</summary>
public sealed record UpdateShareExpirationCommand(Guid TenantId, Guid ShareLinkId, DateTime NewExpiresAtUtc);

public static class UpdateShareExpirationHandler
{
    public static async Task<Result<ShareLinkResponse>> Handle(
        UpdateShareExpirationCommand command,
        IShareLinkRepository shares,
        IStorageLimitRepository limits,
        ISystemClock clock,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var link = await shares.GetAsync(command.TenantId, command.ShareLinkId, ct);
        if (link is null)
            return Result.Failure<ShareLinkResponse>(ShareErrors.NotFound);

        // Extender un enlace externo no puede saltarse la vida máxima de la oficina.
        if (link.Visibility.IsExternal() && await limits.GetAsync(command.TenantId, ct) is { } limit)
        {
            var allowed = limit.CheckExternalExpiration(command.NewExpiresAtUtc, clock.UtcNow);
            if (allowed.IsFailure)
                return Result.Failure<ShareLinkResponse>(allowed.Error);
        }

        var updated = link.UpdateExpiration(command.NewExpiresAtUtc, clock.UtcNow);
        if (updated.IsFailure)
            return Result.Failure<ShareLinkResponse>(updated.Error);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(ShareLinkResponseMapper.Map(link, clock.UtcNow));
    }
}
