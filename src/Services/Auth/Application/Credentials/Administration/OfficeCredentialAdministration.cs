using System.Text.Json;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Application.Mfa;
using TaxVision.Auth.Application.Sessions;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.Credentials;
using TaxVision.Auth.Domain.Users;
using Wolverine;

namespace TaxVision.Auth.Application.Credentials.Administration;

/// <summary>
/// Bitácora de una acción de la oficina (03 §13.2): el actor en UserId y el afectado en TargetId; IP y agente reales
/// (en M2M, los que reenvía Customer). Nunca token, link, correo ni la nota libre del motivo.
/// </summary>
public sealed record CredentialAdminAudit(
    IAuthAuditWriter Writer,
    Guid TenantId,
    Guid ActorUserId,
    string? IpAddress,
    string? UserAgent,
    string CorrelationId,
    string? ActorType,
    UserAccountKind AccountKind,
    DateTime? ReauthenticatedAtUtc = null
)
{
    public Task RecordAsync(
        string action,
        bool success,
        Guid? targetId,
        string modality,
        CancellationToken ct,
        string? outcome = null,
        string? code = null,
        Guid? resetTokenId = null,
        Guid? notificationEventId = null,
        string? reasonCode = null,
        bool? identityConfirmed = null
    ) =>
        Writer.AddAsync(
            AuthAuditLog.Record(
                TenantId,
                ActorUserId,
                action,
                success,
                IpAddress,
                UserAgent,
                CorrelationId,
                "User",
                targetId,
                JsonSerializer.Serialize(
                    new
                    {
                        accountKind = AccountKind.ToString(),
                        modality,
                        outcome,
                        code,
                        resetTokenId,
                        notificationEventId,
                        reauthenticatedAtUtc = ReauthenticatedAtUtc,
                        reasonCode,
                        identityConfirmed,
                    }
                ),
                ActorType
            ),
            ct
        );
}

/// <summary>Estado de la credencial que ve la oficina: último link y botones permitidos.</summary>
public sealed record OfficeCredentialSnapshot(
    User Target,
    ResetLinkSummary? LatestResetLink,
    StaffCredentialActions Actions,
    DateTime? EmailChangeCooldownUntilUtc = null
);

/// <summary>
/// Núcleo común de staff (Auth) y portal (M2M desde Customer): política <see cref="CredentialAdministrationPolicy"/>
/// con actor y objetivo cargados de BD, emisión, cancelación y lectura. Toda denegación de la política o del tope por
/// objetivo queda como <c>auth.credential_admin.denied</c>. Guarda antes de publicar.
/// </summary>
public static class OfficeCredentialAdministration
{
    public const string SendModality = "send_reset_link";
    public const string CancelModality = "cancel_reset_link";
    public const string RequireChangeModality = "require_change";
    public const string SecureModality = "secure_account";
    public const string ResetMfaModality = "reset_mfa";

    /// <summary>Horas de vida de un link administrativo nuevo, con el tope de 24 h (D-03).</summary>
    public static int LinkLifetimeHours(CredentialAdministrationOptions options) =>
        (int)Math.Min(options.AdministrativeLinkLifetime.TotalHours, 24);

    public static async Task<Result<User>> AuthorizeAsync(
        Guid tenantId,
        Guid actorUserId,
        User? target,
        CredentialAdministrationAction action,
        UserAccountKind kind,
        bool ownerOnly,
        IUserRepository users,
        CancellationToken ct
    )
    {
        // El tenant sale del claim (o del token de servicio), nunca del body (I-7).
        if (target is null || target.TenantId != tenantId)
            return Result.Failure<User>(CredentialAdministrationErrors.NotFound);
        var actor = await users.GetByIdAsync(actorUserId, ct);
        if (actor is null)
            return Result.Failure<User>(CredentialAdministrationErrors.ActorNotAllowed);
        var allowed = CredentialAdministrationPolicy.CanActOn(actor, target, action, kind, ownerOnly);
        return allowed.IsFailure ? Result.Failure<User>(allowed.Error) : Result.Success(actor);
    }

