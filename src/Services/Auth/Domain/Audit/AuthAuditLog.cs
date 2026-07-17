using BuildingBlocks.Domain;

namespace TaxVision.Auth.Domain.Audit;

/// <summary>Registro inmutable (append-only) de eventos de seguridad del servicio Auth.</summary>
public sealed class AuthAuditLog : TenantEntity
{
    private AuthAuditLog() { }

    public Guid? UserId { get; private set; }
    public string Action { get; private set; } = default!;
    public string? TargetType { get; private set; }
    public Guid? TargetId { get; private set; }
    public string? IpAddress { get; private set; }
    public string? UserAgent { get; private set; }
    public string? CorrelationId { get; private set; }
    public string? DetailsJson { get; private set; }
    public bool Success { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }

    public static AuthAuditLog Record(
        Guid tenantId,
        Guid? userId,
        string action,
        bool success,
        string? ipAddress,
        string? userAgent,
        string? correlationId,
        string? targetType = null,
        Guid? targetId = null,
        string? detailsJson = null
    )
    {
        var log = new AuthAuditLog
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Action = action,
            Success = success,
            IpAddress = Truncate(ipAddress, 45),
            UserAgent = Truncate(userAgent, 512),
            CorrelationId = Truncate(correlationId, 128),
            TargetType = Truncate(targetType, 32),
            TargetId = targetId,
            DetailsJson = detailsJson,
            OccurredAtUtc = DateTime.UtcNow,
        };
        log.SetTenant(tenantId);
        return log;
    }

    private static string? Truncate(string? value, int maxLength) =>
        value is null ? null
        : value.Length <= maxLength ? value
        : value[..maxLength];
}

public static class AuthAuditAction
{
    public const string LoginSucceeded = "auth.login.succeeded";
    public const string LoginFailed = "auth.login.failed";
    public const string LoginLockedOut = "auth.login.locked_out";
    public const string MfaChallengeSent = "auth.mfa.challenge_sent";
    public const string MfaSucceeded = "auth.mfa.succeeded";
    public const string MfaFailed = "auth.mfa.failed";
    public const string MfaEnabled = "auth.mfa.enabled";
    public const string MfaDisabled = "auth.mfa.disabled";
    public const string RecoveryCodeUsed = "auth.mfa.recovery_code_used";
    public const string RecoveryCodesRegenerated = "auth.mfa.recovery_codes_regenerated";
    public const string TrustedDeviceAdded = "auth.mfa.trusted_device_added";
    public const string TrustedDeviceRevoked = "auth.mfa.trusted_device_revoked";
    public const string MfaPolicyUpdated = "auth.mfa.policy_updated";
    public const string TokenRefreshed = "auth.token.refreshed";
    public const string TokenReuseDetected = "auth.token.reuse_detected";
    public const string SessionRevoked = "auth.session.revoked";
    public const string AllSessionsRevoked = "auth.session.all_revoked";
    public const string PasswordChanged = "auth.password.changed";
    public const string PasswordResetRequested = "auth.password.reset_requested";
    public const string PasswordResetCompleted = "auth.password.reset_completed";
    public const string EmailChangeRequested = "auth.email.change_requested";
    public const string EmailChanged = "auth.email.changed";
    public const string PhoneVerificationRequested = "auth.phone.verification_requested";
    public const string PhoneVerified = "auth.phone.verified";
    public const string InvitationCreated = "auth.invitation.created";
    public const string InvitationAccepted = "auth.invitation.accepted";
    public const string InvitationCancelled = "auth.invitation.cancelled";
    public const string InvitationResent = "auth.invitation.resent";
    public const string UserDeactivated = "auth.user.deactivated";
    public const string UserReactivated = "auth.user.reactivated";
    public const string UserProfileUpdated = "auth.user.profile_updated";
    public const string UserRolesChanged = "auth.user.roles_changed";
    public const string RoleCreated = "auth.role.created";
    public const string RoleUpdated = "auth.role.updated";
    public const string RoleDeactivated = "auth.role.deactivated";

    // Fase A6 — ciclo de vida de dominios (TargetType="TenantDomain", TargetId=domain.Id).
    // Detalles de Cloudflare (status/sslStatus/error) van en DetailsJson: son un detalle
    // de implementación detrás del ACL, no vocabulario propio de auditoría.
    public const string TenantDomainCreated = "tenantdomain.created";
    public const string TenantDomainVerified = "tenantdomain.verified";
    public const string TenantDomainActivated = "tenantdomain.activated";
    public const string TenantDomainDisabled = "tenantdomain.disabled";
    public const string TenantDomainProvisioningFailed = "tenantdomain.provisioning_failed";
    public const string TenantSubdomainChanged = "tenantdomain.subdomain_changed";

    /// <summary>
    /// Solo se registra en falla (Host Header Injection / subdominio desconocido). Los
    /// éxitos NO se auditan uno por uno — inundarían la tabla en tráfico normal; ver
    /// TenantHostResolutionMiddleware.
    /// </summary>
    public const string TenantResolutionFailed = "tenantdomain.resolution_failed";

    /// <summary>Fase L1.4 — ver TenantTermsAcceptance/TermsAcceptanceMiddleware.</summary>
    public const string TermsAccepted = "tenant.terms_accepted";
}
