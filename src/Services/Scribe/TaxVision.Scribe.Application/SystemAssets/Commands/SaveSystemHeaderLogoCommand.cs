using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Scribe.Application.Abstractions;
using TaxVision.Scribe.Domain.Projections;

namespace TaxVision.Scribe.Application.SystemAssets.Commands;

/// <summary>SCR-08: el archivo ya es descargable; esta transacción corta solo apunta la fila a él.</summary>
public sealed record SaveSystemHeaderLogoCommand(Guid FileId, long SizeBytes);

public static class SaveSystemHeaderLogoHandler
{
    public static async Task<Result> Handle(
        SaveSystemHeaderLogoCommand command,
        ISystemAssetRefRepository systemAssetRefRepository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var existing = await systemAssetRefRepository.GetByKeyAsync(SystemAssetKeys.HeaderLogo, ct);
        if (existing is not null)
            existing.Update(command.FileId, "image/png", command.SizeBytes, DateTime.UtcNow);
        else
            await systemAssetRefRepository.AddAsync(
                SystemAssetRef.Create(
                    SystemAssetKeys.HeaderLogo,
                    command.FileId,
                    "image/png",
                    command.SizeBytes,
                    DateTime.UtcNow
                ),
                ct
            );

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
