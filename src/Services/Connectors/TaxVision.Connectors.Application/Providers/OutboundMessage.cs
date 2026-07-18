namespace TaxVision.Connectors.Application.Providers;

/// <summary>
/// DTO normalizado que cruza el M2M de envío (D3 §3.2/§4.4) — nunca MIME crudo, porque Graph no lo
/// acepta para crear un envío nuevo (solo Gmail lo hace). Cada <see cref="IOutboundEmailProviderClient"/>
/// lo traduce a la forma nativa del proveedor. Inline assets no entran en v1 (D3 §11, pendiente
/// documentado — mismo criterio de incrementalidad que Postmaster usó para su propia Fase 3.5).
/// <see cref="Attachments"/> cierra el gap de D3 Compose §9 — bytes ya resueltos por el caller
/// (Postmaster), Connectors nunca los pide a CloudStorage.
/// </summary>
public sealed record OutboundMessage(
    string Subject,
    string Html,
    string? Text,
    IReadOnlyList<string> To,
    IReadOnlyList<string> Cc,
    IReadOnlyList<string> Bcc,
    string? ReplyToDisplayAddress,
    string? InReplyToInternetMessageId,
    IReadOnlyList<string>? References,
    string? ReplyToProviderMessageId,
    IReadOnlyList<OutboundAttachment>? Attachments = null
)
{
    public IReadOnlyList<OutboundAttachment> Attachments { get; init; } = Attachments ?? [];
}
