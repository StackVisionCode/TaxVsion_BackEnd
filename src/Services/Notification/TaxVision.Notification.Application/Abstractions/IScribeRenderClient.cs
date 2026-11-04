using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Results;

namespace TaxVision.Notification.Application.Abstractions;

/// <summary>
/// Email ya renderizado (subject + HTML + texto plano opcional) devuelto por Scribe.
/// <paramref name="DispatchScope"/> es de quien sale el correo, y lo decide el LAYOUT en Scribe, no el
/// consumer — ver <c>EmailDispatchScope</c>.
/// <paramref name="InlineAssets"/> son las referencias a logos/imágenes CID que Scribe resolvió para
/// este render (Scribe Fase 4.5) — reusa <see cref="EmailInlineAssetReference"/> (BuildingBlocks)
/// porque es el mismo tipo que después viaja sin cambios dentro de
/// <see cref="NotificationsEmailSendRequestedIntegrationEvent.InlineAssets"/> (Hardening Fase 9: antes
/// de esta fase este campo no existía y Scribe lo devolvía en vano — el consumer nunca lo pasaba). El
/// default vacío preserva a los consumers que aún no arman <c>Dictionary</c> con logo (ninguno hoy)
/// o a tests que construyen <c>ScribeRenderedEmail</c> con los 3 argumentos originales.
/// </summary>
public sealed record ScribeRenderedEmail(
    string Subject,
    string Html,
    string? Text,
    IReadOnlyList<EmailInlineAssetReference> InlineAssets,
    EmailDispatchScope DispatchScope
)
{
    public ScribeRenderedEmail(string Subject, string Html, string? Text)
        : this(Subject, Html, Text, [], EmailDispatchScope.System) { }

    public ScribeRenderedEmail(
        string Subject,
        string Html,
        string? Text,
        IReadOnlyList<EmailInlineAssetReference> InlineAssets
    )
        : this(Subject, Html, Text, InlineAssets, EmailDispatchScope.System) { }
}

public sealed record ScribeRenderedText(string Subject, string Text);

/// <summary>
/// Quién lee el correo. Lo decide el consumer, que es el que sabe a quién le escribe: un usuario del
/// equipo (Staff) o un cliente, cuenta de portal, firmante o invitado externo (Client). Viaja a Scribe
/// como texto y elige el logo de la oficina (CRM o Portal).
/// </summary>
public enum ScribeRenderAudience
{
    Staff,
    Client,
}

/// <summary>
/// Cliente del microservicio Scribe (HTTP a POST /scribe/render) — Fase 8: reemplaza los catálogos
/// locales <c>EmailTemplates</c>/<c>SignatureTemplateCatalog</c>. Los consumers de Notification
/// corren siempre en contexto background (Wolverine), así que autentica exclusivamente con un token
/// M2M (<see cref="IServiceTokenAcquirer"/>) del tenant indicado — no hay contexto de usuario que
/// reenviar, a diferencia de <see cref="ICloudStorageClient"/>.
/// </summary>
public interface IScribeRenderClient
{
    /// <summary>Render para el equipo de la oficina (<see cref="ScribeRenderAudience.Staff"/>).</summary>
    Task<Result<ScribeRenderedEmail>> RenderAsync(
        string eventKey,
        Guid tenantId,
        IReadOnlyDictionary<string, object?> variables,
        CancellationToken ct = default
    );

    /// <summary>
    /// Render para una audiencia: <see cref="ScribeRenderAudience.Client"/> lleva el logo del portal de
    /// la oficina. Default de interfaz = render de siempre, para los dobles que no lo implementan.
    /// </summary>
    Task<Result<ScribeRenderedEmail>> RenderAsync(
        string eventKey,
        Guid tenantId,
        IReadOnlyDictionary<string, object?> variables,
        ScribeRenderAudience audience,
        CancellationToken ct = default
    ) => RenderAsync(eventKey, tenantId, variables, ct);

    Task<Result<ScribeRenderedText>> RenderTextAsync(
        string eventKey,
        Guid tenantId,
        IReadOnlyDictionary<string, object?> variables,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            Result.Failure<ScribeRenderedText>(
                new Error("Notification.ScribeTextUnsupported", "Text rendering is not supported by this client.")
            )
        );
}
