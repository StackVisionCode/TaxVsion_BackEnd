using System.Text.Json;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.FileTypes;
using TaxVision.CloudStorage.Domain.Quotas;

namespace TaxVision.CloudStorage.Application.Quotas;

// ------------------------------------------------------------------
// Política efectiva para los frontends (accept + validación previa)
// ------------------------------------------------------------------

public sealed record GetUploadPolicyQuery(Guid TenantId, StorageActorScope Scope, FolderType FolderType);

/// <summary>Lo que el usuario actual puede subir en esa carpeta; <see cref="Accept"/> va directo al input de archivo.</summary>
public sealed record UploadPolicyResponse(
    FolderType FolderType,
    long MaxFileSizeBytes,
    IReadOnlyCollection<string> AllowedExtensions,
    IReadOnlyCollection<string> AllowedContentTypes,
    string Accept
);

public static class GetUploadPolicyHandler
{
    public static async Task<Result<UploadPolicyResponse>> Handle(
        GetUploadPolicyQuery query,
        IStorageLimitRepository limits,
        IUploadPolicyResolver resolver,
        CancellationToken ct
    )
    {
        var limit = await limits.GetAsync(query.TenantId, ct);
        if (limit is null)
            return Result.Failure<UploadPolicyResponse>(QuotaErrors.NotProvisioned);

        var policy = await resolver.ResolveAsync(
            query.TenantId,
            limit,
            query.FolderType,
            query.Scope.IsCustomerPortal,
            ct
        );
        return Result.Success(
            new UploadPolicyResponse(
                query.FolderType,
                policy.MaxFileSizeBytes,
                policy.AllowedExtensions,
                policy.AllowedContentTypes,
                string.Join(',', policy.AllowedExtensions.Concat(policy.AllowedContentTypes))
            )
        );
    }
}

// ------------------------------------------------------------------
// Catálogo y políticas por carpeta (PlatformAdmin)
// ------------------------------------------------------------------

public sealed record ListFilePolicyCatalogQuery;

public sealed record FileTypeView(string Extension, string Group, IReadOnlyList<string> ContentTypes, bool IsEnabled);

public sealed record FolderPolicyView(
    FolderType FolderType,
    long MaxSizeBytes,
    IReadOnlyList<string> AllowedExtensions,
    bool UsesPlanFileTypes,
    bool UsesPlanFileSize
);

public sealed record FilePolicyCatalogResponse(
    IReadOnlyList<FileTypeView> FileTypes,
    IReadOnlyList<FolderPolicyView> FolderPolicies
);

public sealed record UpsertFileTypeCommand(
    string Extension,
    string Group,
    IReadOnlyList<string> ContentTypes,
    bool IsEnabled,
    Guid ActorUserId
);

public sealed record UpdateFolderPolicyCommand(
    FolderType FolderType,
    long MaxSizeBytes,
    IReadOnlyList<string> AllowedExtensions,
    bool UsesPlanFileTypes,
    bool UsesPlanFileSize,
    Guid ActorUserId
);

public static class FilePolicyCatalogHandler
{
    private const string FileTypeKind = "file_type";
    private const string FolderPolicyKind = "folder_policy";

    public static async Task<FilePolicyCatalogResponse> Handle(
        ListFilePolicyCatalogQuery query,
        IFilePolicyCatalogRepository repository,
        CancellationToken ct
    )
    {
        var fileTypes = await repository.ListFileTypesAsync(ct);
        var folders = await repository.ListFolderPoliciesAsync(ct);
        return new FilePolicyCatalogResponse(
            fileTypes.Select(ToView).ToList(),
            folders.OrderBy(folder => folder.FolderType.ToString(), StringComparer.Ordinal).Select(ToView).ToList()
        );
    }

    public static async Task<Result<FileTypeView>> Handle(
        UpsertFileTypeCommand command,
        IFilePolicyCatalogRepository repository,
        FilePolicyCatalogCache cache,
        ISystemClock clock,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var extension = FileTypeDefinition.NormalizeExtension(command.Extension);
        if (extension.IsFailure)
            return Result.Failure<FileTypeView>(extension.Error);

        var existing = await repository.GetFileTypeAsync(extension.Value, ct);
        var before = existing is null ? null : Snapshot(ToView(existing));
        FileTypeDefinition definition;
        if (existing is null)
        {
            var created = FileTypeDefinition.Create(
                extension.Value,
                command.Group,
                command.ContentTypes,
                command.ActorUserId,
                clock.UtcNow
            );
            if (created.IsFailure)
                return Result.Failure<FileTypeView>(created.Error);
            definition = created.Value;
            if (!command.IsEnabled)
                definition.Update(
                    definition.Group,
                    definition.ContentTypeList.ToList(),
                    enabled: false,
                    command.ActorUserId,
                    clock.UtcNow
                );
            repository.AddFileType(definition);
        }
        else
        {
            var updated = existing.Update(
                command.Group,
                command.ContentTypes,
                command.IsEnabled,
                command.ActorUserId,
                clock.UtcNow
            );
            if (updated.IsFailure)
                return Result.Failure<FileTypeView>(updated.Error);
            definition = existing;
        }

        var view = ToView(definition);
        repository.AddAudit(
            FilePolicyAudit.Record(
                FileTypeKind,
                definition.Extension,
                before,
                Snapshot(view),
                command.ActorUserId,
                clock.UtcNow
            )
        );
        await unitOfWork.SaveChangesAsync(ct);
        cache.Invalidate();
        return Result.Success(view);
    }