    /// <summary>
    /// D-08 y SR-04: hasta cuándo sigue pausado el reset administrativo (link y "secure") tras un cambio de correo
    /// pedido por la oficina y confirmado, para clientes y empleados; null si no hay pausa.
    /// </summary>
    public static DateTime? EmailChangeCooldownUntil(
        User target,
        CredentialAdministrationOptions options,
        DateTime utcNow
    ) =>
        target.EmailChangedByOfficeAtUtc is { } changedAt && changedAt + options.EmailChangeCooldown > utcNow
            ? changedAt + options.EmailChangeCooldown
            : null;

    // El 409 lleva cuánto falta (retryAfterSeconds) para que el CRM muestre la fecha.
    private static async Task<Error?> RejectCooldownAsync(
        CredentialAdminAudit audit,
        User target,
        string modality,
        CredentialAdministrationOptions options,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var now = DateTime.UtcNow;
        if (EmailChangeCooldownUntil(target, options, now) is not { } until)
            return null;
        return await DenyAsync(
            audit,
            target.Id,
            modality,
            CredentialAdministrationErrors.EmailChangeCooldown.WithRetryAfter(until - now),
            unitOfWork,
            ct
        );
    }

    public static async Task<Result<ResetLinkIssuedResponse>> SendAsync(
        Guid targetId,
        User? target,
        CredentialAdministrationOptions options,
        IUserRepository users,
        ICredentialTokenRepository credentials,
        ISecureTokenService tokens,
        IAccountTokenProtector tokenProtector,
        ITenantRegistry tenants,
        IUserLoginLock userLock,
        CredentialAdminAudit audit,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var authorized = await AuthorizeAsync(
            audit.TenantId,
            audit.ActorUserId,
            target,
            CredentialAdministrationAction.SendResetLink,
            audit.AccountKind,
            options.OwnerOnly,
            users,
            ct
        );
        if (authorized.IsFailure)
            return Result.Failure<ResetLinkIssuedResponse>(
                await DenyAsync(audit, targetId, SendModality, authorized.Error, unitOfWork, ct)
            );
        // 03 §14.1: con la cuenta asegurada el link normal no sirve; se reenvía con "secure" (AJ-15).
        if (target!.CredentialStatus == CredentialStatus.ResetRequired)
            return Result.Failure<ResetLinkIssuedResponse>(CredentialAdministrationErrors.InvalidTransition);
        if (await RejectCooldownAsync(audit, target, SendModality, options, unitOfWork, ct) is { } cooldown)
            return Result.Failure<ResetLinkIssuedResponse>(cooldown);

        var issued = await PasswordResetIssuer.IssueByOfficeAsync(
            target!,
            ResetOrigin.Administrative,
            audit.ActorUserId,
            options,
            credentials,
            tokens,
            tokenProtector,
            tenants,
            userLock,
            audit.IpAddress,
            audit.CorrelationId,
            ct
        );
        if (issued.IsFailure)
        {
            var error =
                issued.Error.Code == CredentialAdministrationErrors.TargetRateLimited.Code
                    ? await DenyAsync(audit, targetId, SendModality, issued.Error, unitOfWork, ct)
                    : issued.Error;
            return Result.Failure<ResetLinkIssuedResponse>(error);
        }

        var issue = issued.Value;
        var response = new ResetLinkIssuedResponse(issue.Outcome, issue.Token.Id, issue.Token.ExpiresAtUtc);
        // Pedido repetido < 60 s: se devuelve el pendiente, sin otro correo ni otra fila.
        if (issue.Event is null)
            return Result.Success(response);

        await audit.RecordAsync(
            AuthAuditAction.PasswordResetSentByOffice,
            true,
            targetId,
            SendModality,
            ct,
            outcome: issue.Outcome.ToString(),
            resetTokenId: issue.Token.Id,
            notificationEventId: issue.Token.NotificationEventId
        );
        await unitOfWork.SaveChangesAsync(ct);
        await bus.PublishAsync(issue.Event);
        await bus.PublishAsync(
            new SecurityAlertIntegrationEvent
            {
                TenantId = target!.TenantId,
                UserId = target.Id,
                AlertType = SecurityAlertType.PasswordResetSentByOffice,
                DetailsJson = JsonSerializer.Serialize(new { origin = "office", actorUserId = audit.ActorUserId }),
                CorrelationId = audit.CorrelationId,
            }
        );
        return Result.Success(response);
    }

