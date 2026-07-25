using System.Text.Json;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Application.Users;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.Invitations;
using TaxVision.Auth.Domain.Roles;
using TaxVision.Auth.Domain.Users;
using Wolverine;

namespace TaxVision.Auth.Application.Invitations.Commands;

public sealed record AcceptInvitationCommand(string InvitationToken, string Name, string LastName, string Password);

public static class AcceptInvitationHandler
{
    public static async Task<Result<UserResponse>> Handle(
        AcceptInvitationCommand command,
        IInvitationRepository invitations,
        IInvitationTokenService tokens,
        IUserRepository users,
        ITenantRegistry tenants,
        IPasswordHasher passwordHasher,
        IRoleRepository roles,
        IAuthAuditWriter audit,
        IRequestContext request,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var tokenHash = tokens.Hash(command.InvitationToken);
        var invitation = await invitations.GetByTokenHashAsync(tokenHash, ct);
        if (invitation is null || !invitation.MatchesTokenHash(tokenHash))
        {
            return Result.Failure<UserResponse>(
                new Error("Auth.InvalidInvitation", "Invitation is invalid or expired.")
            );
        }

        if (invitation.Status == InvitationStatus.Accepted && invitation.AcceptedByUserId is Guid existingUserId)
        {
            var existing = await users.GetByIdAsync(existingUserId, ct);
            if (existing is not null)
                return Result.Success(ToResponse(existing));
        }

        var now = DateTime.UtcNow;
        if (invitation.MarkExpired(now))
        {
            await unitOfWork.SaveChangesAsync(ct);
            return Result.Failure<UserResponse>(
                new Error("Auth.InvalidInvitation", "Invitation is invalid or expired.")
            );
        }

        if (invitation.Status != InvitationStatus.Pending)
        {
            return Result.Failure<UserResponse>(
                new Error("Auth.InvalidInvitation", "Invitation is invalid or expired.")
            );
        }

        var tenant = await tenants.GetByIdAsync(invitation.TenantId, ct);
        if (tenant is null || !tenant.IsActive)
        {
            return Result.Failure<UserResponse>(new Error("Tenant.Inactive", "Tenant does not exist or is inactive."));
        }

        var passwordResult = PasswordPolicy.Validate(command.Password, invitation.Email);
        if (passwordResult.IsFailure)
            return Result.Failure<UserResponse>(passwordResult.Error);

        if (await users.EmailExistsAsync(invitation.TenantId, invitation.Email, ct))
        {
            return Result.Failure<UserResponse>(
                new Error("User.EmailConflict", "Email is already registered in this tenant.")
            );
        }

        var userResult = User.Register(
            invitation.TenantId,
            command.Name,
            command.LastName,
            invitation.Email,
            passwordHasher.Hash(command.Password),
            invitation.ActorType,
            invitation.CustomerId
        );
        if (userResult.IsFailure)
            return Result.Failure<UserResponse>(userResult.Error);

        var user = userResult.Value;

        // El token llegó al buzón del invitado: el email queda verificado.
        user.VerifyEmail();

        var acceptResult = invitation.Accept(user.Id, now);
        if (acceptResult.IsFailure)
            return Result.Failure<UserResponse>(acceptResult.Error);

        await users.AddAsync(user, ct);

        // Roles RBAC: los indicados en la invitación o el rol de sistema del actor.
        var roleIds = ResolveInvitationRoleIds(invitation);
        if (roleIds.Count == 0)
        {
            var systemRoleName = invitation.ActorType switch
            {
                UserActorType.TenantAdmin => Role.SystemTenantAdmin,
                UserActorType.TenantEmployee => Role.SystemEmployee,
                UserActorType.CustomerPortal => Role.SystemCustomerPortal,
                _ => null,
            };
            if (systemRoleName is not null)
            {
                var systemRole = await roles.GetSystemRoleAsync(invitation.TenantId, systemRoleName, ct);
                if (systemRole is not null)
                    roleIds = [systemRole.Id];
            }
        }

        if (roleIds.Count > 0)
        {
            await roles.ReplaceUserRolesAsync(user.Id, roleIds, invitation.InvitedByUserId, ct);
            user.BumpPermissionsVersion();

            // RBAC Fase 7/7.5: sin esto, un usuario dado de alta por invitación nunca tenía fila
            // en UserPermissionsProjection de ningún servicio (ni tampoco PermissionsVersion > 0,
            // que es justo lo que PermissionsBackfillService exige para repararlo después) —
            // ProjectionPermissionsSource lo rechaza en frío en TODO endpoint [HasPermission],
            // para siempre, porque nada vuelve a tocar su versión salvo una reasignación manual
            // de roles. Publicar el mismo evento que AssignUserRolesHandler ya usa lo deja bien
            // desde el alta, sin depender del backfill de arranque de Auth.
            var tenantRoles = await roles.GetByIdsAsync(invitation.TenantId, roleIds, ct);
            var catalog = await roles.GetPermissionsCatalogAsync(ct);

            await bus.PublishAsync(
                new UserRolesChangedIntegrationEvent
                {
                    TenantId = user.TenantId,
                    UserId = user.Id,
                    PermissionsVersion = user.PermissionsVersion,
                    RoleNames = tenantRoles.Select(role => role.Name).ToArray(),
                    RoleIds = tenantRoles.Select(role => role.Id).ToArray(),
                    PermissionCodes = ResolveEffectivePermissionCodes(tenantRoles, catalog),
                    CorrelationId = correlation.CorrelationId,
                }
            );

            // Ya quedó reparado acá mismo — que PermissionsBackfillService no lo vuelva a
            // reprocesar (y republicar) en cada arranque de Auth de ahí en adelante.
            user.MarkPermissionsBackfilled(now);
        }

        // Publicar ANTES de SaveChangesAsync: Wolverine + UseEntityFrameworkCoreTransactions()
        // agrupa el outbox del mensaje con el commit del UnitOfWork cuando PublishAsync
        // ocurre antes del SaveChanges del mismo DbContext (mismo patrón que
        // DeactivateUserHandler/AssignUserRolesHandler). Publicar después del commit
        // (como estaba antes) rompe la atomicidad: si el proceso muere entre el
        // SaveChanges y el PublishAsync, el usuario queda creado pero el evento nunca sale.
        await bus.PublishAsync(
            new UserRegisteredIntegrationEvent
            {
                UserId = user.Id,
                TenantId = user.TenantId,
                Email = user.Email,
                ActorType = user.ActorType.ToString(),
                CustomerId = user.CustomerId,
                Name = user.Name,
                LastName = user.LastName,
                CorrelationId = correlation.CorrelationId,
            }
        );

        await audit.AddAsync(
            AuthAuditLog.Record(
                invitation.TenantId,
                user.Id,
                AuthAuditAction.InvitationAccepted,
                true,
                request.IpAddress,
                request.UserAgent,
                correlation.CorrelationId,
                targetType: "Invitation",
                targetId: invitation.Id
            ),
            ct
        );
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(ToResponse(user));
    }

    private static List<Guid> ResolveInvitationRoleIds(Invitation invitation)
    {
        if (string.IsNullOrWhiteSpace(invitation.RoleIdsJson))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<Guid>>(invitation.RoleIdsJson) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    // Mismo cálculo que UserManagementCommands.ResolveEffectivePermissionCodes — duplicado a
    // propósito (helper privado de 6 líneas, no amerita una abstracción compartida nueva).
    private static string[] ResolveEffectivePermissionCodes(
        IReadOnlyList<Role> tenantRoles,
        IReadOnlyList<Permission> catalog
    )
    {
        var codeByPermissionId = catalog.ToDictionary(permission => permission.Id, permission => permission.Code);
        return tenantRoles
            .SelectMany(role => role.Permissions)
            .Select(rolePermission => rolePermission.PermissionId)
            .Distinct()
            .Where(codeByPermissionId.ContainsKey)
            .Select(permissionId => codeByPermissionId[permissionId])
            .ToArray();
    }

    private static UserResponse ToResponse(User user) =>
        new(user.Id, user.TenantId, user.Name, user.LastName, user.Email, user.ActorType, user.CustomerId);
}
