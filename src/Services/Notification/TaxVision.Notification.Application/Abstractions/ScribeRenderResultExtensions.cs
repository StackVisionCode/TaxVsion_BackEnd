using BuildingBlocks.Results;

namespace TaxVision.Notification.Application.Abstractions;

/// <summary>
/// Único punto de conversión entre el <see cref="Result{T}"/> que devuelve
/// <see cref="IScribeRenderClient.RenderAsync"/> (convención establecida del monorepo para fallas
/// esperadas en el borde del cliente HTTP) y el comportamiento que necesita todo consumer de
/// Wolverine: una falla de render NUNCA debe traducirse en un <c>return</c> silencioso que descarte
/// el email — eso es exactamente el bug que corrige la Fase 7 del Hardening plan. Todos los
/// consumers de Notification que renderizan vía Scribe deben llamar <see cref="EnsureRendered"/> en
/// vez de chequear <c>IsFailure</c> a mano, para que el fix sea estructural (no 12 variantes
/// manuales) y no reaparezca cuando alguien agregue el consumer #13 copiando un ejemplo viejo.
/// </summary>
public static class ScribeRenderResultExtensions
{
    /// <summary>
    /// Devuelve el email renderizado si Scribe respondió con éxito; si no, lanza
    /// <see cref="ScribeRenderFailedException"/> para que Wolverine reintente/DLQ el mensaje en vez
    /// de que el consumer complete silenciosamente sin haber enviado nada.
    /// </summary>
    public static ScribeRenderedEmail EnsureRendered(this Result<ScribeRenderedEmail> result, string eventKey) =>
        result.IsSuccess ? result.Value : throw new ScribeRenderFailedException(eventKey, result.Error);
}
