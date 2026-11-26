namespace TaxVision.Auth.Domain.Sessions;

/// <summary>Estado de una sesión para la gestión del equipo.</summary>
public enum SessionStatus
{
    Active,
    Expired,
    SignedOut,
    Revoked,
    Replaced,
    ClosedForSecurity,
}

/// <summary>Traduce el motivo de cierre (o la política, si sigue abierta) a <see cref="SessionStatus"/>.</summary>
public static class SessionStatuses
{
    public static readonly string[] SignedOutReasons = ["user_logout", "user_logout_all", "account_logout"];
    public static readonly string[] RevokedReasons = ["admin_revoke"];
    public static readonly string[] ReplacedReasons = ["single_session_superseded"];

    // El barrido cierra con "expired"; el refresh, con el motivo de la política.
    public static readonly string[] ExpiredReasons = ["expired", "idle_timeout", "absolute_lifetime"];

    public static readonly string[] KnownReasons =
    [
        .. SignedOutReasons,
        .. RevokedReasons,
        .. ReplacedReasons,
        .. ExpiredReasons,
    ];

    public static SessionStatus Of(DateTime? revokedAtUtc, string? revokedReason, bool withinPolicy)
    {
        if (revokedAtUtc is null)
            return withinPolicy ? SessionStatus.Active : SessionStatus.Expired;

        return revokedReason switch
        {
            _ when SignedOutReasons.Contains(revokedReason) => SessionStatus.SignedOut,
            _ when RevokedReasons.Contains(revokedReason) => SessionStatus.Revoked,
            _ when ReplacedReasons.Contains(revokedReason) => SessionStatus.Replaced,
            _ when ExpiredReasons.Contains(revokedReason) => SessionStatus.Expired,
            _ => SessionStatus.ClosedForSecurity,
        };
    }
}
