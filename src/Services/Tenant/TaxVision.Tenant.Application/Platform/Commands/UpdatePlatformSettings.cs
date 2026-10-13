using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tenant.Application.Platform.Abstractions;
using Wolverine;

namespace TaxVision.Tenant.Application.Platform.Commands;

public sealed record UpdatePlatformSettingsCommand(
    Guid ActorId,
    string BrandName,
    string LegalName,
    string BaseDomain,
    string ApiHost,
    string SupportEmail,
    string BillingEmail
);

/// <summary>
/// El PlatformAdmin cambia la marca o el dominio de la plataforma. Si algo cambió, sube la versión y
/// publica la foto completa para que cada servicio la aplique. Un PUT idéntico no publica nada.
/// </summary>
public static class UpdatePlatformSettingsHandler
{
    public static async Task<Result<PlatformSettingsResponse>> Handle(
        UpdatePlatformSettingsCommand cmd,
        IPlatformSettingsRepository repository,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var settings = await repository.GetAsync(ct);
        if (settings is null)
            return Result.Failure<PlatformSettingsResponse>(PlatformSettingsMapping.NotFound);

        var changed = settings.Update(
            cmd.BrandName,
            cmd.LegalName,
            cmd.BaseDomain,
            cmd.ApiHost,
            cmd.SupportEmail,
            cmd.BillingEmail,
            cmd.ActorId,
            DateTime.UtcNow
        );
        if (changed.IsFailure)
            return Result.Failure<PlatformSettingsResponse>(changed.Error);

        if (changed.Value)
        {
            await unitOfWork.SaveChangesAsync(ct);
            await bus.PublishAsync(settings.ToChangedEvent(correlation.CorrelationId));
        }

        return Result.Success(settings.ToResponse());
    }
}
