using BuildingBlocks.Results;

namespace TaxVision.CloudStorage.Domain.Folders;

/// <summary>
/// Tag logico opcional que un modulo consumidor (Customer, Tenant, etc.) define como
/// constante propia (ej. "customer.documents") para marcar la carpeta "ancla" que ese
/// modulo siempre reutiliza para un mismo dueno (OwnerType+OwnerId) — no es un enum fijo
/// en CloudStorage, cada consumidor inventa su propia convencion sin que este servicio
/// tenga que conocerla de antemano. Folders organizados libremente por el usuario (el
/// arbol tipo Explorer) no llevan Category.
///
/// Junto con el indice unico filtrado (TenantId, OwnerType, OwnerId, Category) WHERE
/// Category IS NOT NULL (ver FolderConfiguration), esto resuelve dos gaps reales
/// auditados el 2026-07-20: (1) dos interfaces creando folders para el mismo dueno ya no
/// terminan con folders raiz duplicados y huerfanos entre si — get-or-create real via
/// CreateFolderHandler; (2) dos duenos distintos ya no chocan entre si al intentar
/// nombrar su folder raiz igual (ver el ensanche de NameExistsUnderParentAsync a
/// OwnerType+OwnerId en el mismo cambio).
/// </summary>
public sealed record FolderCategory
{
    private const int MaxLength = 100;

    private FolderCategory(string value) => Value = value;

    public string Value { get; }

    public static Result<FolderCategory> Create(string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > MaxLength)
            return Result.Failure<FolderCategory>(FolderErrors.InvalidCategory);

        if (!IsValidSlug(trimmed))
            return Result.Failure<FolderCategory>(FolderErrors.InvalidCategory);

        return Result.Success(new FolderCategory(trimmed));
    }

    /// <summary>
    /// Convencion de identificador tecnico estable (ej. "customer.documents"), no texto
    /// libre visible al usuario final como FolderName — minusculas ascii, digitos, y
    /// '.', '-', '_' como separadores. Sin LINQ (Domain).
    /// </summary>
    private static bool IsValidSlug(string value)
    {
        foreach (var c in value)
        {
            var isAllowed = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c is '.' or '-' or '_';
            if (!isAllowed)
                return false;
        }
        return true;
    }
}
