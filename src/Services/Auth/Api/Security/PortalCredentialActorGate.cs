using System.Security.Claims;
using System.Text.Json;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Api.Security;

/// <summary>
/// Puerta de los M2M del portal que Customer pide para la oficina (credenciales y MFA por SR-03; sesiones e
/// invitación por SR-11): sin el token del actor válido no se llega al handler. Cada rechazo queda como
/// <c>auth.credential_admin.denied</c> con el código y el actor verificado (nunca el solicitante declarado).
/// </summary>
public sealed class PortalCredentialActorGate(
    ForwardedActorTokenValidator validator,
    IUserRepository users,
    IAuthAuditWriter audit,
    IUnitOfWork unitOfWork,
    ICorrelationContext correlation
)
{
    public const string ReadStatusModality = "read_status";
    public const string ListSessionsModality = "list_sessions";
    public const string RevokeSessionModality = "revoke_session";
    public const string RevokeAllSessionsModality = "revoke_all_sessions";
    public const string IssueInvitationModality = "issue_invitation";
    private const string ServiceActorType = "Service";
    private const string ClientIdClaim = "client_id";

    public Task<Result<ForwardedActor>> CheckAsync(
        HttpRequest request,
        Guid tenantId,
        Guid customerId,
        Guid requestedByUserId,
        string requiredPermission,
        string modality,
        bool requireReauthentication,
        string? actorIpAddress,
        string? actorUserAgent,
        CancellationToken ct
    ) =>
        CheckAsync(
            request,
            tenantId,
            customerId,
            requestedByUserId,
            new ForwardedActorRequirement([ActorType.TenantAdmin], requiredPermission, requireReauthentication),
            modality,
            actorIpAddress,
            actorUserAgent,
            ct
        );

    public async Task<Result<ForwardedActor>> CheckAsync(
        HttpRequest request,
        Guid tenantId,
        Guid customerId,
        Guid? requestedByUserId,
        ForwardedActorRequirement requirement,
        string modality,
        string? actorIpAddress,
        string? actorUserAgent,
        CancellationToken ct
    )
    {
        var validation = await validator.ValidateAsync(
            request.Headers[ForwardedActorToken.HeaderName].ToString(),
            tenantId,
            requestedByUserId,
            requirement,
            ct
        );
        if (validation.Outcome.IsSuccess)
            return validation.Outcome;

        var target = await users.GetPortalUserByCustomerAsync(tenantId, customerId, ct);
        var (actorUserId, actorType, serviceClientId) = AuditActor(validation, request.HttpContext.User);
        await audit.AddAsync(
            AuthAuditLog.Record(
                tenantId,
                actorUserId,
                AuthAuditAction.CredentialAdminDenied,
                false,
                actorIpAddress,
                actorUserAgent,
                correlation.CorrelationId,
                "User",
                target?.Id,
                JsonSerializer.Serialize(
                    new
                    {
                        accountKind = UserAccountKind.Portal.ToString(),
                        modality,
                        code = validation.Outcome.Error.Code,
                        claimedRequestedByUserId = requestedByUserId,
                        serviceClientId,
                    }
                ),
                actorType
            ),
            ct
        );
        await unitOfWork.SaveChangesAsync(ct);
        return validation.Outcome;
    }

    // OBS-04: el actor es el sub del token reenviado con firma válida; si no la hay, el servicio que llamó.
    // El requestedByUserId del pedido no está verificado y solo va en los detalles.
    private static (Guid? UserId, string? ActorType, string? ServiceClientId) AuditActor(
        ForwardedActorValidation validation,
        ClaimsPrincipal caller
    )
    {
        if (validation.SignedUserId is { } signedUserId)
            return (signedUserId, validation.SignedActorType, null);
        if (caller.FindFirst(ClaimNames.ActorType)?.Value == ServiceActorType && caller.TryGetUserId(out var serviceId))
            return (serviceId, ServiceActorType, caller.FindFirst(ClientIdClaim)?.Value);
        return (null, null, null);
    }

    /// <summary>Permiso adicional del actor ya validado (p. ej. ver la ubicación de las sesiones).</summary>
    public Task<bool> HasPermissionAsync(ForwardedActor actor, string permission, CancellationToken ct) =>
        validator.HasPermissionAsync(actor, permission, ct);
}
