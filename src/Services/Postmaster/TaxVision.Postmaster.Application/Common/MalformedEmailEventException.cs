using BuildingBlocks.Results;

namespace TaxVision.Postmaster.Application.Common;

/// <summary>
/// PMS-11. El evento no alcanza para armar un <c>SentMessage</c>: el reintento falla igual. Deriva de
/// <see cref="ArgumentException"/> para ir directo a la cola de errores.
/// </summary>
public sealed class MalformedEmailEventException(string eventName, Error error)
    : ArgumentException($"Malformed {eventName}: {error.Message}")
{
    public string ErrorCode { get; } = error.Code;
}
