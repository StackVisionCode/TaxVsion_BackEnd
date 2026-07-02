using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.Roles;

namespace TaxVision.Auth.Application.Roles.Commands;

public sealed record CreateRoleCommand(
    Guid TenantId,
    Guid CreatedByUserId,
    string Name,
    string? Description,
    IReadOnlyList<Guid> PermissionIds);

public sealed record RoleResponse(
    Guid Id,
    string Name,
    string? Description,
    bool IsSystem,
    bool IsActive,
    IReadOnlyList<string> PermissionCodes);

public static class CreateRoleHandler
{
    public static async Task<Result<RoleResponse>> Handle(
        CreateRoleCommand command,
        IRoleRepository roles,
        IAuthAuditWriter audit,
        IRequestContext request,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        if (await roles.NameExistsAsync(command.TenantId, command.Name?.Trim() ?? string.Empty, ct))
        {
            return Result.Failure<RoleResponse>(
                new Error("Role.NameConflict", "A role with this name already exists."));
        }

        var roleResult = Role.Create(command.TenantId, command.Name!, command.Description);
        if (roleResult.IsFailure)
            return Result.Failure<RoleResponse>(roleResult.Error);
        var role = roleResult.Value;

        var validation = await ValidatePermissionIdsAsync(roles, command.PermissionIds, ct);
        if (validation.IsFailure)
            return Result.Failure<RoleResponse>(validation.Error);

        var setResult = role.SetPermissions(command.PermissionIds?.Distinct().ToList() ?? []);
        if (setResult.IsFailure)
            return Result.Failure<RoleResponse>(setResult.Error);

        await roles.AddAsync(role, ct);
        await audit.AddAsync(
            AuthAuditLog.Record(
                command.TenantId, command.CreatedByUserId, AuthAuditAction.RoleCreated, true,
                request.IpAddress, request.UserAgent, correlation.CorrelationId,
                targetType: "Role", targetId: role.Id),
            ct);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(await ToResponseAsync(role, roles, ct));
    }

    internal static async Task<Result> ValidatePermissionIdsAsync(
        IRoleRepository roles,
        IReadOnlyList<Guid>? permissionIds,
        CancellationToken ct)
    {
        if (permissionIds is null || permissionIds.Count == 0)
            return Result.Success();

        var catalog = await roles.GetPermissionsCatalogAsync(ct);
        var known = catalog.Select(permission => permission.Id).ToHashSet();
        return permissionIds.All(known.Contains)
            ? Result.Success()
            : Result.Failure(new Error("Permission.NotFound", "One or more permissions do not exist."));
    }

    internal static async Task<RoleResponse> ToResponseAsync(
        Role role,
        IRoleRepository roles,
        CancellationToken ct)
    {
        var catalog = await roles.GetPermissionsCatalogAsync(ct);
        var codesById = catalog.ToDictionary(permission => permission.Id, permission => permission.Code);
        return new RoleResponse(
            role.Id,
            role.Name,
            role.Description,
            role.IsSystem,
            role.IsActive,
            role.Permissions
                .Where(link => codesById.ContainsKey(link.PermissionId))
                .Select(link => codesById[link.PermissionId])
                .OrderBy(code => code)
                .ToList());
    }
}

public sealed record UpdateRoleCommand(
    Guid TenantId,
    Guid RoleId,
    Guid UpdatedByUserId,
    string Name,
    string? Description);

public static class UpdateRoleHandler
{
    public static async Task<Result> Handle(
        UpdateRoleCommand command,
        IRoleRepository roles,
        IAuthAuditWriter audit,
        IRequestContext request,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        var role = await roles.GetByIdAsync(command.RoleId, ct);
        if (role is null || role.TenantId != command.TenantId)
            return Result.Failure(new Error("Role.NotFound", "Role does not exist."));

        var result = role.Update(command.Name, command.Description);
        if (result.IsFailure)
            return result;

        await audit.AddAsync(
            AuthAuditLog.Record(
                command.TenantId, command.UpdatedByUserId, AuthAuditAction.RoleUpdated, true,
                request.IpAddress, request.UserAgent, correlation.CorrelationId,
                targetType: "Role", targetId: role.Id),
            ct);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public sealed record SetRolePermissionsCommand(
    Guid TenantId,
    Guid RoleId,
    Guid UpdatedByUserId,
    IReadOnlyList<Guid> PermissionIds);

public static class SetRolePermissionsHandler
{
    public static async Task<Result> Handle(
        SetRolePermissionsCommand command,
        IRoleRepository roles,
        IAuthAuditWriter audit,
        IRequestContext request,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        var role = await roles.GetByIdAsync(command.RoleId, ct);
        if (role is null || role.TenantId != command.TenantId)
            return Result.Failure(new Error("Role.NotFound", "Role does not exist."));

        var validation = await CreateRoleHandler.ValidatePermissionIdsAsync(
            roles, command.PermissionIds, ct);
        if (validation.IsFailure)
            return validation;

        var result = role.SetPermissions(command.PermissionIds?.Distinct().ToList() ?? []);
        if (result.IsFailure)
            return result;

        await audit.AddAsync(
            AuthAuditLog.Record(
                command.TenantId, command.UpdatedByUserId, AuthAuditAction.RoleUpdated, true,
                request.IpAddress, request.UserAgent, correlation.CorrelationId,
                targetType: "Role", targetId: role.Id,
                detailsJson: """{"change":"permissions"}"""),
            ct);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public sealed record DeactivateRoleCommand(
    Guid TenantId,
    Guid RoleId,
    Guid RequestedByUserId);

public static class DeactivateRoleHandler
{
    public static async Task<Result> Handle(
        DeactivateRoleCommand command,
        IRoleRepository roles,
        IAuthAuditWriter audit,
        IRequestContext request,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        var role = await roles.GetByIdAsync(command.RoleId, ct);
        if (role is null || role.TenantId != command.TenantId)
            return Result.Failure(new Error("Role.NotFound", "Role does not exist."));

        var result = role.Deactivate();
        if (result.IsFailure)
            return result;

        await audit.AddAsync(
            AuthAuditLog.Record(
                command.TenantId, command.RequestedByUserId, AuthAuditAction.RoleDeactivated, true,
                request.IpAddress, request.UserAgent, correlation.CorrelationId,
                targetType: "Role", targetId: role.Id),
            ct);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
