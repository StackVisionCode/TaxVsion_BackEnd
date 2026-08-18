using BuildingBlocks.Results;
using TaxVision.Auth.Application.Onboarding.Abstractions;

namespace TaxVision.Auth.Application.Onboarding.TermsVersions.Queries;

/// <summary>
/// Auditoría (gap MinIO/legal-docs) — respalda el endpoint público mediador
/// <c>GET /auth/onboarding/terms/{id}/content</c>: resuelve el <c>ContentFileId</c> de la
/// TermsVersion y devuelve el HTML real (no redirige, a diferencia de
/// GetOnboardingReceiptDownloadRedirectQuery — el frontend de onboarding necesita el texto
/// para renderizarlo inline, no un link de descarga). El Id de TermsVersion ya funciona como
/// capability opaca (mismo razonamiento que el FileId del recibo): sin autenticación adicional.
/// </summary>
public sealed record GetTermsContentQuery(Guid TermsVersionId);

public static class GetTermsContentHandler
{
    public static async Task<Result<string>> Handle(
        GetTermsContentQuery query,
        ITermsVersionRepository repository,
        ITermsContentStorageClient storageClient,
        CancellationToken ct
    )
    {
        var version = await repository.GetByIdAsync(query.TermsVersionId, ct);
        if (version is not { ContentFileId: { } fileId })
            return Result.Failure<string>(
                new Error("TermsVersion.NotFound", "No terms version with content was found for the given id.")
            );

        return await storageClient.DownloadTextAsync(fileId, ct);
    }
}
