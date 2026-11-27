using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Application.Customers;
using TaxVision.Auth.Application.Customers.Commands;
using TaxVision.Auth.Domain.Users;
using Wolverine;

namespace TaxVision.Auth.Application.Credentials.Administration;

// ---------------------------------------------------------------------------
// Link de reset del portal de UN cliente, pedido por Customer (M2M, 03 §14.3). Customer ya aplicó permiso, ABAC y
// step-up; Auth vuelve a aplicar la política con quien lo pidió y la ventana de reautenticación (defensa en
// profundidad, 03 §6.5).
// ---------------------------------------------------------------------------

public sealed record SendCustomerPortalPasswordResetLinkCommand(
    Guid TenantId,
    Guid CustomerId,
    PortalCredentialCommandEnvelope Envelope,
    string CorrelationId
);

public sealed record CancelCustomerPortalPasswordResetLinkCommand(
    Guid TenantId,
    Guid CustomerId,
    PortalCredentialCommandEnvelope Envelope,
    string CorrelationId
);

public sealed record GetCustomerPortalCredentialStatusQuery(Guid TenantId, Guid CustomerId, Guid RequestedByUserId);

public sealed record RequireCustomerPortalPasswordChangeCommand(
    Guid TenantId,
    Guid CustomerId,
    PortalCredentialCommandEnvelope Envelope,
    string CorrelationId
);

public sealed record SecureCustomerPortalAccountCommand(
    Guid TenantId,
    Guid CustomerId,
    PortalCredentialCommandEnvelope Envelope,
    string CorrelationId
);

internal static class PortalCredentialAudit
{
    // La IP y el agente son los del actor que reenvía Customer, no los del servicio que llama.
    public static CredentialAdminAudit For(
        Guid tenantId,
        PortalCredentialCommandEnvelope envelope,
        string correlationId,
        IAuthAuditWriter audit
    ) =>
        new(
            audit,
            tenantId,
            envelope.RequestedByUserId,
            envelope.ActorIpAddress,
            envelope.ActorUserAgent,
            correlationId,
            null,
            UserAccountKind.Portal,
            envelope.ReauthenticatedAtUtc
        );

    /// <summary>El motivo que reenvía Customer; sin código no hay acción (Auth.CredentialAdmin.ReasonRequired).</summary>
    public static CredentialAdministrationRequest? ReasonOf(PortalCredentialCommandEnvelope envelope) =>
        envelope.ReasonCode is { } code
            ? new CredentialAdministrationRequest(code, envelope.ReasonNote, envelope.IdentityConfirmed)
            : null;

    public static async Task<Error?> RejectStaleAsync(
        CredentialAdminAudit audit,
        User target,
        string modality,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var window = ReauthenticationWindow.Validate(audit.ReauthenticatedAtUtc ?? default, DateTime.UtcNow);
        return window.IsSuccess
            ? null
            : await OfficeCredentialAdministration.DenyAsync(audit, target.Id, modality, window.Error, unitOfWork, ct);
    }
}

public static class SendCustomerPortalPasswordResetLinkHandler
{
    public static async Task<Result<ResetLinkIssuedResponse>> Handle(
        SendCustomerPortalPasswordResetLinkCommand command,
        IUserRepository users,
        ICredentialTokenRepository credentials,
        ISecureTokenService tokens,
        IAccountTokenProtector tokenProtector,
        ITenantRegistry tenants,
        IUserLoginLock userLock,
        IOptions<CredentialAdministrationOptions> options,
        IAuthAuditWriter audit,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var target = await users.GetPortalUserByCustomerAsync(command.TenantId, command.CustomerId, ct);
        if (target is null)
            return Result.Failure<ResetLinkIssuedResponse>(ResetCustomerPortalMfaHandler.NoPortalAccount);

        var trail = PortalCredentialAudit.For(command.TenantId, command.Envelope, command.CorrelationId, audit);
        if (
            await PortalCredentialAudit.RejectStaleAsync(
                trail,
                target,
                OfficeCredentialAdministration.SendModality,
                unitOfWork,
                ct
            ) is
            { } stale
        )
            return Result.Failure<ResetLinkIssuedResponse>(stale);

        return await OfficeCredentialAdministration.SendAsync(
            target.Id,
            target,
            options.Value,
            users,
            credentials,
            tokens,
            tokenProtector,
            tenants,
            userLock,
            trail,
            unitOfWork,
            bus,
            ct
        );
    }
}