    /// <summary>Anula el link pendiente sin avisar al afectado (Q-04). Si ya se usó: <c>LinkAlreadyUsed</c>.</summary>
    public static async Task<Result> CancelAsync(
        Guid targetId,
        User? target,
        CredentialAdministrationOptions options,
        IUserRepository users,
        ICredentialTokenRepository credentials,
        IUserLoginLock userLock,
        CredentialAdminAudit audit,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var authorized = await AuthorizeAsync(
            audit.TenantId,
            audit.ActorUserId,
            target,
            CredentialAdministrationAction.CancelResetLink,
            audit.AccountKind,
            options.OwnerOnly,
            users,
            ct
        );
        if (authorized.IsFailure)
            return Result.Failure(await DenyAsync(audit, targetId, CancelModality, authorized.Error, unitOfWork, ct));

        await userLock.AcquireAsync(target!, ct);
        var now = DateTime.UtcNow;
        var pending = await credentials.GetPendingPasswordResetsAsync(target!.Id, now, ct);
        if (pending.Count == 0)
        {
            var latest = await credentials.GetLatestPasswordResetAsync(target.Id, ct);
            return Result.Failure(
                latest?.UsedAtUtc is not null
                    ? CredentialAdministrationErrors.LinkAlreadyUsed
                    : CredentialAdministrationErrors.NoPendingLink
            );
        }

        foreach (var link in pending)
            link.Revoke(now, ResetRevocationReason.CancelledByOffice, audit.ActorUserId);

        await audit.RecordAsync(
            AuthAuditAction.PasswordResetCancelledByOffice,
            true,
            targetId,
            CancelModality,
            ct,
            outcome: "Cancelled",
            resetTokenId: pending[^1].Id
        );
        // 03 §18.4: el link se usó entre la lectura y el guardado; se relee para responder lo que pasó.
        if (!await ConcurrentSave.TrySaveAsync(unitOfWork, ct))
            return Result.Failure(
                (await credentials.GetLatestPasswordResetAsync(target.Id, ct))?.UsedAtUtc is not null
                    ? CredentialAdministrationErrors.LinkAlreadyUsed
                    : new Error(WolverineConflictPolicy.ConcurrencyConflictCode, "The link changed. Refresh and retry.")
            );
        return Result.Success();
    }

    /// <summary>Lectura del panel: misma política (sin exigir correo verificado) y sin auditar.</summary>
    public static async Task<Result<OfficeCredentialSnapshot>> ReadAsync(
        Guid tenantId,
        Guid actorUserId,
        User? target,
        UserAccountKind kind,
        CredentialAdministrationOptions options,
        IUserRepository users,
        ICredentialTokenRepository credentials,
        INotificationDeliveryClient deliveries,
        CancellationToken ct
    )
    {
        var authorized = await AuthorizeAsync(
            tenantId,
            actorUserId,
            target,
            CredentialAdministrationAction.CancelResetLink,
            kind,
            options.OwnerOnly,
            users,
            ct
        );
        if (authorized.IsFailure)
            return Result.Failure<OfficeCredentialSnapshot>(authorized.Error);

        bool Can(CredentialAdministrationAction action) =>
            CredentialAdministrationPolicy
                .CanActOn(authorized.Value, target!, action, kind, options.OwnerOnly)
                .IsSuccess;
        // Botones según política y estado (03 §4.6): asegurada ⇒ solo "secure" (reenvío); el cambio solo desde Active.
        var status = target!.CredentialStatus;
        var now = DateTime.UtcNow;
        // D-08: durante la pausa no se ofrece ningún link (ni el normal ni el seguro).
        var cooldownUntil = EmailChangeCooldownUntil(target, options, now);
        var linksAllowed = cooldownUntil is null;
        var actions = new StaffCredentialActions(
            Can(CredentialAdministrationAction.SendResetLink)
                && status != CredentialStatus.ResetRequired
                && linksAllowed,
            Can(CredentialAdministrationAction.RequireChange) && status == CredentialStatus.Active,
            Can(CredentialAdministrationAction.Secure) && linksAllowed,
            Can(CredentialAdministrationAction.ResetMfa) && target.MfaEnabled
        );
        var latest = await credentials.GetLatestPasswordResetAsync(target!.Id, ct);
        var summary = ResetLinkSummary.From(latest, now);
        // La entrega solo importa mientras el link está vigente; se pregunta a Notification por su EventId.
        if (summary is { Status: ResetLinkStatus.Pending } && latest!.NotificationEventId is { } eventId)
            summary = summary with
            {
                Delivery = ResetLinkDeliveryResolver.Resolve(
                    await deliveries.GetEmailStatusAsync(tenantId, eventId, ct),
                    latest.CreatedAtUtc,
                    now
                ),
            };
        return Result.Success(new OfficeCredentialSnapshot(target, summary, actions, cooldownUntil));
    }

