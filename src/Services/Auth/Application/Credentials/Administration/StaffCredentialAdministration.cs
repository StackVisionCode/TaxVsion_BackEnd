using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Domain.Credentials;
using TaxVision.Auth.Domain.Users;
using Wolverine;

namespace TaxVision.Auth.Application.Credentials.Administration;

// ---------------------------------------------------------------------------
// Link de reset de un empleado enviado, cancelado y consultado por la oficina (03 §3 filas 1–1c, §14.1).
// El tenant y el actor salen del JWT; la política decide quién (D-01: solo el Owner).
// ---------------------------------------------------------------------------

public sealed record SendStaffPasswordResetLinkCommand(
    Guid TenantId,
    Guid ActorUserId,
    Guid TargetUserId,
    DateTime? ReauthenticatedAtUtc = null
);

public sealed record CancelStaffPasswordResetLinkCommand(
    Guid TenantId,
    Guid ActorUserId,
    Guid TargetUserId,
    DateTime? ReauthenticatedAtUtc = null
);

public sealed record GetStaffCredentialStatusQuery(Guid TenantId, Guid ActorUserId, Guid TargetUserId);

/// <summary>Cambio obligatorio de un empleado (03 §3 fila 2). El motivo viaja con su nota, que no se audita.</summary>
public sealed record RequireStaffPasswordChangeCommand(
    Guid TenantId,
    Guid ActorUserId,
    Guid TargetUserId,
    CredentialAdministrationReasonCode ReasonCode,
    string? ReasonNote,
    bool? IdentityConfirmed = null,
    DateTime? ReauthenticatedAtUtc = null
)
{
    public CredentialAdministrationRequest Request => new(ReasonCode, ReasonNote, IdentityConfirmed);
}

/// <summary>Cuenta asegurada de un empleado (03 §3 fila 3); repetido reenvía el link seguro (AJ-15).</summary>
public sealed record SecureStaffAccountCommand(
    Guid TenantId,
    Guid ActorUserId,
    Guid TargetUserId,
    CredentialAdministrationReasonCode ReasonCode,
    string? ReasonNote,
    bool? IdentityConfirmed = null,
    DateTime? ReauthenticatedAtUtc = null
)
{
    public CredentialAdministrationRequest Request => new(ReasonCode, ReasonNote, IdentityConfirmed);
}

/// <summary>Reset de MFA de un empleado (03 §3 fila 4). Motivo obligatorio; <see cref="IdentityConfirmed"/> se audita.</summary>
public sealed record ResetStaffMfaCommand(
    Guid TenantId,
    Guid ActorUserId,
    Guid TargetUserId,
    CredentialAdministrationReasonCode ReasonCode,
    string? ReasonNote,
    bool? IdentityConfirmed = null,
    DateTime? ReauthenticatedAtUtc = null
)
{
    public CredentialAdministrationRequest Request => new(ReasonCode, ReasonNote, IdentityConfirmed);
}

internal static class StaffCredentialAudit
{
    public static CredentialAdminAudit For(
        Guid tenantId,
        Guid actorUserId,
        DateTime? reauthenticatedAtUtc,
        IAuthAuditWriter audit,
        IRequestContext request,
        ICorrelationContext correlation
    ) =>
        new(
            audit,
            tenantId,
            actorUserId,
            request.IpAddress,
            request.UserAgent,
            correlation.CorrelationId,
            request.ActorType,
            UserAccountKind.Staff,
            reauthenticatedAtUtc
        );
}

public static class SendStaffPasswordResetLinkHandler
{
    public static async Task<Result<ResetLinkIssuedResponse>> Handle(
        SendStaffPasswordResetLinkCommand command,
        IUserRepository users,
        ICredentialTokenRepository credentials,
        ISecureTokenService tokens,
        IAccountTokenProtector tokenProtector,
        ITenantRegistry tenants,
        IUserLoginLock userLock,
        IOptions<CredentialAdministrationOptions> options,
        IAuthAuditWriter audit,
        IRequestContext request,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    ) =>
        await OfficeCredentialAdministration.SendAsync(
            command.TargetUserId,
            await users.GetByIdAsync(command.TargetUserId, ct),
            options.Value,
            users,
            credentials,
            tokens,
            tokenProtector,
            tenants,
            userLock,
            StaffCredentialAudit.For(
                command.TenantId,
                command.ActorUserId,
                command.ReauthenticatedAtUtc,
                audit,
                request,
                correlation
            ),
            unitOfWork,
            bus,
            ct
        );
}

