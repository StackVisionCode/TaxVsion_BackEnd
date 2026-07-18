using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Results;

namespace TaxVision.Notification.Application.Abstractions;

/// <summary>
/// Email ya renderizado (subject + HTML + texto plano opcional) devuelto por Scribe.
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
    IReadOnlyList<EmailInlineAssetReference> InlineAssets
)
{
    public ScribeRenderedEmail(string Subject, string Html, string? Text)
        : this(Subject, Html, Text, []) { }
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
    Task<Result<ScribeRenderedEmail>> RenderAsync(
        string eventKey,
        Guid tenantId,
        IReadOnlyDictionary<string, object?> variables,
        CancellationToken ct = default
    );
}