    /// <summary>
    /// Cambio obligatorio (03 §18.3): candado → transición → corte de sesiones → auditoría → guardar → publicar.
    /// Repetido sobre una cuenta que ya lo tiene: sin efectos nuevos (<c>AlreadyInState</c>).
    /// </summary>
    public static async Task<Result<CredentialStateChangedResponse>> RequireChangeAsync(
        Guid targetId,
        User? target,
        CredentialAdministrationRequest request,
        CredentialAdministrationOptions options,
        IUserRepository users,
        IUserLoginLock userLock,
        SessionAccessCutoff cutoff,
        CredentialAdminAudit audit,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var authorized = await AuthorizeAsync(
            audit.TenantId,
            audit.ActorUserId,
            target,
            CredentialAdministrationAction.RequireChange,
            audit.AccountKind,
            options.OwnerOnly,
            users,
            ct
        );
        if (authorized.IsFailure)
            return Result.Failure<CredentialStateChangedResponse>(
                await DenyAsync(audit, targetId, RequireChangeModality, authorized.Error, unitOfWork, ct)
            );

        var reason = CredentialAdministrationReason.Create(request.ReasonCode, request.ReasonNote);
        if (reason.IsFailure)
            return Result.Failure<CredentialStateChangedResponse>(reason.Error);

        await userLock.AcquireAsync(target!, ct);
        var now = DateTime.UtcNow;
        var transition = target!.RequirePasswordChange(reason.Value, audit.ActorUserId, now);
        if (transition.IsFailure)
            return Result.Failure<CredentialStateChangedResponse>(transition.Error);
        if (transition.Value == CredentialTransition.AlreadyInState)
            return Result.Success(new CredentialStateChangedResponse(target.CredentialStatus, true, 0));

        var revoked = await cutoff.ForUserAsync(target, CredentialChangeRequiredReason, ct);
        await audit.RecordAsync(
            AuthAuditAction.PasswordChangeRequired,
            true,
            targetId,
            RequireChangeModality,
            ct,
            outcome: "Changed",
            reasonCode: request.ReasonCode.ToString(),
            identityConfirmed: request.IdentityConfirmed
        );
        await unitOfWork.SaveChangesAsync(ct);
        await PublishStateAlertAsync(target, SecurityAlertType.PasswordChangeRequired, request, audit, bus);
        if (revoked > 0)
            await bus.PublishAsync(
                SessionEndedEvents.ForUser(
                    target.TenantId,
                    target.Id,
                    null,
                    CredentialChangeRequiredReason,
                    now,
                    audit.CorrelationId
                )
            );
        return Result.Success(new CredentialStateChangedResponse(target.CredentialStatus, false, revoked));
    }