    public static async Task<Result<FolderPolicyView>> Handle(
        UpdateFolderPolicyCommand command,
        IFilePolicyCatalogRepository repository,
        FilePolicyCatalogCache cache,
        ISystemClock clock,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        // Cada extensión de una carpeta tiene que existir en el catálogo (de ahí salen sus MIME y su grupo).
        var known = (await repository.ListFileTypesAsync(ct))
            .Select(definition => definition.Extension)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var extension in command.AllowedExtensions)
        {
            var normalized = FileTypeDefinition.NormalizeExtension(extension);
            if (normalized.IsFailure)
                return Result.Failure<FolderPolicyView>(normalized.Error);
            if (!known.Contains(normalized.Value))
                return Result.Failure<FolderPolicyView>(FileTypeErrors.UnknownExtension);
        }

        var existing = await repository.GetFolderPolicyAsync(command.FolderType, ct);
        if (existing is null)
            return Result.Failure<FolderPolicyView>(
                new Error("FileType.FolderPolicyNotFound", "This folder has no file policy.")
            );

        var before = Snapshot(ToView(existing));
        var updated = existing.Update(
            command.MaxSizeBytes,
            command.AllowedExtensions,
            command.UsesPlanFileTypes,
            command.UsesPlanFileSize,
            command.ActorUserId,
            clock.UtcNow
        );
        if (updated.IsFailure)
            return Result.Failure<FolderPolicyView>(updated.Error);

        var view = ToView(existing);
        repository.AddAudit(
            FilePolicyAudit.Record(
                FolderPolicyKind,
                existing.FolderType.ToString(),
                before,
                Snapshot(view),
                command.ActorUserId,
                clock.UtcNow
            )
        );
        await unitOfWork.SaveChangesAsync(ct);
        cache.Invalidate();
        return Result.Success(view);
    }

    private static FileTypeView ToView(FileTypeDefinition definition) =>
        new(definition.Extension, definition.Group, definition.ContentTypeList, definition.IsEnabled);

    private static FolderPolicyView ToView(FolderFilePolicy policy) =>
        new(
            policy.FolderType,
            policy.MaxSizeBytes,
            policy.ExtensionList,
            policy.UsesPlanFileTypes,
            policy.UsesPlanFileSize
        );

    private static string Snapshot<T>(T value) => JsonSerializer.Serialize(value);
}

// ------------------------------------------------------------------
// Lo que suben los clientes del portal (oficina)
// ------------------------------------------------------------------

public sealed record GetPortalUploadSettingsQuery(Guid TenantId);

public sealed record UpdatePortalUploadSettingsCommand(
    Guid TenantId,
    IReadOnlyList<string>? FileTypeGroups,
    long? MaxFileBytes
);

/// <summary>
/// <see cref="PlanFileTypeGroups"/> y <see cref="PlanMaxFileBytes"/> son el techo (lo que vende el plan); las
/// restricciones del portal en null significan "lo mismo que el plan".
/// </summary>
public sealed record PortalUploadSettingsResponse(
    IReadOnlyCollection<string> PlanFileTypeGroups,
    long PlanMaxFileBytes,
    IReadOnlyCollection<string>? PortalFileTypeGroups,
    long? PortalMaxFileBytes
);

public static class PortalUploadSettingsHandler
{
    public static async Task<Result<PortalUploadSettingsResponse>> Handle(
        GetPortalUploadSettingsQuery query,
        IStorageLimitRepository limits,
        CancellationToken ct
    )
    {
        var limit = await limits.GetAsync(query.TenantId, ct);
        return limit is null
            ? Result.Failure<PortalUploadSettingsResponse>(QuotaErrors.NotProvisioned)
            : Result.Success(ToResponse(limit));
    }

    public static async Task<Result<PortalUploadSettingsResponse>> Handle(
        UpdatePortalUploadSettingsCommand command,
        IStorageLimitRepository limits,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var limit = await limits.GetAsync(command.TenantId, ct);
        if (limit is null)
            return Result.Failure<PortalUploadSettingsResponse>(QuotaErrors.NotProvisioned);

        var set = limit.SetPortalUploadRestriction(command.FileTypeGroups, command.MaxFileBytes);
        if (set.IsFailure)
            return Result.Failure<PortalUploadSettingsResponse>(set.Error);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(ToResponse(limit));
    }

    private static PortalUploadSettingsResponse ToResponse(TenantStorageLimit limit)
    {
        var plan = limit.PlanAllowance();
        var portal = limit.PortalRestriction();
        return new PortalUploadSettingsResponse(
            plan.FileTypeGroups.Order(StringComparer.Ordinal).ToList(),
            plan.MaxFileBytes,
            portal.FileTypeGroups?.Order(StringComparer.Ordinal).ToList(),
            portal.MaxFileBytes
        );
    }
}
