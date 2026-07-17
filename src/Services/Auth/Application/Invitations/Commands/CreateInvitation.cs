using System.Net.Mail;
using System.Text.Json;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.Invitations;
using TaxVision.Auth.Domain.Users;
using Wolverine;

namespace TaxVision.Auth.Application.Invitations.Commands;

/// <summary>Opciones de invitaciones. En producción ReturnRawToken debe ser false.</summary>
public sealed class InvitationOptions
{
    public const string SectionName = "Invitations";

    /// <summary>Solo para desarrollo: incluye el token en claro en la respuesta HTTP.</summary>
    public bool ReturnRawToken { get; set; }

    public int ValidityDays { get; set; } = 7;
}

public sealed record CreateInvitationCommand(
    Guid InvitedByUserId,
    Guid TenantId,
    string Email,
    UserActorType ActorType,
    Guid? CustomerId,
    IReadOnlyList<Guid>? RoleIds = null
);

public sealed record CreateInvitationResponse(
    Guid InvitationId,
    Guid TenantId,
    string Email,
    UserActorType ActorType,
    Guid? CustomerId,
    string? InvitationToken,
    DateTime ExpiresAtUtc
);

public static class CreateInvitationHandler
{
    public static async Task<Result<CreateInvitationResponse>> Handle(
        CreateInvitationCommand command,
        IUserRepository users,
        ITenantRegistry tenants,
        IInvitationRepository invitations,
        IInvitationTokenService tokens,
        IRoleRepository roles,
        ITenantPlanLimitsStore planLimits,
        IAuthAuditWriter audit,
        IRequestContext request,
        ICorrelationContext correlation,
        IOptions<InvitationOptions> options,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var inviter = await users.GetByIdAsync(command.InvitedByUserId, ct);
        if (inviter is null || !inviter.IsActive)
        {
            return Result.Failure<CreateInvitationResponse>(
                new Error("Invitation.Forbidden", "Inviter is not authorized.")
            );
        }

        if (!CanInvite(inviter, command))
        {
            return Result.Failure<CreateInvitationResponse>(
                new Error("Invitation.Forbidden", "This actor cannot create the requested invitation.")
            );
        }

        var tenant = await tenants.GetByIdAsync(command.TenantId, ct);
        if (tenant is null || !tenant.IsActive)
        {
            return Result.Failure<CreateInvitationResponse>(
                new Error("Tenant.Inactive", "Target tenant does not exist or is inactive.")
            );
        }

        var normalizedEmail = command.Email?.Trim().ToLowerInvariant() ?? string.Empty;
        if (
            !MailAddress.TryCreate(normalizedEmail, out var parsedEmail)
            || !string.Equals(parsedEmail.Address, normalizedEmail, StringComparison.OrdinalIgnoreCase)
        )
        {
            return Result.Failure<CreateInvitationResponse>(
                new Error("Invitation.Email", "Invitation email is invalid.")
            );
        }

        if (await users.EmailExistsAsync(command.TenantId, normalizedEmail, ct))
        {
            return Result.Failure<CreateInvitationResponse>(
                new Error("User.EmailConflict", "Email is already registered in this tenant.")
            );
        }

        if (await invitations.HasPendingAsync(command.TenantId, normalizedEmail, ct))
        {
            return Result.Failure<CreateInvitationResponse>(
                new Error(
                    "Invitation.PendingConflict",
                    "A pending invitation already exists for this email and tenant."
                )
            );
        }

        // Límites del plan: los usuarios del portal cliente no consumen asientos.
        if (command.ActorType != UserActorType.CustomerPortal)
        {
            var seatResult = await PlanGuard.EnsureSeatAvailableAsync(
                command.TenantId,
                planLimits,
                users,
                invitations,
                ct
            );
            if (seatResult.IsFailure)
                return Result.Failure<CreateInvitationResponse>(seatResult.Error);
        }

        // Roles a asignar al aceptar (deben existir en el tenant y estar activos).
        string? roleIdsJson = null;
        if (command.RoleIds is { Count: > 0 })
        {
            var requestedIds = command.RoleIds.Distinct().ToList();
            var tenantRoles = await roles.GetByIdsAsync(command.TenantId, requestedIds, ct);
            if (tenantRoles.Count != requestedIds.Count || tenantRoles.Any(role => !role.IsActive))
            {
                return Result.Failure<CreateInvitationResponse>(
                    new Error("Role.NotFound", "One or more roles do not exist in this tenant.")
                );
            }

            // Fase A1: si se invita a un Tenant Customer con roles explícitos, esos roles
            // no pueden colar permisos internos (ver CustomerPortalRoleGuard). El flujo de
            // invitación disparado por Customer (CustomerPortalInvitationRequestedConsumer)
            // nunca pasa RoleIds, así que cae siempre en el rol de sistema portal-safe.
            if (command.ActorType == UserActorType.CustomerPortal)
            {
                var catalog = await roles.GetPermissionsCatalogAsync(ct);
                var portalGuard = CustomerPortalRoleGuard.ValidateRolesForCustomerPortal(tenantRoles, catalog);
                if (portalGuard.IsFailure)
                    return Result.Failure<CreateInvitationResponse>(portalGuard.Error);
            }

            roleIdsJson = JsonSerializer.Serialize(requestedIds);
        }

        var token = tokens.Generate();
        var expiresAtUtc = DateTime.UtcNow.AddDays(options.Value.ValidityDays);
        var result = Invitation.Create(
            command.TenantId,
            normalizedEmail,
            command.ActorType,
            command.CustomerId,
            inviter.Id,
            token.TokenHash,
            expiresAtUtc,
            roleIdsJson
        );
        if (result.IsFailure)
            return Result.Failure<CreateInvitationResponse>(result.Error);

        result.Value.MarkSent();
        await invitations.AddAsync(result.Value, ct);

        await bus.PublishAsync(
            new InvitationCreatedIntegrationEvent
            {
                TenantId = command.TenantId,
                InvitationId = result.Value.Id,
                Email = normalizedEmail,
                ActorType = command.ActorType.ToString(),
                RawToken = token.RawToken,
                ExpiresAtUtc = expiresAtUtc,
                TenantName = tenant.Name,
                InviterName = $"{inviter.Name} {inviter.LastName}",
                CorrelationId = correlation.CorrelationId,
            }
        );

        await audit.AddAsync(
            AuthAuditLog.Record(
                command.TenantId,
                inviter.Id,
                AuthAuditAction.InvitationCreated,
                true,
                request.IpAddress,
                request.UserAgent,
                correlation.CorrelationId,
                targetType: "Invitation",
                targetId: result.Value.Id
            ),
            ct
        );
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(
            new CreateInvitationResponse(
                result.Value.Id,
                result.Value.TenantId,
                result.Value.Email,
                result.Value.ActorType,
                result.Value.CustomerId,
                options.Value.ReturnRawToken ? token.RawToken : null,
                result.Value.ExpiresAtUtc
            )
        );
    }

    private static bool CanInvite(User inviter, CreateInvitationCommand command) =>
        inviter.ActorType switch
        {
            UserActorType.PlatformAdmin => command.ActorType == UserActorType.PlatformAdmin
                ? command.TenantId == PlatformTenant.Id
                : command.ActorType == UserActorType.TenantAdmin && command.TenantId != PlatformTenant.Id,

            UserActorType.TenantAdmin => inviter.TenantId == command.TenantId
                && command.TenantId != PlatformTenant.Id
                && command.ActorType
                    is UserActorType.TenantAdmin
                        or UserActorType.TenantEmployee
                        or UserActorType.CustomerPortal,

            _ => false,
        };
}
