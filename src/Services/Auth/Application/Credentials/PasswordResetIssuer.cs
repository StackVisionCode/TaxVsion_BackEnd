using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Application.Credentials.Administration;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.Credentials;
using TaxVision.Auth.Domain.Users;
using Wolverine;

namespace TaxVision.Auth.Application.Credentials;

/// <summary>Link emitido por la oficina. <see cref="Event"/> es null si se devolvió el pendiente (idempotencia).</summary>
public sealed record OfficeResetIssue(
    ResetLinkIssueOutcome Outcome,
    PasswordResetToken Token,
    PasswordResetRequestedIntegrationEvent? Event
);

/// <summary>
/// Emite el link de reset de un usuario (forgot, envío y reenvío de la oficina; asegurar después). Bajo el candado de
/// fila del usuario revoca los pendientes como <see cref="ResetRevocationReason.Superseded"/> (I-4), fija el EventId
/// del correo y lo guarda en el token. No guarda: el handler hace un único SaveChanges.
/// </summary>
public static class PasswordResetIssuer
{
    public const string OfficeAdministratorRole = "office_administrator";

    private static readonly TimeSpan IdempotencyWindow = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan MaxOfficeLifetime = TimeSpan.FromHours(24);
    private static readonly TimeSpan CapWindow = TimeSpan.FromHours(24);

    /// <summary>Self-service: publica y audita aquí mismo, como hacía el forgot.</summary>
    public static async Task<PasswordResetToken> IssueAsync(
        User user,
        ResetOrigin origin,
        Guid? requestedByUserId,
        TimeSpan validity,
        ICredentialTokenRepository credentials,
        ISecureTokenService tokens,
        IAccountTokenProtector tokenProtector,
        ITenantRegistry tenants,
        IUserLoginLock userLock,
        IAuthAuditWriter audit,
        IRequestContext request,
        ICorrelationContext correlation,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        // Dos emisiones a la vez para el mismo usuario se ponen en fila: la segunda ve y revoca a la primera.
        await userLock.AcquireAsync(user, ct);
        var rawToken = tokens.GenerateToken();
        var sealedToken = tokenProtector.Seal(
            AuthTokenPayloadSchemas.PasswordReset,
            AuthTokenPayloadSchemas.PasswordResetBinding(user.TenantId, user.Id),
            rawToken
        );
        var (resetToken, evt) = await IssueLockedAsync(
            user,
            origin,
            requestedByUserId,
            validity,
            tokens.Hash(rawToken),
            sealedToken,
            credentials,
            tenants,
            request.IpAddress,
            correlation.CorrelationId,
            ct
        );
        await bus.PublishAsync(evt);

        await audit.AddAsync(
            AuthAuditLog.Record(
                user.TenantId,
                user.Id,
                AuthAuditAction.PasswordResetRequested,
                true,
                request.IpAddress,
                request.UserAgent,
                correlation.CorrelationId
            ),
            ct
        );
        return resetToken;
    }

