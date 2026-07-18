using BuildingBlocks.Results;

namespace TaxVision.Notification.Application.Abstractions;

/// <summary>
/// Señala que <see cref="IScribeRenderClient.RenderAsync"/> devolvió un <see cref="Result{T}"/> de
/// falla — Scribe caído, lento, o el request rechazado. Deliberadamente una excepción (no un
/// <c>Result</c> propagado como valor) para que la política global de retry+cooldown de Wolverine
/// (<c>Program.cs</c>, <c>OnException&lt;Exception&gt;</c>) reintente el mensaje completo en vez de
/// que el consumer complete "con éxito" habiendo descartado el email (Hardening plan, Fase 7 — bug
/// real: <c>if (render.IsFailure) return;</c> perdía emails transaccionales en silencio cuando Scribe
/// fallaba en el momento exacto del evento). Ver <see cref="ScribeRenderResultExtensions.EnsureRendered"/>,
/// el único punto donde se lanza.
/// </summary>
public sealed class ScribeRenderFailedException(string eventKey, Error scribeError)
    : Exception($"Scribe render failed for event '{eventKey}': {scribeError.Message}")
{
    public string EventKey { get; } = eventKey;
    public Error ScribeError { get; } = scribeError;
}
