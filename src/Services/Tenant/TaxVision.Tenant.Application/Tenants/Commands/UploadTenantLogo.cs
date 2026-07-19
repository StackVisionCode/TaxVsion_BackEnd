using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tenant.Application.Tenants.Abstractions;

namespace TaxVision.Tenant.Application.Tenants.Commands;

public sealed record UploadTenantLogoCommand(
    Guid TenantId,
    Guid ActorId,
    byte[] Content,
    string ContentType,
    string FileName
);

public sealed record UploadTenantLogoResponse(Guid FileId, string Status);

/// <summary>
/// Upload asincrono/desacoplado (Tenant_Service_LogoSupport_Plan.md §5): sube a MinIO y setea
/// Tenant.LogoFileId de forma OPTIMISTA con los metadatos declarados por el cliente — el aggregate
/// mismo valida contentType/sizeBytes como invariante duro (defensa en profundidad, la whitelist
/// real ya la aplico CloudStorage via FolderType.Branding). Se confirma (o se descarta) cuando
/// llega FileAvailable/Infected/BlockedByPolicy — ver TenantBrandingFileScanResultConsumer.
/// </summary>
public static class UploadTenantLogoHandler
{
    public static async Task<Result<UploadTenantLogoResponse>> Handle(
        UploadTenantLogoCommand cmd,
        Abstractions.ITenantRepository repo,
        ITenantBrandingCloudStorageClient client,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var tenant = await repo.GetByIdAsync(cmd.TenantId, ct);
        if (tenant is null)
            return Result.Failure<UploadTenantLogoResponse>(new Error("Tenant.NotFound", "Tenant not found."));

        var uploadResult = await client.UploadAsync(
            cmd.TenantId,
            new TenantLogoUpload(cmd.Content, cmd.ContentType, cmd.FileName, cmd.ActorId),
            ct
        );
        if (uploadResult.IsFailure)
            return Result.Failure<UploadTenantLogoResponse>(uploadResult.Error);

        var fileId = uploadResult.Value;
        var setResult = tenant.SetLogoPending(fileId, cmd.ContentType, cmd.Content.LongLength, null, null);
        if (setResult.IsFailure)
            return Result.Failure<UploadTenantLogoResponse>(setResult.Error);

        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(new UploadTenantLogoResponse(fileId, "processing"));
    }
}
