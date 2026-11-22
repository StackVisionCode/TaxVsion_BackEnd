using BuildingBlocks.Messaging.AuthIntegrationEvents;
using TaxVision.Auth.Domain.RefreshTokens;
using TaxVision.Auth.Domain.Sessions;

namespace TaxVision.Auth.Application.Sessions;

/// <summary>Arma el aviso de fin de sesión: solo ids, el motivo y el canal.</summary>
public static class SessionEndedEvents
{
    public static SessionEndedIntegrationEvent ForSession(
        UserSession session,
        string reason,
        DateTime now,
        string? correlationId = null
    ) =>
        new()
        {
            TenantId = session.TenantId,
            UserId = session.UserId,
            SessionId = session.Id,
            Reason = reason,
            Channel = session.Channel.ToString(),
            EndedAtUtc = now,
            CorrelationId = correlationId ?? string.Empty,
        };

    /// <summary>Sesión identificada por un refresh token: el canal sale de su superficie.</summary>
    public static SessionEndedIntegrationEvent ForToken(
        RefreshToken token,
        string reason,
        DateTime now,
        string? correlationId = null
    ) =>
        new()
        {
            TenantId = token.TenantId,
            UserId = token.UserId,
            SessionId = token.SessionId,
            Reason = reason,
            Channel = (token.Surface == SessionSurface.Mobile ? SessionChannel.Mobile : SessionChannel.Web).ToString(),
            EndedAtUtc = now,
            CorrelationId = correlationId ?? string.Empty,
        };

    /// <summary>Todas las sesiones del usuario, salvo <paramref name="exceptSessionId"/> si viene.</summary>
    public static SessionEndedIntegrationEvent ForUser(
        Guid tenantId,
        Guid userId,
        Guid? exceptSessionId,
        string reason,
        DateTime now,
        string? correlationId = null
    ) =>
        new()
        {
            TenantId = tenantId,
            UserId = userId,
            ExceptSessionId = exceptSessionId,
            Reason = reason,
            EndedAtUtc = now,
            CorrelationId = correlationId ?? string.Empty,
        };
}
