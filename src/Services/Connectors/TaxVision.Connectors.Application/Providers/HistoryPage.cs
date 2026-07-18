namespace TaxVision.Connectors.Application.Providers;

/// <summary>
/// Página de mensajes nuevos desde el último cursor. NewMessageIds son solo los IDs — el caller
/// (Fase 7) pide el detalle de cada uno vía GetMessageAsync (metadata-first, nunca el body acá).
/// </summary>
public sealed record HistoryPage(IReadOnlyList<string> NewMessageIds, string? NextCursor, bool HasMore);
