using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tenant.Application.Platform.Abstractions;
using TaxVision.Tenant.Domain.Platform;
using Wolverine;

namespace TaxVision.Tenant.Application.Platform.Commands;

/// <summary>Valores con los que se siembra la fila la primera vez (los de la config de arranque).</summary>
public sealed record InitializePlatformSettingsCommand(
    string BrandName,
    string LegalName,
    string BaseDomain,
    string ApiHost,
    string SupportEmail,
    string BillingEmail
);

/// <summary>
/// Crea la fila si todavía no existe. Si ya existe no la toca: desde ese momento la fuente es la fila
/// (lo que cambie el PlatformAdmin), no la config, así que un redeploy no pisa un cambio hecho por API.
/// </summary>
public static class InitializePlatformSettingsHandler
{
    public static async Task<Result<bool>> Handle(
        InitializePlatformSettingsCommand cmd,
        IPlatformSettingsRepository repository,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        if (await repository.GetAsync(ct) is not null)
            return Result.Success(false);

        var created = PlatformSettings.Create(
            cmd.BrandName,
            cmd.LegalName,
            cmd.BaseDomain,
            cmd.ApiHost,
            cmd.SupportEmail,
            cmd.BillingEmail,
            DateTime.UtcNow
        );
        if (created.IsFailure)
            return Result.Failure<bool>(created.Error);

        await repository.AddAsync(created.Value, ct);
        await unitOfWork.SaveChangesAsync(ct);
        await bus.PublishAsync(created.Value.ToChangedEvent(correlation.CorrelationId));
        return Result.Success(true);
    }
}
