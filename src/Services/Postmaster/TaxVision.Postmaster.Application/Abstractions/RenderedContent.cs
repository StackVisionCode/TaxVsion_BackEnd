using TaxVision.Postmaster.Domain.Sending;

namespace TaxVision.Postmaster.Application.Abstractions;

/// <summary>
/// Contenido ya renderizado por Scribe, listo para empaquetar en un MIME. En Fase 3 era un DTO
/// mínimo (Html/Text/Subject); el comentario original decía que <c>InlineAssets</c> se agregaría en
/// Fase 3.5 junto con el soporte CID, pero esa fase solo construyó <see cref="InlineAsset"/> y
/// <c>IInlineAssetFetcher</c> — este campo quedó sin agregar hasta Hardening Fase 9, que conecta el
/// pipeline completo (Scribe → Notification → este evento → acá).
/// </summary>
/// <param name="InlineAssets">
/// Referencias (no bytes) a los logos/imágenes que Scribe resolvió para este render. El consumer
/// (<c>NotificationsEmailSendRequestedConsumer</c>) las pasa tal cual a
/// <c>IInlineAssetFetcher.FetchAllAsync</c> — mismo tipo exacto que ese método ya espera, sin mapeo —
/// para obtener los bytes reales antes de invocar <c>IEmailSender.SendAsync</c>. Vacío por default:
/// el path OAuth (<c>IOAuthEmailSender</c>) y el envío síncrono de Correspondence
/// (<c>SendCorrespondenceMessageHandler</c>, fuera de alcance de esta fase) siguen sin soporte de
/// logos inline.
/// </param>
public sealed record RenderedContent(string Subject, string Html, string? Text, IReadOnlyList<InlineAsset> InlineAssets)
{
    /// <summary>
    /// Preserva a los callers/tests que construían <c>RenderedContent</c> con los 3 argumentos
    /// originales (path OAuth, Correspondence, y los tests existentes de Mime/Connectors) — quedan
    /// sin logos inline, comportamiento idéntico al de antes de esta fase.
    /// </summary>
    public RenderedContent(string Subject, string Html, string? Text)
        : this(Subject, Html, Text, []) { }
}
