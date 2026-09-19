namespace TaxVision.Signature.Domain.Requests;

/// <summary>
/// Entrada cruda de un valor de campo de texto que el firmante envía al firmar: el id del
/// campo y el texto tal cual lo escribió. La validación/normalización ocurre dentro del
/// aggregate (<see cref="Signer.CaptureFieldValues"/>), no aquí.
/// </summary>
public readonly record struct SignerFieldValueInput(Guid FieldId, string? Value);
