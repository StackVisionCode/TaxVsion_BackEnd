using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Onboarding.Abstractions;
using TaxVision.Auth.Domain.Onboarding.TermsVersions;

namespace TaxVision.Auth.Application.Onboarding.TermsVersions.Commands;

public sealed record PublishTermsVersionCommand(
    TermsKind Kind,
    string Version,
    string ContentUri,
    string Locale,
    Guid CreatedByUserId,
    DateTime? EffectiveUntilUtc = null
);

public sealed record TermsVersionResponse(
    Guid TermsVersionId,
    TermsKind Kind,
    string Version,
    string? ContentUri,
    string? ContentHash,
    string Locale,
    DateTime EffectiveFromUtc,
    DateTime? EffectiveUntilUtc
);

/// <summary>
/// PlatformAdmin publica una version nueva de un documento legal. No se registra en
/// AuthAuditLog: esa tabla es tenant-scoped (AuthAuditLog : TenantEntity, requiere
/// TenantId), y publicar una TermsVersion es una accion de plataforma sin tenant —
/// forzar un Guid.Empty como sentinel no tiene precedente en el resto del codigo y
/// seria una desviacion mas confusa que util para este alcance.
///
/// El ContentHash nunca lo provee el llamador: el handler descarga el documento desde
/// ContentUri y lo calcula el mismo (ITermsDocumentHasher), para que el hash sea una
/// garantia verificable del contenido real publicado, no un dato de entrada que alguien
/// pudo copiar mal o directamente inventar.
/// </summary>
public static class PublishTermsVersionHandler
{
    public static async Task<Result<TermsVersionResponse>> Handle(
        PublishTermsVersionCommand command,
        ITermsVersionRepository repository,
        ITermsDocumentHasher documentHasher,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var hashResult = await documentHasher.ComputeHashAsync(command.ContentUri, ct);
        if (hashResult.IsFailure)
            return Result.Failure<TermsVersionResponse>(hashResult.Error);

        var nowUtc = DateTime.UtcNow;
        var result = TermsVersion.Publish(
            command.Kind,
            command.Version,
            command.ContentUri,
            hashResult.Value,
            command.Locale,
            command.CreatedByUserId,
            nowUtc,
            command.EffectiveUntilUtc
        );
        if (result.IsFailure)
            return Result.Failure<TermsVersionResponse>(result.Error);

        var version = result.Value;
        await repository.AddAsync(version, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(
            new TermsVersionResponse(
                version.Id,
                version.Kind,
                version.Version,
                version.ContentUri,
                version.ContentHash,
                version.Locale,
                version.EffectiveFromUtc,
                version.EffectiveUntilUtc
            )
        );
    }
}
