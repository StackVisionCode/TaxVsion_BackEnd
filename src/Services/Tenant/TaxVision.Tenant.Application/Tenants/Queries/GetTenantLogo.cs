using BuildingBlocks.Results;
using TaxVision.Tenant.Application.Tenants.Abstractions;

namespace TaxVision.Tenant.Application.Tenants.Queries;

public sealed record GetTenantLogoQuery(Guid TenantId);

public sealed record TenantLogoResponse(
    Guid FileId,
    string ContentType,
    long SizeBytes,
    int? Width,
    int? Height,
    DateTime UpdatedAtUtc,
    Uri DownloadUrl,
    DateTime DownloadUrlExpiresAtUtc
);

public static class GetTenantLogoHandler
{
    public static async Task<Result<TenantLogoResponse>> Handle(
        GetTenantLogoQuery query,
        ITenantRepository repo,
        ITenantBrandingCloudStorageClient client,
        CancellationToken ct
    )
    {
        var tenant = await repo.GetByIdAsync(query.TenantId, ct);
        if (tenant is null)
            return Result.Failure<TenantLogoResponse>(new Error("Tenant.NotFound", "Tenant not found."));

        // LogoUpdatedAtUtc no nulo == confirmado por FileAvailable — antes de eso el archivo todavia
        // puede estar en escaneo y CloudStorage rechazaria el download-url igual (ver
        // TenantBrandingFileScanResultConsumer). Tratarlo como "sin logo" es mas simple y correcto
        // que inventar un tercer estado "pending" en la respuesta publica.
        if (tenant.LogoFileId is not { } fileId || tenant.LogoUpdatedAtUtc is not { } updatedAtUtc)
            return Result.Failure<TenantLogoResponse>(new Error("Tenant.Logo.NotFound", "Tenant has no logo."));

        var urlResult = await client.GetDownloadUrlAsync(query.TenantId, fileId, ct);
        if (urlResult.IsFailure)
            return Result.Failure<TenantLogoResponse>(urlResult.Error);

        return Result.Success(
            new TenantLogoResponse(
                fileId,
                tenant.LogoContentType!,
                tenant.LogoSizeBytes!.Value,
                tenant.LogoWidth,
                tenant.LogoHeight,
                updatedAtUtc,
                urlResult.Value.Url,
                urlResult.Value.ExpiresAtUtc
            )
        );
    }
}
