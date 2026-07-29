using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Auth.Application.Onboarding.Abstractions;

namespace TaxVision.Auth.Application.Onboarding.ReceiptDownload.Queries;

/// <summary>PayFlow (Fase 11) — respalda el endpoint público mediador de descarga del recibo.
/// Deliberadamente NO valida que <see cref="FileId"/> pertenezca a un onboarding conocido: el FileId
/// es un GUID no adivinable generado por Documents (nunca expuesto salvo a través de este mismo
/// flujo), así que sirve de capability opaca — mismo modelo de exposición que los ShareLink públicos
/// de CloudStorage. Si CloudStorage no encuentra el file (borrado, o un GUID inventado), la llamada
/// M2M simplemente falla y el endpoint devuelve 404.</summary>
public sealed record GetOnboardingReceiptDownloadRedirectQuery(Guid FileId);

public static class GetOnboardingReceiptDownloadRedirectHandler
{
    public static async Task<Result<Uri>> Handle(
        GetOnboardingReceiptDownloadRedirectQuery query,
        ICloudStorageDownloadUrlClient cloudStorage,
        ILogger<GetOnboardingReceiptDownloadRedirectQuery> logger,
        CancellationToken ct
    )
    {
        var result = await cloudStorage.GetDownloadUrlAsync(query.FileId, ct);
        if (result.IsFailure)
            logger.LogWarning(
                "Onboarding receipt download redirect failed for file {FileId}: {ErrorCode}",
                query.FileId,
                result.Error.Code
            );

        return result;
    }
}
