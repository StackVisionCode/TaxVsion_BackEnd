namespace TaxVision.Correspondence.Application.Compose;

/// <summary>
/// Fila lean para <c>GET /correspondence/drafts?customerId=</c> (Fase 15) — "retomar un
/// autoguardado", no la vista completa del composer (esa es <see cref="DraftDetail"/>, Fase 11).
/// A propósito sin body/recipients/attachments: la UI solo necesita esto para decidir qué
/// borrador abrir, y recién ahí pide <see cref="DraftDetail"/> por <c>GET /drafts/{id}</c>.
/// </summary>
public sealed record DraftListItem(
    Guid DraftId,
    string Subject,
    string Status,
    bool IsReply,
    DateTime UpdatedAtUtc,
    DateTime? LastAutoSavedAtUtc
);