    /// <summary>
    /// Cuenta asegurada (03 §3 fila 3, §18.3): la contraseña deja de servir, sesiones y dispositivos de confianza
    /// revocados, MFA intacto, links pendientes anulados y un link <c>Incident</c> nuevo. Sobre una cuenta ya
    /// asegurada (AJ-15) reemite el link seguro (el pendiente queda <c>Superseded</c>), salvo el mismo pedido &lt; 60 s.
    /// Todo lo que puede fallar se comprueba antes de cambiar nada.
    /// </summary>
    public static async Task<Result<AccountSecuredResponse>> SecureAsync(
        Guid targetId,
        User? target,
        CredentialAdministrationRequest request,
        CredentialAdministrationOptions options,
        IUserRepository users,
        ICredentialTokenRepository credentials,
        ISecureTokenService tokens,
        IAccountTokenProtector tokenProtector,
        ITenantRegistry tenants,
        IUserLoginLock userLock,
        SessionAccessCutoff cutoff,
        IMfaRepository mfa,
        CredentialAdminAudit audit,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var authorized = await AuthorizeAsync(
            audit.TenantId,
            audit.ActorUserId,
            target,
            CredentialAdministrationAction.Secure,
            audit.AccountKind,
            options.OwnerOnly,
            users,
            ct
        );
        if (authorized.IsFailure)
            return Result.Failure<AccountSecuredResponse>(
                await DenyAsync(audit, targetId, SecureModality, authorized.Error, unitOfWork, ct)
            );

        var reason = CredentialAdministrationReason.Create(request.ReasonCode, request.ReasonNote);
        if (reason.IsFailure)
            return Result.Failure<AccountSecuredResponse>(reason.Error);
        var allowed = target!.CanMarkResetRequired(reason.Value);
        if (allowed.IsFailure)
            return Result.Failure<AccountSecuredResponse>(allowed.Error);
        if (await RejectCooldownAsync(audit, target, SecureModality, options, unitOfWork, ct) is { } cooldown)
            return Result.Failure<AccountSecuredResponse>(cooldown);

        await userLock.AcquireAsync(target, ct);
        var now = DateTime.UtcNow;
        var alreadySecured = target.CredentialStatus == CredentialStatus.ResetRequired;
        var linksRevoked = (await credentials.GetPendingPasswordResetsAsync(target.Id, now, ct)).Count;
        var issued = await PasswordResetIssuer.IssueByOfficeAsync(
            target,
            ResetOrigin.Incident,
            audit.ActorUserId,
            options,
            credentials,
            tokens,
            tokenProtector,
            tenants,
            userLock,
            audit.IpAddress,
            audit.CorrelationId,
            ct,
            alreadySecured ? ResetRevocationReason.Superseded : ResetRevocationReason.AccountSecured
        );
        if (issued.IsFailure)
        {
            var error =
                issued.Error.Code == CredentialAdministrationErrors.TargetRateLimited.Code
                    ? await DenyAsync(audit, targetId, SecureModality, issued.Error, unitOfWork, ct)
                    : issued.Error;
            return Result.Failure<AccountSecuredResponse>(error);
        }

        var issue = issued.Value;
        // El mismo pedido < 60 s: el link seguro pendiente sigue, sin otro correo ni otra revocación.
        if (issue.Event is null)
            return Result.Success(new AccountSecuredResponse(0, 0, 0, issue.Token.Id, AlreadyInState: true));

        target.MarkResetRequired(reason.Value, audit.ActorUserId, now);
        var sessionsRevoked = await cutoff.ForUserAsync(target, AccountSecuredReason, ct);
        var devicesRevoked = 0;
        foreach (var device in await mfa.GetTrustedDevicesAsync(target.Id, ct))
        {
            if (!device.IsActive)
                continue;
            device.Revoke();
            devicesRevoked++;
        }

        await audit.RecordAsync(
            AuthAuditAction.UserSecured,
            true,
            targetId,
            SecureModality,
            ct,
            outcome: alreadySecured ? "Reissued" : "Secured",
            resetTokenId: issue.Token.Id,
            notificationEventId: issue.Token.NotificationEventId,
            reasonCode: request.ReasonCode.ToString(),
            identityConfirmed: request.IdentityConfirmed
        );
        await unitOfWork.SaveChangesAsync(ct);
        await bus.PublishAsync(issue.Event);
        await PublishStateAlertAsync(target, SecurityAlertType.AccountSecured, request, audit, bus);
        if (sessionsRevoked > 0)
            await bus.PublishAsync(
                SessionEndedEvents.ForUser(
                    target.TenantId,
                    target.Id,
                    null,
                    AccountSecuredReason,
                    now,
                    audit.CorrelationId
                )
            );
        return Result.Success(
            new AccountSecuredResponse(sessionsRevoked, devicesRevoked, linksRevoked, issue.Token.Id, alreadySecured)
        );
    }

