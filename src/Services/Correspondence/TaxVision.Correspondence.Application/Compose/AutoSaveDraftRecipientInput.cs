namespace TaxVision.Correspondence.Application.Compose;

/// <summary>
/// Un destinatario tal como lo manda el caller de <see cref="AutoSaveDraftCommand"/> — dirección
/// todavía sin validar (string crudo, la valida <see cref="AutoSaveDraftHandler"/> vía
/// <see cref="Domain.ValueObjects.EmailAddress.Create"/>). No confundir con
/// <see cref="Domain.Compose.DraftRecipientData"/>, que ya tiene la dirección validada — este tipo
/// es el que cruza el borde HTTP/Application, ese otro es el que cruza el borde Application/Domain.
/// </summary>
public sealed record AutoSaveDraftRecipientInput(string Address, string? DisplayName);