    /// <summary>
    /// Link pedido por la oficina (03 §4.8, §6.6, §8.2): vida por origen (tope 24 h), el mismo pedido &lt; 60 s
    /// devuelve el pendiente, tope de links por objetivo en 24 h y nunca RawToken (sin clave de sellado no emite).
    /// No publica ni audita: el handler guarda y luego publica <see cref="OfficeResetIssue.Event"/>.
    /// </summary>
    public static async Task<Result<OfficeResetIssue>> IssueByOfficeAsync(
        User target,
        ResetOrigin origin,
        Guid requestedByUserId,
        CredentialAdministrationOptions options,
        ICredentialTokenRepository credentials,
        ISecureTokenService tokens,
        IAccountTokenProtector tokenProtector,
        ITenantRegistry tenants,
        IUserLoginLock userLock,
        string? requestedIp,
        string correlationId,
        CancellationToken ct,
        ResetRevocationReason supersedeAs = ResetRevocationReason.Superseded
    )
    {
        await userLock.AcquireAsync(target, ct);
        var now = DateTime.UtcNow;

        var latest = await credentials.GetLatestPasswordResetAsync(target.Id, ct);
        if (
            latest is not null
            && latest.Origin == origin
            && latest.RequestedByUserId == requestedByUserId
            && now - latest.CreatedAtUtc < IdempotencyWindow
            && latest.StatusAt(now) == PasswordResetTokenStatus.Pending
        )
            return Result.Success(new OfficeResetIssue(ResetLinkIssueOutcome.AlreadyPending, latest, null));

        var issuedToday = await credentials.CountOfficePasswordResetsSinceAsync(target.Id, now - CapWindow, ct);
        if (issuedToday >= options.TargetDailyCap)
        {
            // Cota del 429: cuando el último link de la oficina sale de la ventana, salen todos.
            var wait = latest is { Origin: not ResetOrigin.SelfService }
                ? latest.CreatedAtUtc + CapWindow - now
                : CapWindow;
            return Result.Failure<OfficeResetIssue>(
                CredentialAdministrationErrors.TargetRateLimited.WithRetryAfter(wait)
            );
        }

        var rawToken = tokens.GenerateToken();
        var sealedToken = tokenProtector.Seal(
            AuthTokenPayloadSchemas.PasswordReset,
            AuthTokenPayloadSchemas.PasswordResetBinding(target.TenantId, target.Id),
            rawToken
        );
        if (sealedToken.ProtectedToken is null)
            return Result.Failure<OfficeResetIssue>(CredentialAdministrationErrors.DeliveryUnavailable);

        var lifetime =
            origin == ResetOrigin.Incident ? options.IncidentLinkLifetime : options.AdministrativeLinkLifetime;
        var (resetToken, evt) = await IssueLockedAsync(
            target,
            origin,
            requestedByUserId,
            lifetime > MaxOfficeLifetime ? MaxOfficeLifetime : lifetime,
            tokens.Hash(rawToken),
            sealedToken,
            credentials,
            tenants,
            requestedIp,
            correlationId,
            ct,
            supersedeAs
        );
        return Result.Success(new OfficeResetIssue(ResetLinkIssueOutcome.Issued, resetToken, evt));
    }

    private static async Task<(
        PasswordResetToken Token,
        PasswordResetRequestedIntegrationEvent Event
    )> IssueLockedAsync(
        User user,
        ResetOrigin origin,
        Guid? requestedByUserId,
        TimeSpan validity,
        string tokenHash,
        AccountTokenFields sealedToken,
        ICredentialTokenRepository credentials,
        ITenantRegistry tenants,
        string? requestedIp,
        string correlationId,
        CancellationToken ct,
        ResetRevocationReason supersedeAs = ResetRevocationReason.Superseded
    )
    {
        // Al asegurar la cuenta los pendientes quedan como AccountSecured; si no, Superseded (I-4).
        var now = DateTime.UtcNow;
        foreach (var pending in await credentials.GetPendingPasswordResetsAsync(user.Id, now, ct))
            pending.Revoke(now, supersedeAs, requestedByUserId);

        var eventId = Guid.NewGuid();
        var resetToken = PasswordResetToken.Create(
            user.TenantId,
            user.Id,
            tokenHash,
            requestedIp,
            validity,
            origin,
            requestedByUserId,
            eventId
        );
        await credentials.AddPasswordResetAsync(resetToken, ct);
        var tenant = await tenants.GetByIdAsync(user.TenantId, ct);

        var evt = new PasswordResetRequestedIntegrationEvent
        {
            EventId = eventId,
            TenantId = user.TenantId,
            UserId = user.Id,
            Email = user.Email,
            RawToken = sealedToken.RawToken,
            ProtectedToken = sealedToken.ProtectedToken,
            ExpiresAtUtc = resetToken.ExpiresAtUtc,
            ActorType = user.ActorType.ToString(),
            TenantName = tenant?.Name,
            CorrelationId = correlationId,
            Origin = origin.ToString(),
            RequestedByRole = origin == ResetOrigin.SelfService ? null : OfficeAdministratorRole,
        };
        return (resetToken, evt);
    }
}
