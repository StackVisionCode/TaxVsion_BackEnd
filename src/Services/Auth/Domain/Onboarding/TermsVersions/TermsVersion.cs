using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Auth.Domain.Onboarding.TermsVersions;

/// <summary>
/// Version inmutable y publicada de un documento legal (ToS/Privacy Policy). Igual que
/// TenantOnboarding/EmailVerificationChallenge, hereda BaseEntity (no AggregateRoot):
/// es un recurso a nivel de plataforma, no de tenant, y se consulta anonimamente
/// (GET /auth/onboarding/terms/current) antes de que exista cualquier tenant.
/// ContentUri/ContentHash son nullable a nivel de columna solo para permitir la fila
/// semilla legacy que inserta la migracion de retrofit (Fase 6) — Publish() los exige
/// siempre para cualquier version nueva.
/// <para>
/// Auditoría (gap MinIO/legal-docs) — el documento real ya no vive en una URL externa: se sube
/// a CloudStorage (mismo patrón D0/D1 que el resto del repo) y <see cref="ContentFileId"/> es el
/// FileId resultante. <see cref="ContentUri"/> pasa a ser la URL mediadora auto-referencial de
/// Auth (<c>/auth/onboarding/terms/{Id}/content</c>) — no se puede calcular dentro de
/// <see cref="Publish"/> mismo porque <see cref="BuildingBlocks.Domain.BaseEntity.Id"/> ya existe
/// en ese momento (es client-generated), así que se fija después vía <see cref="SetContentUri"/>.
/// </para>
/// </summary>
public sealed class TermsVersion : BaseEntity
{
    private TermsVersion() { }

    public TermsKind Kind { get; private set; }
    public string Version { get; private set; } = default!;
    public Guid? ContentFileId { get; private set; }
    public string? ContentUri { get; private set; }
    public string? ContentHash { get; private set; }
    public DateTime EffectiveFromUtc { get; private set; }
    public DateTime? EffectiveUntilUtc { get; private set; }
    public string Locale { get; private set; } = default!;
    public DateTime CreatedAtUtc { get; private set; }
    public Guid CreatedByUserId { get; private set; }

    public static Result<TermsVersion> Publish(
        TermsKind kind,
        string version,
        Guid contentFileId,
        string contentHash,
        string locale,
        Guid createdByUserId,
        DateTime nowUtc,
        DateTime? effectiveUntilUtc = null
    )
    {
        if (string.IsNullOrWhiteSpace(version) || version.Length > 64)
            return Result.Failure<TermsVersion>(
                new Error("Onboarding.TermsVersionInvalid", "Version must be between 1 and 64 characters.")
            );

        if (contentFileId == Guid.Empty)
            return Result.Failure<TermsVersion>(
                new Error("Onboarding.TermsContentFileIdRequired", "ContentFileId is required.")
            );

        if (!IsValidContentHash(contentHash))
            return Result.Failure<TermsVersion>(
                new Error(
                    "Onboarding.TermsContentHashInvalid",
                    "ContentHash must be a 64-character lowercase hex SHA-256 digest."
                )
            );

        if (string.IsNullOrWhiteSpace(locale) || locale.Length > 16)
            return Result.Failure<TermsVersion>(
                new Error("Onboarding.TermsLocaleInvalid", "Locale must be between 1 and 16 characters.")
            );

        if (createdByUserId == Guid.Empty)
            return Result.Failure<TermsVersion>(
                new Error("Onboarding.TermsCreatedByRequired", "CreatedByUserId is required.")
            );

        if (effectiveUntilUtc is not null && effectiveUntilUtc <= nowUtc)
            return Result.Failure<TermsVersion>(
                new Error("Onboarding.TermsEffectiveUntilInvalid", "EffectiveUntilUtc must be in the future.")
            );

        return Result.Success(
            new TermsVersion
            {
                Kind = kind,
                Version = version,
                ContentFileId = contentFileId,
                ContentHash = contentHash.ToLowerInvariant(),
                EffectiveFromUtc = nowUtc,
                EffectiveUntilUtc = effectiveUntilUtc,
                Locale = locale,
                CreatedAtUtc = nowUtc,
                CreatedByUserId = createdByUserId,
            }
        );
    }

    /// <summary>Fija la URL mediadora auto-referencial una vez que el Id (client-generated) ya existe.</summary>
    public Result SetContentUri(string contentUri)
    {
        if (string.IsNullOrWhiteSpace(contentUri) || contentUri.Length > 2048)
            return Result.Failure(
                new Error(
                    "Onboarding.TermsContentUriInvalid",
                    "ContentUri is required and must be at most 2048 characters."
                )
            );

        ContentUri = contentUri;
        return Result.Success();
    }

    private static bool IsValidContentHash(string? hash)
    {
        if (string.IsNullOrEmpty(hash) || hash.Length != 64)
            return false;

        foreach (var c in hash)
        {
            if (c is (< '0' or > '9') and (< 'a' or > 'f') and (< 'A' or > 'F'))
                return false;
        }

        return true;
    }
}
