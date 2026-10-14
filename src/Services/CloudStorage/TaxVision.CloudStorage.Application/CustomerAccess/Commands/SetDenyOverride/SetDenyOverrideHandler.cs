using BuildingBlocks.CustomerVisibility;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Domain.CustomerAccess;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Folders;

namespace TaxVision.CloudStorage.Application.CustomerAccess.Commands.SetDenyOverride;

public static class SetDenyOverrideHandler
{
    public static async Task<Result<SetDenyOverrideResponse>> Handle(
        SetDenyOverrideCommand command,
        ICustomerAccessRuleRepository rules,
        ICustomerAccessRuleAuditRepository audit,
        IFolderRepository folders,
        IFileObjectRepository files,
        ICustomerAssignmentProjectionStore assignments,
        ISystemClock clock,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var auth = await EnsureAuthorized(command, assignments, ct);
        if (auth.IsFailure)
            return Result.Failure<SetDenyOverrideResponse>(auth.Error);

        // Validamos y capturamos el nombre del recurso en el mismo paso: ya cargamos el folder/file,
        // asi la respuesta vuelve con ResourceName sin un segundo round-trip. TenantScope ⇒ null.
        var validation = await ValidateResource(command, folders, files, ct);
        if (validation.IsFailure)
            return Result.Failure<SetDenyOverrideResponse>(validation.Error);

        var result = await UpsertAndAudit(command, rules, audit, clock, validation.Value, ct);
        if (result.IsFailure)
            return Result.Failure<SetDenyOverrideResponse>(result.Error);

        await unitOfWork.SaveChangesAsync(ct);
        return result;
    }

    private static async Task<Result> EnsureAuthorized(
        SetDenyOverrideCommand command,
        ICustomerAssignmentProjectionStore assignments,
        CancellationToken ct
    )
    {
        if (command.HasManageAny)
            return Result.Success();
        if (!command.HasManageAssigned)
            return Result.Failure(CustomerAccessErrors.Forbidden);

        var assigned = await assignments.IsAnyAssignedToUserAsync(
            command.TenantId,
            command.ActorUserId,
            [command.CustomerId],
            ct
        );
        return assigned ? Result.Success() : Result.Failure(CustomerAccessErrors.Forbidden);
    }

    private static async Task<Result<string?>> ValidateResource(
        SetDenyOverrideCommand command,
        IFolderRepository folders,
        IFileObjectRepository files,
        CancellationToken ct
    )
    {
        if (command.ResourceType == AccessResourceType.Folder)
        {
            var folder = await folders.GetAsync(command.TenantId, command.ResourceId, ct);
            if (folder is null)
                return Result.Failure<string?>(CustomerAccessErrors.ResourceNotFound);
            if (folder.OwnerType != OwnerType.Customer || folder.OwnerId != command.CustomerId)
                return Result.Failure<string?>(CustomerAccessErrors.ResourceNotOwnedByCustomer);
            return Result.Success<string?>(folder.Name);
        }

        if (command.ResourceType == AccessResourceType.File)
        {
            var file = await files.GetAsync(command.TenantId, command.ResourceId, ct);
            if (file is null)
                return Result.Failure<string?>(CustomerAccessErrors.ResourceNotFound);
            if (file.OwnerType != OwnerType.Customer || file.OwnerId != command.CustomerId)
                return Result.Failure<string?>(CustomerAccessErrors.ResourceNotOwnedByCustomer);
            return Result.Success<string?>(file.OriginalName);
        }

        // Preset tenant-scope: no hay folder/file que validar; el ResourceId debe ser el TenantId
        // del JWT (fail-closed: nunca aceptar un valor arbitrario del body).
        if (command.ResourceType == AccessResourceType.TenantScope)
        {
            return command.ResourceId == command.TenantId
                ? Result.Success<string?>(null)
                : Result.Failure<string?>(CustomerAccessErrors.ResourceNotOwnedByCustomer);
        }

        return Result.Failure<string?>(CustomerAccessErrors.ResourceTypeNotSupported);
    }

    private static async Task<Result<SetDenyOverrideResponse>> UpsertAndAudit(
        SetDenyOverrideCommand command,
        ICustomerAccessRuleRepository rules,
        ICustomerAccessRuleAuditRepository audit,
        ISystemClock clock,
        string? resourceName,
        CancellationToken ct
    )
    {
        var now = clock.UtcNow;
        var existing = await rules.GetAsync(
            command.TenantId,
            command.CustomerId,
            command.ResourceType,
            command.ResourceId,
            command.Capability,
            ct
        );

        CustomerDocumentAccessRule rule;
        bool created;
        if (existing is null)
        {
            var createResult = CustomerDocumentAccessRule.Create(
                Guid.NewGuid(),
                command.TenantId,
                command.CustomerId,
                command.ResourceType,
                command.ResourceId,
                command.Capability,
                command.ActorUserId,
                now,
                command.Reason
            );
            if (createResult.IsFailure)
                return Result.Failure<SetDenyOverrideResponse>(createResult.Error);
            rule = createResult.Value;
            rules.Add(rule);
            created = true;
        }
        else
        {
            var refreshed = existing.Refresh(command.ActorUserId, now, command.Reason);
            if (refreshed.IsFailure)
                return Result.Failure<SetDenyOverrideResponse>(refreshed.Error);
            rule = existing;
            created = false;
        }

        audit.Add(
            CustomerAccessRuleAudit.Created(
                rule.TenantId,
                rule.CustomerId,
                rule.ResourceType,
                rule.ResourceId,
                rule.Capability,
                rule.SetByUserId,
                now,
                rule.Reason
            )
        );
        return Result.Success(new SetDenyOverrideResponse(CustomerAccessRuleMapper.Map(rule, resourceName), created));
    }
}
