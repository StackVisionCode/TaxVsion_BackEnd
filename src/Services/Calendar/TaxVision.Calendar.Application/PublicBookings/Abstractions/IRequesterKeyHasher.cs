using TaxVision.Calendar.Domain.ValueObjects;

namespace TaxVision.Calendar.Application.PublicBookings.Abstractions;

/// <summary>
/// La llave del visitante en los índices: HMAC-SHA256 del email con una clave del servidor, en hex. Un SHA-256
/// sin clave se revierte con un diccionario de emails; este no.
/// </summary>
public interface IRequesterKeyHasher
{
    string KeyOf(WorkEmail email);
}
