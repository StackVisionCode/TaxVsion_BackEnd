using TaxVision.Correspondence.Domain.ValueObjects;

namespace TaxVision.Correspondence.Domain.Inbox;

/// <summary>
/// Datos de un destinatario tal como los arma el caller de <see cref="IncomingEmail.Create"/>
/// (el consumer de Fase 4, a partir del evento crudo de Connectors). No es una entidad
/// persistida por sí misma — <see cref="IncomingEmail.Create"/> la usa para construir los
/// <see cref="IncomingEmailRecipient"/> reales, con su propio <c>Id</c> e <c>IncomingEmailId</c>.
/// </summary>
public sealed record IncomingEmailRecipientData(EmailAddress Address, EmailRecipientType Type, string? DisplayName);
