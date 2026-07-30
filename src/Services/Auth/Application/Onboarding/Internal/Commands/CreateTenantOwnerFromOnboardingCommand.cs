using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Application.Onboarding.Abstractions;
using TaxVision.Auth.Domain.Roles;
using TaxVision.Auth.Domain.Users;
using Wolverine;

namespace TaxVision.Auth.Application.Onboarding.Internal.Commands;

public sealed record CreateTenantOwnerFromOnboardingCommand(
    Guid OnboardingId,
    Guid TenantId,
    string Email,
    string FirstName,
    string LastName,
    Guid PasswordHashReference
);

/// <summary>
/// PayFlow (Fase 16) — receptor del <c>POST auth/internal/tenants/{tenantId}/owners</c> que la Saga
/// (Fase 15, <c>Sagas/Commands/CreateTenantOwnerCommand.cs</c>) invoca vía
/// <c>IAuthInternalOwnerCreationClient</c>. Nombre distinto a propósito del comando local de la Saga
/// (mismo nombre en el plan original, <c>CreateTenantOwnerCommand</c>, en ambos lados habría sido
/// confuso — dos tipos con el mismo nombre en namespaces distintos, uno disparando el HTTP y este
/// otro ejecutando el efecto real).
/// <para>
/// El password NUNCA cruza este comando en texto plano ni siquiera hasheado por HTTP directo:
/// <see cref="PasswordHashReference"/> apunta a una entrada Redis de un solo uso
/// (<see cref="ITokenReferenceStore"/>, GETDEL) que ya contiene el hash PBKDF2 calculado por
/// <c>CompleteOnboardingRegistrationHandler</c> (Fase 13) — este handler solo la canjea.
/// </para>
/// </summary>
public static class CreateTenantOwnerFromOnboardingHandler
{
    public static async Task<Result> Handle(
        CreateTenantOwnerFromOnboardingCommand command,
        IUserRepository users,
        IRoleRepository roles,
        ITokenReferenceStore passwordHashReferences,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var existing = await users.GetByOnboardingIdAsync(command.OnboardingId, ct);
        if (existing is not null)
            return Result.Success();

        var passwordHash = await passwordHashReferences.ConsumeAsync(command.PasswordHashReference, ct);
        if (string.IsNullOrWhiteSpace(passwordHash))
            return Result.Failure(
                new Error("Onboarding.PasswordReferenceExpired", "The password reference has expired.")
            );

        var userResult = User.Register(
            command.TenantId,
            command.FirstName,
            command.LastName,
            command.Email,
            passwordHash,
            UserActorType.TenantAdmin,
            onboardingId: command.OnboardingId
        );
        if (userResult.IsFailure)
            return Result.Failure(userResult.Error);

        var user = userResult.Value;
        user.VerifyEmail();

        await users.AddAsync(user, ct);

        var systemRole = await roles.GetSystemRoleAsync(command.TenantId, Role.SystemTenantAdmin, ct);
        if (systemRole is not null)
        {
            await roles.ReplaceUserRolesAsync(user.Id, [systemRole.Id], assignedByUserId: null, ct);
            user.BumpPermissionsVersion();

            var tenantRoles = await roles.GetByIdsAsync(command.TenantId, [systemRole.Id], ct);
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
                    ActorType = user.ActorType.ToString(),
                    CorrelationId = correlation.CorrelationId,
                }
            );

            user.MarkPermissionsBackfilled(DateTime.UtcNow);
        }

        await bus.PublishAsync(
            new TenantOwnerCreatedIntegrationEvent
            {
                TenantId = command.TenantId,
                OnboardingId = command.OnboardingId,
                CreatedUserId = user.Id,
                CorrelationId = correlation.CorrelationId,
            }
        );

        // Bug real encontrado auditando UserDirectoryEntry en Communication: este handler es el
        // "gemelo" de AcceptInvitationHandler (ver comentario de clase arriba) para el owner de
        // PayFlow, pero al escribirlo se copió el bloque de UserRolesChanged y se omitió este —
        // el único evento que alimenta UserDirectoryEntry (displayName/email/actorType) en
        // Communication, TenantEmployeeDirectoryEntry en Customer, y el consumer de bienvenida
        // en Notification. Sin él, TODO TenantAdmin dado de alta vía onboarding pay-first quedaba
        // invisible en los tres — nunca podía ser resuelto por nombre en chat/calls, ni asignado
        // como preparador de un cliente, ni recibía el email de bienvenida. Mismo patrón que
        // AcceptInvitation.cs: publicar ANTES de SaveChangesAsync para que el outbox de Wolverine
        // agrupe el mensaje con el commit del UnitOfWork.
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

        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }

    // Mismo cálculo que AcceptInvitationHandler.ResolveEffectivePermissionCodes — duplicado a
    // propósito, ver el comentario original ahí.
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
}
