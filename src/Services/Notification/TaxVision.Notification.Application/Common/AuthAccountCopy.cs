using TaxVision.Notification.Application.Abstractions;

namespace TaxVision.Notification.Application.Common;

/// <summary>
/// Cómo nombrar la cuenta en los correos de Auth. Una persona puede tener cuenta de staff y de portal en la
/// misma oficina (con el mismo email), así que cada correo dice de cuál habla y enlaza a su superficie.
/// </summary>
public static class AuthAccountCopy
{
    private const string PortalActorType = "CustomerPortal";

    public static bool IsPortal(string? actorType) => actorType == PortalActorType;

    /// <summary>Valor de la variable <c>account_kind</c> de las plantillas: <c>portal</c> o <c>workspace</c>.</summary>
    public static string Kind(string? actorType) => IsPortal(actorType) ? "portal" : "workspace";

    /// <summary>La cuenta de portal es del cliente: su correo lleva el logo del portal.</summary>
    public static ScribeRenderAudience Audience(string? actorType) =>
        IsPortal(actorType) ? ScribeRenderAudience.Client : ScribeRenderAudience.Staff;
}
