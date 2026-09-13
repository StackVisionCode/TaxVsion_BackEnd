using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using TaxVision.Scribe.Application.Abstractions;
using TaxVision.Scribe.Application.Templates.Storage;
using TaxVision.Scribe.Domain.Projections;

namespace TaxVision.Scribe.Application.SystemAssets.Commands;

public sealed record UpdateSystemHeaderLogoCommand(byte[] Content, string ContentType, Guid ActorUserId);

public sealed record UpdateSystemHeaderLogoResult(Guid FileId, long SizeBytes);

/// <summary>
/// Break-glass de PlatformAdmin: reemplaza en caliente el logo de header de la plataforma que se
/// embebe en TODOS los correos del sistema (<see cref="SystemAssetKeys.HeaderLogo"/>). Hace lo mismo
/// que <c>ScribeSystemAssetSeeder</c> pero a demanda: sube el PNG a CloudStorage, espera a que quede
/// descargable, y recién ahí apunta el <see cref="SystemAssetRef"/> al nuevo FileId — así nunca deja
/// el ref apuntando a un archivo aún no catalogado (los correos seguirían con el logo viejo hasta que
/// esto confirme). Invalida el L1 de LogoResolver en esta instancia; otras réplicas se auto-sanan por
/// el TTL de 5 min (mismo criterio que el consumer de TenantLogoUpdated).
///
/// <para><b>PNG obligatorio a propósito:</b> el logo se inyecta inline por Content-ID en el correo y
/// los clientes de email NO renderizan SVG — además el storage fuerza el content-type a image/png
/// para <see cref="TemplateArtifactKind.SystemLogo"/>. Un SVG quedaría mal-etiquetado y roto, así que
/// se rechaza en la puerta.</para>
/// </summary>
public static class UpdateSystemHeaderLogoHandler
{
    private const long MaxSizeBytes = 1_048_576; // 1 MB — va inline en cada correo; un logo no debe pesar más.
    private const int DownloadableWaitAttempts = 60;
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    // Clave del L1 que arma LogoResolver para el scope System ($"logo:{tenantId? ?? "system"}").
    private const string SystemLogoCacheKey = "logo:system";

    public static async Task<Result<UpdateSystemHeaderLogoResult>> Handle(
        UpdateSystemHeaderLogoCommand command,
        ISystemAssetRefRepository systemAssetRefRepository,
        ITemplateStorageService storageService,
        IMemoryCache l1Cache,
        IUnitOfWork unitOfWork,
        ILogger<UpdateSystemHeaderLogoCommand> logger,
        CancellationToken ct
    )
    {
        var validationError = Validate(command);
        if (validationError is not null)
            return Result.Failure<UpdateSystemHeaderLogoResult>(validationError);

        // OwnerType=Tenant con PlatformTenant.Id para contenido System — mismo actorId que el seeder,
        // para no chocar con la validación de propietario de CloudStorage (el actor real ya quedó
        // acotado por el gate PlatformAdmin del controller; se registra abajo para auditoría).
        var uploadResult = await storageService.UploadAsync(
            tenantId: null,
            TemplateArtifactKind.SystemLogo,
            command.Content,
            PlatformTenant.Id,
            ct
        );
        if (uploadResult.IsFailure)
            return Result.Failure<UpdateSystemHeaderLogoResult>(uploadResult.Error);

        if (!await WaitUntilDownloadableAsync(storageService, uploadResult.Value.FileId, ct))
            return Result.Failure<UpdateSystemHeaderLogoResult>(
                new Error(
                    "SystemAsset.NotCatalogued",
                    "CloudStorage did not catalogue the uploaded logo in time; the previous logo was left untouched."
                )
            );

        var existing = await systemAssetRefRepository.GetByKeyAsync(SystemAssetKeys.HeaderLogo, ct);
        var sizeBytes = command.Content.LongLength;
        if (existing is not null)
            existing.Update(uploadResult.Value.FileId, "image/png", sizeBytes, DateTime.UtcNow);
        else
            await systemAssetRefRepository.AddAsync(
                SystemAssetRef.Create(
                    SystemAssetKeys.HeaderLogo,
                    uploadResult.Value.FileId,
                    "image/png",
                    sizeBytes,
                    DateTime.UtcNow
                ),
                ct
            );

        await unitOfWork.SaveChangesAsync(ct);
        l1Cache.Remove(SystemLogoCacheKey);

        logger.LogInformation(
            "System header logo replaced by {ActorUserId}: FileId {FileId}, {Bytes} bytes.",
            command.ActorUserId,
            uploadResult.Value.FileId,
            sizeBytes
        );

        return Result.Success(new UpdateSystemHeaderLogoResult(uploadResult.Value.FileId, sizeBytes));
    }

    private static Error? Validate(UpdateSystemHeaderLogoCommand command)
    {
        if (command.Content is null || command.Content.Length == 0)
            return new Error("SystemAsset.Empty", "The uploaded file is empty.");

        if (command.Content.LongLength > MaxSizeBytes)
            return new Error("SystemAsset.TooLarge", "The logo must be 1 MB or smaller.");

        if (!string.Equals(command.ContentType, "image/png", StringComparison.OrdinalIgnoreCase))
            return new Error(
                "SystemAsset.ContentType",
                "The header logo must be a PNG. Email clients do not render SVG, so PNG (transparent background) is required."
            );

        if (!HasPngSignature(command.Content))
            return new Error("SystemAsset.NotPng", "The uploaded bytes are not a valid PNG (signature mismatch).");

        return null;
    }

    private static bool HasPngSignature(byte[] content)
    {
        if (content.Length < PngSignature.Length)
            return false;
        for (var i = 0; i < PngSignature.Length; i++)
        {
            if (content[i] != PngSignature[i])
                return false;
        }
        return true;
    }

    private static async Task<bool> WaitUntilDownloadableAsync(
        ITemplateStorageService storageService,
        Guid fileId,
        CancellationToken ct
    )
    {
        for (var attempt = 1; attempt <= DownloadableWaitAttempts; attempt++)
        {
            var download = await storageService.DownloadTextAsync(fileId, null, ct);
            if (download.IsSuccess)
                return true;

            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }

        return false;
    }
}
