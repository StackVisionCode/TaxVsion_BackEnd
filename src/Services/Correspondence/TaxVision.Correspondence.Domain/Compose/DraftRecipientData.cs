using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Domain.ValueObjects;

namespace TaxVision.Correspondence.Domain.Compose;

/// <summary>
/// Datos de un destinatario tal como los arma el caller de <see cref="Draft.AutoSave"/> — mismo
/// rol que <see cref="IncomingEmailRecipientData"/> del lado del inbox: no es una entidad
/// persistida por sí misma, <see cref="Draft.AutoSave"/> la usa para construir los
/// <see cref="DraftRecipient"/> reales.
/// </summary>
public sealed record DraftRecipientData(EmailAddress Address, EmailRecipientType Type, string? DisplayName);
