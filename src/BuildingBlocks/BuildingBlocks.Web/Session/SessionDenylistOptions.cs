namespace BuildingBlocks.Web.Session;

/// <summary>
/// Qué hacer cuando el store de la denylist no responde y no se puede saber si la sesión sigue viva.
/// </summary>
public enum SessionDenylistFailureMode
{
    /// <summary>
    /// Dejar pasar la request. Default: un Redis caído no debe tumbar todo el tráfico autenticado, y
    /// la ventana de exposición está acotada por el <c>exp</c> del access token (15 min).
    /// </summary>
    FailOpen,

    /// <summary>
    /// Responder 503. Para tenants o entornos donde una revocación ignorada es peor que una caída —
    /// es una decisión de riesgo, no el default.
    /// </summary>
    FailClosed,
}

/// <summary>
/// RBAC Fase 6 — flag por servicio para apagar el chequeo de denylist sin redeploy. Default
/// habilitado.
/// </summary>
public sealed class SessionDenylistOptions
{
    public const string SectionName = "SessionDenylist";

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// H-06 — el fail-open era implícito, invisible y no se podía cambiar. Ahora es explícito, se
    /// contabiliza en <c>authz.session_denylist_unavailable</c> y se puede endurecer por servicio.
    /// </summary>
    public SessionDenylistFailureMode FailureMode { get; set; } = SessionDenylistFailureMode.FailOpen;
}
