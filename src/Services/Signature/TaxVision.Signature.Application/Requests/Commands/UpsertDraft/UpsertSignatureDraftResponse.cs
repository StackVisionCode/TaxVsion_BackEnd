namespace TaxVision.Signature.Application.Requests.Commands.UpsertDraft;

/// <summary>
/// Token de versión para el siguiente autosave y los ids que el servidor resolvió, en el orden del
/// payload. El cliente los guarda: sin ellos, lo recién creado viajaría otra vez sin id.
/// </summary>
public sealed record UpsertSignatureDraftResponse(
    DateTime UpdatedAtUtc,
    IReadOnlyList<UpsertedDraftDocument> Documents,
    IReadOnlyList<Guid> SignerIds,
    IReadOnlyList<Guid> FieldIds
);

public sealed record UpsertedDraftDocument(string LocalId, Guid Id);
