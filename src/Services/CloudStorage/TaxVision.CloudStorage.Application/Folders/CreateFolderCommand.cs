using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Folders;

namespace TaxVision.CloudStorage.Application.Folders;

/// <summary>
/// Fase C2 — crea una carpeta navegable (arbol logico, no toca MinIO). Category (ver
/// FolderCategory.cs) es opcional: cuando viene informado, esta misma llamada tiene
/// semantica get-or-create — dos interfaces distintas pidiendo la carpeta ancla de un mismo
/// dueno con la misma Category siempre convergen al mismo Folder, nunca duplican.
/// </summary>
public sealed record CreateFolderCommand(
    Guid TenantId,
    Guid ActorId,
    StorageActorScope Scope,
    Guid? ParentFolderId,
    string? Name,
    OwnerType OwnerType,
    Guid? OwnerId,
    string? Category = null
);

public static class CreateFolderHandler
{
    private const string UniqueConstraintCode = "Persistence.UniqueConstraint";

    public static async Task<Result<FolderResponse>> Handle(
        CreateFolderCommand command,
        IFolderRepository folders,
        ISystemClock clock,
        IUnitOfWork unitOfWork,
        ILogger<Folder> logger,
        CancellationToken ct
    )
    {
        if (!command.Scope.CanCreate(command.OwnerType, command.OwnerId))
            return Result.Failure<FolderResponse>(FolderErrors.Forbidden);

        var built = await ValidateAndBuildFolder(command, folders, clock, ct);
        if (built.IsFailure)
            return Result.Failure<FolderResponse>(built.Error);

        return await PersistOrReturnExisting(command, built.Value, folders, unitOfWork, logger, ct);
    }

    private static async Task<Result<Folder>> ValidateAndBuildFolder(
        CreateFolderCommand command,
        IFolderRepository folders,
        ISystemClock clock,
        CancellationToken ct
    )
    {
        var nameResult = FolderName.Create(command.Name);
        if (nameResult.IsFailure)
            return Result.Failure<Folder>(nameResult.Error);

        var categoryResult = ResolveCategory(command.Category);
        if (categoryResult.IsFailure)
            return Result.Failure<Folder>(categoryResult.Error);

        var parentPathResult = await ResolveParentPath(command, folders, ct);
        if (parentPathResult.IsFailure)
            return Result.Failure<Folder>(parentPathResult.Error);

        if (
            await folders.NameExistsUnderParentAsync(
                command.TenantId,
                command.ParentFolderId,
                nameResult.Value.Value,
                command.OwnerType,
                command.OwnerId,
                null,
                ct
            )
        )
            return Result.Failure<Folder>(FolderErrors.NameAlreadyExists);

        return Folder.Create(
            Guid.NewGuid(),
            command.TenantId,
            command.OwnerType,
            command.OwnerId,
            command.ParentFolderId,
            nameResult.Value,
            parentPathResult.Value,
            command.ActorId,
            clock.UtcNow,
            categoryResult.Value
        );
    }

    private static Result<FolderCategory?> ResolveCategory(string? category)
    {
        if (category is null)
            return Result.Success<FolderCategory?>(null);

        // Un usuario no puede crear una carpeta con una category de sistema (sys.*): esas solo las
        // materializa el provisioner, y protegerlas de crear/renombrar/borrar depende de esa reserva.
        if (SystemFolderCatalog.IsSystemCategory(category.Trim()))
            return Result.Failure<FolderCategory?>(FolderErrors.SystemFolderProtected);

        var result = FolderCategory.Create(category);
        return result.IsFailure
            ? Result.Failure<FolderCategory?>(result.Error)
            : Result.Success<FolderCategory?>(result.Value);
    }

    /// <summary>
    /// Los indices unicos filtrados IX_Folders_Owner_Category (con Category) y
    /// UX_Folders_Owner_Parent_Name (sin Category) respaldan una carrera real: si dos
    /// requests concurrentes pasan el pre-check de arriba y ambas intentan insertar, la
    /// segunda choca con ConflictException — en vez de propagar el error, buscamos la fila
    /// que gano la carrera y la devolvemos como si la hubieramos creado nosotros. Mismo
    /// patron que StartCustomerImportHandler (Customer.Application) para el race de IdempotencyKey.
    /// </summary>
    private static async Task<Result<FolderResponse>> PersistOrReturnExisting(
        CreateFolderCommand command,
        Folder folder,
        IFolderRepository folders,
        IUnitOfWork unitOfWork,
        ILogger<Folder> logger,
        CancellationToken ct
    )
    {
        folders.Add(folder);
        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (ConflictException conflict) when (conflict.Code == UniqueConstraintCode)
        {
            logger.LogInformation(
                "CreateFolder hit unique violation for tenant {TenantId} owner {OwnerType}/{OwnerId} category {Category} "
                    + "(concurrent create race) — returning the folder that won the race.",
                command.TenantId,
                command.OwnerType,
                command.OwnerId,
                command.Category
            );
            // Sale del seguimiento: el guardado final de la transacción no debe repetir el INSERT.
            folders.Remove(folder);
            var existing = command.Category is not null
                ? await folders.GetByOwnerAndCategoryAsync(
                    command.TenantId,
                    command.OwnerType,
                    command.OwnerId,
                    command.Category,
                    ct
                )
                : await folders.GetByNameUnderParentAsync(
                    command.TenantId,
                    command.ParentFolderId,
                    folder.Name,
                    command.OwnerType,
                    command.OwnerId,
                    ct
                );
            if (existing is not null)
                return Result.Success(FolderResponseMapper.Map(existing));

            throw; // El indice rechazo la insercion: la fila ganadora tiene que existir. Si no aparece, algo mas grave paso.
        }

        return Result.Success(FolderResponseMapper.Map(folder));
    }

    private static async Task<Result<string?>> ResolveParentPath(
        CreateFolderCommand command,
        IFolderRepository folders,
        CancellationToken ct
    )
    {
        if (command.ParentFolderId is not { } parentId)
            return Result.Success<string?>(null);

        var parent = await folders.GetAsync(command.TenantId, parentId, ct);
        if (parent is null)
            return Result.Failure<string?>(FolderErrors.ParentNotFound);
        if (parent.OwnerType != command.OwnerType || parent.OwnerId != command.OwnerId)
            return Result.Failure<string?>(FolderErrors.OwnerMismatch);

        return Result.Success<string?>(parent.RelativePath);
    }
}