public static class CancelCustomerPortalPasswordResetLinkHandler
{
    public static async Task<Result> Handle(
        CancelCustomerPortalPasswordResetLinkCommand command,
        IUserRepository users,
        ICredentialTokenRepository credentials,
        IUserLoginLock userLock,
        IOptions<CredentialAdministrationOptions> options,
        IAuthAuditWriter audit,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var target = await users.GetPortalUserByCustomerAsync(command.TenantId, command.CustomerId, ct);
        if (target is null)
            return Result.Failure(ResetCustomerPortalMfaHandler.NoPortalAccount);

        var trail = PortalCredentialAudit.For(command.TenantId, command.Envelope, command.CorrelationId, audit);
        if (
            await PortalCredentialAudit.RejectStaleAsync(
                trail,
                target,
                OfficeCredentialAdministration.CancelModality,
                unitOfWork,
                ct
            ) is
            { } stale
        )
            return Result.Failure(stale);

        return await OfficeCredentialAdministration.CancelAsync(
            target.Id,
            target,
            options.Value,
            users,
            credentials,
            userLock,
            trail,
            unitOfWork,
            ct
        );
    }
}

public static class RequireCustomerPortalPasswordChangeHandler
{
    public static async Task<Result<CredentialStateChangedResponse>> Handle(
        RequireCustomerPortalPasswordChangeCommand command,
        IUserRepository users,
        IUserLoginLock userLock,
        SessionAccessCutoff cutoff,
        IOptions<CredentialAdministrationOptions> options,
        IAuthAuditWriter audit,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var target = await users.GetPortalUserByCustomerAsync(command.TenantId, command.CustomerId, ct);
        if (target is null)
            return Result.Failure<CredentialStateChangedResponse>(ResetCustomerPortalMfaHandler.NoPortalAccount);

        var trail = PortalCredentialAudit.For(command.TenantId, command.Envelope, command.CorrelationId, audit);
        if (
            await PortalCredentialAudit.RejectStaleAsync(
                trail,
                target,
                OfficeCredentialAdministration.RequireChangeModality,
                unitOfWork,
                ct
            ) is
            { } stale
        )
            return Result.Failure<CredentialStateChangedResponse>(stale);
        if (PortalCredentialAudit.ReasonOf(command.Envelope) is not { } request)
            return Result.Failure<CredentialStateChangedResponse>(CredentialAdministrationErrors.ReasonRequired);

        return await OfficeCredentialAdministration.RequireChangeAsync(
            target.Id,
            target,
            request,
            options.Value,
            users,
            userLock,
            cutoff,
            trail,
            unitOfWork,
            bus,
            ct
        );
    }
}

public static class SecureCustomerPortalAccountHandler
{
    public static async Task<Result<AccountSecuredResponse>> Handle(
        SecureCustomerPortalAccountCommand command,
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
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var target = await users.GetPortalUserByCustomerAsync(command.TenantId, command.CustomerId, ct);
        if (target is null)
            return Result.Failure<AccountSecuredResponse>(ResetCustomerPortalMfaHandler.NoPortalAccount);

        var trail = PortalCredentialAudit.For(command.TenantId, command.Envelope, command.CorrelationId, audit);
        if (
            await PortalCredentialAudit.RejectStaleAsync(
                trail,
                target,
                OfficeCredentialAdministration.SecureModality,
                unitOfWork,
                ct
            ) is
            { } stale
        )
            return Result.Failure<AccountSecuredResponse>(stale);
        if (PortalCredentialAudit.ReasonOf(command.Envelope) is not { } request)
            return Result.Failure<AccountSecuredResponse>(CredentialAdministrationErrors.ReasonRequired);

        return await OfficeCredentialAdministration.SecureAsync(
            target.Id,
            target,
            request,
            options.Value,
            users,
            credentials,
            tokens,
            tokenProtector,
            tenants,
            userLock,
            cutoff,
            mfa,
            trail,
            unitOfWork,
            bus,
            ct
        );
    }
}

public static class GetCustomerPortalCredentialStatusHandler
{
    public static async Task<Result<PortalCredentialStatusResponse>> Handle(
        GetCustomerPortalCredentialStatusQuery query,
        IUserRepository users,
        ICredentialTokenRepository credentials,
        IOptions<CredentialAdministrationOptions> options,
        INotificationDeliveryClient deliveries,
        CancellationToken ct
    )
    {
        var target = await users.GetPortalUserByCustomerAsync(query.TenantId, query.CustomerId, ct);
        if (target is null)
            return Result.Success(
                new PortalCredentialStatusResponse(
                    query.CustomerId,
                    false,
                    CredentialStatus.Active,
                    null,
                    null,
                    null,
                    false,
                    false,
                    null,
                    new StaffCredentialActions(false, false, false, false),
                    OfficeCredentialAdministration.LinkLifetimeHours(options.Value)
                )
            );

        var snapshot = await OfficeCredentialAdministration.ReadAsync(
            query.TenantId,
            query.RequestedByUserId,
            target,
            UserAccountKind.Portal,
            options.Value,
            users,
            credentials,
            deliveries,
            ct
        );
        if (snapshot.IsFailure)
            return Result.Failure<PortalCredentialStatusResponse>(snapshot.Error);

        var (_, latest, actions, cooldownUntil) = snapshot.Value;
        return Result.Success(
            new PortalCredentialStatusResponse(
                query.CustomerId,
                true,
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
