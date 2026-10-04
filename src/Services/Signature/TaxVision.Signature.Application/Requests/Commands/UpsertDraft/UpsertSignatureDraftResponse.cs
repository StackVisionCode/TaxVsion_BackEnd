namespace TaxVision.Signature.Application.Requests.Commands.UpsertDraft;

/// <summary>El token de versión que el cliente debe reenviar en el siguiente autosave.</summary>
public sealed record UpsertSignatureDraftResponse(DateTime UpdatedAtUtc);
