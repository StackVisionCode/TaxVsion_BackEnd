using BuildingBlocks.Results;

namespace TaxVision.Notification.Application.Abstractions;

/// <summary>
/// SCR-11. Scribe rechazó el pedido con un error determinista (sin plantilla, sin versión publicada, plantilla
/// inválida): repetirlo da lo mismo. Deriva de <see cref="ArgumentException"/> para que la política estándar lo mande
/// directo a la cola de errores. Lo transitorio sigue en <see cref="ScribeRenderFailedException"/>.
/// </summary>
public sealed class ScribeRenderRejectedException(string eventKey, Error scribeError)
    : ArgumentException($"Scribe rejected the render for event '{eventKey}': {scribeError.Message}")
{
    public const string ErrorCode = "Scribe.RenderRejected";

    public string EventKey { get; } = eventKey;
    public Error ScribeError { get; } = scribeError;
}