public static class CancelStaffPasswordResetLinkHandler
{
    public static async Task<Result> Handle(
        CancelStaffPasswordResetLinkCommand command,
        IUserRepository users,
        ICredentialTokenRepository credentials,
        IUserLoginLock userLock,
        IOptions<CredentialAdministrationOptions> options,
        IAuthAuditWriter audit,
        IRequestContext request,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    ) =>
        await OfficeCredentialAdministration.CancelAsync(
            command.TargetUserId,
            await users.GetByIdAsync(command.TargetUserId, ct),
            options.Value,
            users,
            credentials,
            userLock,
            StaffCredentialAudit.For(
                command.TenantId,
                command.ActorUserId,
                command.ReauthenticatedAtUtc,
                audit,
                request,
                correlation
            ),
            unitOfWork,
            ct
        );
}

public static class RequireStaffPasswordChangeHandler
{
    public static async Task<Result<CredentialStateChangedResponse>> Handle(
        RequireStaffPasswordChangeCommand command,
        IUserRepository users,
        IUserLoginLock userLock,
        SessionAccessCutoff cutoff,
        IOptions<CredentialAdministrationOptions> options,
        IAuthAuditWriter audit,
        IRequestContext request,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    ) =>
        await OfficeCredentialAdministration.RequireChangeAsync(
            command.TargetUserId,
            await users.GetByIdAsync(command.TargetUserId, ct),
            command.Request,
            options.Value,
            users,
            userLock,
            cutoff,
            StaffCredentialAudit.For(
                command.TenantId,
                command.ActorUserId,
                command.ReauthenticatedAtUtc,
                audit,
                request,
                correlation
            ),
            unitOfWork,
            bus,
            ct
        );
}

public static class SecureStaffAccountHandler
{
    public static async Task<Result<AccountSecuredResponse>> Handle(
        SecureStaffAccountCommand command,
        IUserRepository users,
        ICredentialTokenRepository credentials,
        ISecureTokenService tokens,
        IAccountTokenProtector tokenProtector,
        ITenantRegistry tenants,
        IUserLoginLock userLock,
        SessionAccessCutoff cutoff,
        IMfaRepository mfa,
        IOptions<CredentialAdministrationOptions> options,
        IAuthAuditWriter audit,
        IRequestContext request,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    ) =>
        await OfficeCredentialAdministration.SecureAsync(
            command.TargetUserId,
            await users.GetByIdAsync(command.TargetUserId, ct),
            command.Request,
            options.Value,
            users,
            credentials,
            tokens,
            tokenProtector,
            tenants,
            userLock,
            cutoff,
            mfa,
            StaffCredentialAudit.For(
                command.TenantId,
                command.ActorUserId,
                command.ReauthenticatedAtUtc,
                audit,
                request,
                correlation
            ),
            unitOfWork,
            bus,
            ct
        );
}

public static class ResetStaffMfaHandler
{
    public static async Task<Result<StaffMfaResetResponse>> Handle(
        ResetStaffMfaCommand command,
        IUserRepository users,
        IUserLoginLock userLock,
        SessionAccessCutoff cutoff,
        IMfaRepository mfa,
        IOptions<CredentialAdministrationOptions> options,
        IAuthAuditWriter audit,
        IRequestContext request,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    ) =>
        await OfficeCredentialAdministration.ResetMfaAsync(
            command.TargetUserId,
            await users.GetByIdAsync(command.TargetUserId, ct),
            command.Request,
            options.Value,
            users,
            userLock,
            cutoff,
            mfa,
            StaffCredentialAudit.For(
                command.TenantId,
                command.ActorUserId,
                command.ReauthenticatedAtUtc,
                audit,
                request,
                correlation
            ),
            unitOfWork,
            bus,
            ct
        );
}

public static class GetStaffCredentialStatusHandler
{
    public static async Task<Result<StaffCredentialStatusResponse>> Handle(
        GetStaffCredentialStatusQuery query,
        IUserRepository users,
        ICredentialTokenRepository credentials,
        IOptions<CredentialAdministrationOptions> options,
        INotificationDeliveryClient deliveries,
        CancellationToken ct
    )
    {
        var snapshot = await OfficeCredentialAdministration.ReadAsync(
            query.TenantId,
            query.ActorUserId,
            await users.GetByIdAsync(query.TargetUserId, ct),
            UserAccountKind.Staff,
            options.Value,
            users,
            credentials,
            deliveries,
            ct
        );
        if (snapshot.IsFailure)
            return Result.Failure<StaffCredentialStatusResponse>(snapshot.Error);

        var (target, latest, actions, cooldownUntil) = snapshot.Value;
        return Result.Success(
            new StaffCredentialStatusResponse(
                target.Id,
                target.CredentialStatus,
                target.CredentialStateReasonCode,
                target.CredentialStateChangedAtUtc,
                target.PasswordChangedAtUtc,
                target.MfaEnabled,
                target.EmailVerified,
                latest,
                actions,
                OfficeCredentialAdministration.LinkLifetimeHours(options.Value),
                cooldownUntil
            )
        );
    }
}
