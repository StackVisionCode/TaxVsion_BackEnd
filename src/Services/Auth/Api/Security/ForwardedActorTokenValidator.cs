using System.Security.Claims;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Permissions;
using BuildingBlocks.Results;
using BuildingBlocks.Sessions;
using BuildingBlocks.Web.Session;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Api.Security;

/// <summary>Actor leído del token reenviado (firma validada por Auth), nunca del cuerpo del M2M.</summary>
public sealed record ForwardedActor(
    Guid UserId,
    Guid TenantId,
    Guid SessionId,
    DateTime? ReauthenticatedAtUtc,
    ActorType ActorType
);

/// <summary>Lo que un M2M exige del actor reenviado: tipos de actor, permiso y si pide step-up.</summary>
public sealed record ForwardedActorRequirement(
    IReadOnlyCollection<ActorType> AllowedActorTypes,
    string RequiredPermission,
    bool RequireReauthentication
);

/// <summary>Resultado de la validación; actor type y sub solo si la firma fue válida (para auditar el rechazo).</summary>
public sealed record ForwardedActorValidation(
    Result<ForwardedActor> Outcome,
    string? SignedActorType,
    Guid? SignedUserId
);

/// <summary>
/// SR-03, SR-05 y SR-11: valida el access token del usuario que Customer reenvía en
/// <see cref="ForwardedActorToken.HeaderName"/> con los mismos parámetros del JwtBearer de Auth, pero solo con la
/// audiencia de servicios (fuera Account, solo-MFA y cambio obligatorio). Exige actor type permitido, oficina y
/// sesión viva; el step-up sale del token y el permiso efectivo (con Deny) se relee en vivo. PlatformAdmin pasa el
/// permiso, igual que <c>HasPermission</c> en el resto del sistema.
/// </summary>
public sealed class ForwardedActorTokenValidator(
    IOptionsMonitor<JwtBearerOptions> jwtBearer,
    IConfiguration configuration,
    ISessionDenylistReader denylist,
    IOptions<SessionDenylistOptions> denylistOptions,
    IUserPermissionsProjectionReader permissions,
    ILogger<ForwardedActorTokenValidator> logger
)
{
    private const string BearerPrefix = "Bearer ";
    private static readonly JsonWebTokenHandler Handler = new() { MapInboundClaims = false };

    public async Task<Result<ForwardedActor>> ValidateAsync(
        string? header,
        Guid tenantId,
        Guid requestedByUserId,
        ActorType expectedActorType,
        string requiredPermission,
        bool requireReauthentication,
        CancellationToken ct
    ) =>
        (
            await ValidateAsync(
                header,
                tenantId,
                requestedByUserId,
                new ForwardedActorRequirement([expectedActorType], requiredPermission, requireReauthentication),
                ct
            )
        ).Outcome;

    /// <summary>Sin <paramref name="requestedByUserId"/> (lecturas sin sobre) el actor es el <c>sub</c> del token.</summary>
    public async Task<ForwardedActorValidation> ValidateAsync(
        string? header,
        Guid tenantId,
        Guid? requestedByUserId,
        ForwardedActorRequirement requirement,
        CancellationToken ct
    )
    {
        var invalid = new ForwardedActorValidation(
            Result.Failure<ForwardedActor>(CredentialAdministrationErrors.ActorTokenInvalid),
            null,
            null
        );
        if (
            string.IsNullOrWhiteSpace(header)
            || !header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase)
            || header[BearerPrefix.Length..].Trim() is not { Length: > 0 } raw
        )
            return invalid;

        var parameters = jwtBearer.Get(JwtBearerDefaults.AuthenticationScheme).TokenValidationParameters.Clone();
        parameters.ValidAudience =
            configuration["Jwt:Audience"] ?? throw new InvalidOperationException("Jwt:Audience is missing.");
        parameters.ValidAudiences = null;

        var validated = await Handler.ValidateTokenAsync(raw, parameters);
        if (!validated.IsValid)
        {
            // Sin el token ni su contenido en el log.
            logger.LogWarning(
                "Forwarded actor token rejected: {Reason}.",
                validated.Exception?.GetType().Name ?? "invalid"
            );
            return invalid;
        }

        var principal = new ClaimsPrincipal(validated.ClaimsIdentity);
        var signedActorType = principal.FindFirst(ClaimNames.ActorType)?.Value;
        Guid? signedUserId = principal.TryGetUserId(out var userId) ? userId : null;
        var rejected = invalid with { SignedActorType = signedActorType, SignedUserId = signedUserId };
        // Comparación por nombre: un actor_type numérico no cuenta.
        var allowed = requirement.AllowedActorTypes.Where(t => t.ToString() == signedActorType).Distinct().ToList();
        if (
            PasswordChangeToken.IsRequired(principal)
            || MfaEnrollmentToken.IsPending(principal)
            || allowed.Count != 1
            || signedUserId is null
            || (requestedByUserId is { } requestedBy && userId != requestedBy)
            || !principal.TryGetTenantId(out var tokenTenantId)
            || tokenTenantId != tenantId
            || !principal.TryGetSessionId(out var sessionId)
        )
            return rejected;

        if (await IsSessionRevokedAsync(sessionId, ct))
            return rejected;

        DateTime? reauthenticatedAtUtc = principal.TryGetReauthenticatedAt(out var reauthAt)
            ? reauthAt.UtcDateTime
            : null;
        if (requirement.RequireReauthentication)
        {
            var window = ReauthenticationWindow.Validate(reauthenticatedAtUtc ?? default, DateTime.UtcNow);
            if (window.IsFailure)
                return rejected with { Outcome = Result.Failure<ForwardedActor>(window.Error) };
        }

        var actor = new ForwardedActor(userId, tenantId, sessionId, reauthenticatedAtUtc, allowed[0]);
        if (!await HasPermissionAsync(actor, requirement.RequiredPermission, ct))
            return rejected with
            {
                Outcome = Result.Failure<ForwardedActor>(CredentialAdministrationErrors.ActorNotAllowed),
            };

        return new ForwardedActorValidation(Result.Success(actor), signedActorType, userId);
    }

    /// <summary>Permiso efectivo en vivo (roles − Deny); PlatformAdmin pasa, como en <c>HasPermission</c>.</summary>
    public async Task<bool> HasPermissionAsync(ForwardedActor actor, string permission, CancellationToken ct)
    {
        if (actor.ActorType == ActorType.PlatformAdmin)
            return true;
        var snapshot = await permissions.GetSnapshotAsync(actor.TenantId, actor.UserId, ct);
        return snapshot is not null && snapshot.PermissionCodes.Contains(permission, StringComparer.OrdinalIgnoreCase);
    }

    // Misma política que SessionDenylistMiddleware si Redis no responde.
    private async Task<bool> IsSessionRevokedAsync(Guid sessionId, CancellationToken ct)
    {
        if (!denylistOptions.Value.Enabled)
            return false;
        try
        {
            return await denylist.IsSessionDeniedAsync(sessionId, ct);
        }
        catch (SessionDenylistUnavailableException ex)
        {
            var failClosed = denylistOptions.Value.FailureMode == SessionDenylistFailureMode.FailClosed;
            logger.LogWarning(
                ex,
                "Session denylist unavailable for a forwarded actor token (fail closed: {FailClosed}).",
                failClosed
            );
            return failClosed;
        }
    }
}
