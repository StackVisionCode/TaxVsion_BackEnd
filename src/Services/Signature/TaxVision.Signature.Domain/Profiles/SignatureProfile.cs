using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Signature.Domain.Profiles;

/// <summary>
/// Firma reutilizable del preparador/oficina (facsímil visual estilo "rubber stamp" del 8879). La
/// imagen PNG vive en CloudStorage (<see cref="FileId"/>); aquí solo el metadato del pick-list.
/// El ámbito lo define <see cref="OwnerUserId"/>: un usuario concreto (firma personal) o <c>null</c>
/// (firma de oficina, gestionada por el TenantAdmin). Una sola por defecto por ámbito.
/// </summary>
public sealed class SignatureProfile : TenantEntity
{
    public const int MinLabelLength = 1;
    public const int MaxLabelLength = 80;

    /// <summary>Tope de firmas activas (no archivadas) por ámbito (usuario u oficina). Archivar libera cupo.</summary>
    public const int MaxActiveProfilesPerScope = 10;

    private SignatureProfile() { }

    /// <summary>Dueño de la firma: un usuario (firma personal) o <c>null</c> = firma de oficina del tenant.</summary>
    public Guid? OwnerUserId { get; private set; }

    /// <summary>Etiqueta legible que el usuario le da a esta firma (p. ej. "Firma azul").</summary>
    public string Label { get; private set; } = default!;

    /// <summary>FileId del PNG en CloudStorage (OwnerType Signature / FolderType Signatures).</summary>
    public Guid FileId { get; private set; }

    public int Width { get; private set; }
    public int Height { get; private set; }

    /// <summary>La firma por defecto del ámbito (una sola por (TenantId, OwnerUserId)).</summary>
    public bool IsDefault { get; private set; }
    public bool IsArchived { get; private set; }

    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>true = firma de oficina (sin dueño usuario).</summary>
    public bool IsOffice => OwnerUserId is null;

    public static Result<SignatureProfile> Create(
        Guid tenantId,
        Guid createdByUserId,
        Guid? ownerUserId,
        string label,
        Guid fileId,
        int width,
        int height
    )
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<SignatureProfile>(new Error("Signature.Profile.Tenant", "TenantId is required."));
        if (createdByUserId == Guid.Empty)
            return Result.Failure<SignatureProfile>(
                new Error("Signature.Profile.CreatedBy", "CreatedByUserId is required.")
            );
        if (fileId == Guid.Empty)
            return Result.Failure<SignatureProfile>(new Error("Signature.Profile.File", "FileId is required."));
        if (width <= 0 || height <= 0)
            return Result.Failure<SignatureProfile>(
                new Error("Signature.Profile.Dimensions", "Width and height must be positive.")
            );

        var labelResult = ValidateLabel(label, out var trimmed);
        if (labelResult.IsFailure)
            return Result.Failure<SignatureProfile>(labelResult.Error);

        var now = DateTime.UtcNow;
        var profile = new SignatureProfile
        {
            Id = Guid.NewGuid(),
            OwnerUserId = ownerUserId,
            CreatedByUserId = createdByUserId,
            Label = trimmed,
            FileId = fileId,
            Width = width,
            Height = height,
            IsDefault = false,
            IsArchived = false,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        profile.SetTenant(tenantId);
        return Result.Success(profile);
    }

    public Result Rename(string label)
    {
        var result = ValidateLabel(label, out var trimmed);
        if (result.IsFailure)
            return result;

        Label = trimmed;
        Touch();
        return Result.Success();
    }

    /// <summary>Marca esta firma como la por defecto de su ámbito. El handler se encarga de desmarcar la anterior.</summary>
    public Result MarkDefault()
    {
        if (IsArchived)
            return Result.Failure(
                new Error("Signature.Profile.ArchivedDefault", "An archived signature cannot be the default.")
            );

        IsDefault = true;
        Touch();
        return Result.Success();
    }

    public void UnsetDefault()
    {
        if (!IsDefault)
            return;
        IsDefault = false;
        Touch();
    }

    /// <summary>Archiva la firma; una firma por defecto deja de serlo al archivarse (no puede quedar oculta y default).</summary>
    public Result Archive()
    {
        IsArchived = true;
        IsDefault = false;
        Touch();
        return Result.Success();
    }

    public Result Unarchive()
    {
        IsArchived = false;
        Touch();
        return Result.Success();
    }

    private void Touch() => UpdatedAtUtc = DateTime.UtcNow;

    private static Result ValidateLabel(string label, out string trimmed)
    {
        trimmed = (label ?? string.Empty).Trim();
        if (trimmed.Length is < MinLabelLength or > MaxLabelLength)
            return Result.Failure(
                new Error(
                    "Signature.Profile.Label",
                    $"Label must be between {MinLabelLength} and {MaxLabelLength} characters."
                )
            );
        return Result.Success();
    }
}
