using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.CloudStorage.Domain.Files;

namespace TaxVision.CloudStorage.Domain.Folders;

/// <summary>
/// Fase C2 — carpeta navegable creada por el usuario (arbol logico, vive solo en
/// BD). No confundir con FolderType (Domain/Files/FileEnums.cs): esa es la
/// categoria fija que compone la ruta fisica del objeto en MinIO; esta es la
/// jerarquia que el usuario arma para organizar archivos, y no cambia nada del
/// almacenamiento fisico.
/// </summary>
public sealed class Folder : TenantEntity
{
    private Folder() { }

    public OwnerType OwnerType { get; private set; }
    public Guid? OwnerId { get; private set; }
    public Guid? ParentFolderId { get; private set; }
    public string Name { get; private set; } = default!;

    /// <summary>Path materializado (ej. "/Clientes/Oficina A/Recibos") para listar/mover sin recorrer el arbol recursivamente.</summary>
    public string RelativePath { get; private set; } = default!;
    public Guid CreatedBy { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>
    /// Ver <see cref="FolderCategory"/> — inmutable desde la creacion a proposito: identifica
    /// PARA QUE modulo esta carpeta es la ancla de un dueno, y reclasificarla despues rompería
    /// esa garantia de get-or-create. Si un modulo necesita otra categoria, crea otra carpeta.
    /// </summary>
    public string? Category { get; private set; }

    public static Result<Folder> Create(
        Guid id,
        Guid tenantId,
        OwnerType ownerType,
        Guid? ownerId,
        Guid? parentFolderId,
        FolderName name,
        string? parentRelativePath,
        Guid createdBy,
        DateTime nowUtc,
        FolderCategory? category = null
    )
    {
        if (ownerType != OwnerType.Tenant && ownerId is null)
            return Result.Failure<Folder>(FolderErrors.OwnerRequired);

        var folder = new Folder
        {
            Id = id,
            OwnerType = ownerType,
            OwnerId = ownerId,
            ParentFolderId = parentFolderId,
            Name = name.Value,
            RelativePath = ComposePath(parentRelativePath, name.Value),
            CreatedBy = createdBy,
            CreatedAtUtc = nowUtc,
            Category = category?.Value,
        };
        folder.SetTenant(tenantId);
        return Result.Success(folder);
    }

    public void Rename(FolderName newName, string? parentRelativePath)
    {
        Name = newName.Value;
        RelativePath = ComposePath(parentRelativePath, Name);
    }

    public void Reparent(Guid? newParentFolderId, string? newParentRelativePath)
    {
        ParentFolderId = newParentFolderId;
        RelativePath = ComposePath(newParentRelativePath, Name);
    }

    /// <summary>
    /// Recalcula SOLO el path materializado de un descendiente cuando un
    /// ancestro se renombra o se mueve — no toca Name ni ParentFolderId propios.
    /// Lo usa el handler al cascadear (ver RenameFolderHandler/MoveFolderHandler);
    /// mantener el path materializado consistente entre agregados es una
    /// responsabilidad de aplicacion, no de este agregado individual.
    /// </summary>
    public void RebasePath(string newRelativePath) => RelativePath = newRelativePath;

    private static string ComposePath(string? parentPath, string name) =>
        string.IsNullOrEmpty(parentPath) ? $"/{name}" : $"{parentPath}/{name}";
}
