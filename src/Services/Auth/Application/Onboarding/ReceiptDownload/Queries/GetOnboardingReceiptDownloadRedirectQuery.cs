using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Auth.Application.Onboarding.Abstractions;

namespace TaxVision.Auth.Application.Onboarding.ReceiptDownload.Queries;

/// <summary>PayFlow (Fase 11) — respalda el endpoint público mediador de descarga del recibo. El
/// <see cref="FileId"/> es un GUID no adivinable que sirve de capability opaca (mismo modelo que los
/// ShareLink públicos), pero además se valida que sea un recibo realmente emitido: así el endpoint
/// anónimo no es un resolver genérico de cualquier archivo por GUID. Un FileId desconocido devuelve
/// 404 (anti-enumeración) sin llegar a CloudStorage.</summary>
public sealed record GetOnboardingReceiptDownloadRedirectQuery(Guid FileId);

public static class GetOnboardingReceiptDownloadRedirectHandler
{
    public static async Task<Result<Uri>> Handle(
        GetOnboardingReceiptDownloadRedirectQuery query,
        ITenantOnboardingRepository onboardings,
        ICloudStorageDownloadUrlClient cloudStorage,
        ILogger<GetOnboardingReceiptDownloadRedirectQuery> logger,
        CancellationToken ct
    )
    {
        var onboarding = await onboardings.GetByReceiptFileIdAsync(query.FileId, ct);
        if (onboarding is null)
        {
            logger.LogWarning("Onboarding receipt download rejected: unknown receipt file {FileId}", query.FileId);
            return Result.Failure<Uri>(
                new Error("Onboarding.ReceiptNotFound", "No onboarding receipt matches the requested file.")
            );
        }

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
