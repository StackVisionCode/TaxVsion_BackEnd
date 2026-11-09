using System.Security.Cryptography;
using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Terms;
using Wolverine;

namespace TaxVision.Auth.Application.Terms.Versions.Commands;

public sealed record PublishTermsVersionCommand(
    TermsKind Kind,
    string Version,
    byte[] Content,
    string FileName,
    string ContentType,
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
/// Auditoría (gap MinIO/legal-docs) — el documento ya no se referencia por URL externa: el
/// PlatformAdmin sube el HTML directo (bytes en el request), este handler calcula el hash
/// localmente (nunca confía en un hash de terceros — mismo principio que antes con
/// ITermsDocumentHasher, sólo que ahora no hace falta un round-trip HTTP para conseguir los
/// bytes) y los sube a CloudStorage vía ITermsContentStorageClient (patrón D0/D1). Valida
/// tamaño/tipo ANTES de subir: CloudStorage (SaveFileFromSourceHandler) rechaza cualquier archivo
/// que viole la política del FolderType — sin este chequeo temprano, un archivo inválido crearía
/// una TermsVersion cuyo contenido nunca llega a existir. Si igual lo rechaza (cuota, registro),
/// TermsContentSaveRejectedConsumer retira la versión.
/// <para>
/// Orden: armar la versión (valida antes de subir nada), PUT a MinIO (si falla no se guarda nada),
/// guardar y recién después pedir el catálogo a CloudStorage por el bus del handler. Guardar antes de
/// publicar: un rechazo de CloudStorage siempre encuentra la fila.
/// </para>
/// </summary>
public static class PublishTermsVersionHandler
{
    private const long MaxContentSizeBytes = 5L * 1024 * 1024;
    private const string RequiredContentType = "text/html";

    public static async Task<Result<TermsVersionResponse>> Handle(
        PublishTermsVersionCommand command,
        ITermsVersionRepository repository,
        ITermsContentStorageClient storageClient,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        if (command.Content.LongLength == 0 || command.Content.LongLength > MaxContentSizeBytes)
            return Result.Failure<TermsVersionResponse>(
                new Error(
                    "Onboarding.TermsContentSizeInvalid",
                    $"Content must be between 1 byte and {MaxContentSizeBytes} bytes."
                )
            );

        if (
            !string.Equals(command.ContentType, RequiredContentType, StringComparison.OrdinalIgnoreCase)
            || !command.FileName.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
        )
            return Result.Failure<TermsVersionResponse>(
                new Error(
                    "Onboarding.TermsContentTypeInvalid",
                    "The document must be an .html file with content-type text/html."
                )
            );

        var contentHash = Convert.ToHexString(SHA256.HashData(command.Content)).ToLowerInvariant();
        var fileId = Guid.NewGuid();

        var nowUtc = DateTime.UtcNow;
        var result = TermsVersion.Publish(
            command.Kind,
            command.Version,
            fileId,
            contentHash,
            command.Locale,
            command.CreatedByUserId,
            nowUtc,
            command.EffectiveUntilUtc
        );
        if (result.IsFailure)
            return Result.Failure<TermsVersionResponse>(result.Error);

        var version = result.Value;
        var contentUriResult = version.SetContentUri($"/auth/onboarding/terms/{version.Id}/content");
        if (contentUriResult.IsFailure)
            return Result.Failure<TermsVersionResponse>(contentUriResult.Error);

        var stored = await storageClient.UploadAsync(
            fileId,
            command.Content,
            command.FileName,
            command.ContentType,
            ct
        );
        if (stored.IsFailure)
            return Result.Failure<TermsVersionResponse>(stored.Error);

        await repository.AddAsync(version, ct);
        await unitOfWork.SaveChangesAsync(ct);
        await bus.PublishAsync(
            TermsContentFile.SaveFileRequested(
                stored.Value,
                command.FileName,
                command.ContentType,
                command.Content.LongLength,
                command.CreatedByUserId,
                correlation.CorrelationId
            )
        );

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