    /// <summary>
    /// Reset de MFA por la oficina (03 §3 fila 4, §11): factores y dispositivos fuera, sesiones cortadas, contraseña
    /// intacta. Sin factores: <c>NotEnabled</c> sin efectos. Motivo obligatorio (D-16); la nota no se audita.
    /// </summary>
    public static async Task<Result<StaffMfaResetResponse>> ResetMfaAsync(
        Guid targetId,
        User? target,
        CredentialAdministrationRequest request,
        CredentialAdministrationOptions options,
        IUserRepository users,
        IUserLoginLock userLock,
        SessionAccessCutoff cutoff,
        IMfaRepository mfa,
        CredentialAdminAudit audit,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var authorized = await AuthorizeAsync(
            audit.TenantId,
            audit.ActorUserId,
            target,
            CredentialAdministrationAction.ResetMfa,
            audit.AccountKind,
            options.OwnerOnly,
            users,
            ct
        );
        if (authorized.IsFailure)
            return Result.Failure<StaffMfaResetResponse>(
                await DenyAsync(audit, targetId, ResetMfaModality, authorized.Error, unitOfWork, ct)
            );

        var reason = CredentialAdministrationReason.Create(request.ReasonCode, request.ReasonNote);
        if (reason.IsFailure)
            return Result.Failure<StaffMfaResetResponse>(reason.Error);

        await userLock.AcquireAsync(target!, ct);
        var hasFactors = target!.MfaEnabled || (await mfa.GetMethodsAsync(target.Id, ct)).Count > 0;
        if (!hasFactors)
            return Result.Success(new StaffMfaResetResponse(MfaResetOutcomeNotEnabled, 0));

        var now = DateTime.UtcNow;
        await MfaFactors.ClearAsync(target, mfa, ct);
        var revoked = await cutoff.ForUserAsync(target, MfaResetByOfficeReason, ct);
        await audit.RecordAsync(
            AuthAuditAction.MfaResetByOffice,
            true,
            targetId,
            ResetMfaModality,
            ct,
            outcome: MfaResetOutcomeReset,
            reasonCode: request.ReasonCode.ToString(),
            identityConfirmed: request.IdentityConfirmed
        );
        await unitOfWork.SaveChangesAsync(ct);
        await PublishStateAlertAsync(target, SecurityAlertType.MfaResetByOffice, request, audit, bus);
        if (revoked > 0)
            await bus.PublishAsync(
                SessionEndedEvents.ForUser(
                    target.TenantId,
                    target.Id,
                    null,
                    MfaResetByOfficeReason,
                    now,
                    audit.CorrelationId
                )
            );
        return Result.Success(new StaffMfaResetResponse(MfaResetOutcomeReset, revoked));
    }

    public const string MfaResetOutcomeReset = "Reset";
    public const string MfaResetOutcomeNotEnabled = "NotEnabled";

    /// <summary>Motivos de revocación de sesiones (03 §12); los lee el CRM y el portal en el aviso de cierre.</summary>
    public const string CredentialChangeRequiredReason = "credential_change_required";

    public const string AccountSecuredReason = "account_secured";

    public const string MfaResetByOfficeReason = "mfa_reset_by_office";

    // Al afectado, al Owner y a los suscriptores menos el actor; sin nombres ni la nota del motivo.
    private static ValueTask PublishStateAlertAsync(
        User target,
        string alertType,
        CredentialAdministrationRequest request,
        CredentialAdminAudit audit,
        IMessageBus bus
    ) =>
        bus.PublishAsync(
            new SecurityAlertIntegrationEvent
            {
                TenantId = target.TenantId,
                UserId = target.Id,
                AlertType = alertType,
                CorrelationId = audit.CorrelationId,
                DetailsJson = JsonSerializer.Serialize(
                    new
                    {
                        origin = "office",
                        actorUserId = audit.ActorUserId,
                        reasonCode = request.ReasonCode.ToString(),
                    }
                ),
            }
        );

    public static async Task<Error> DenyAsync(
        CredentialAdminAudit audit,
        Guid targetId,
        string modality,
        Error error,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        await audit.RecordAsync(AuthAuditAction.CredentialAdminDenied, false, targetId, modality, ct, code: error.Code);
        await unitOfWork.SaveChangesAsync(ct);
        return error;
    }
}
