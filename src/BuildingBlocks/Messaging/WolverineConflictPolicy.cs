using BuildingBlocks.Results;
using Wolverine;
using Wolverine.ErrorHandling;

namespace BuildingBlocks.Messaging;

/// <summary>
/// BB-11. Regla de conflictos de la plataforma, opcional y aditiva: quien no la llama sigue igual.
/// Se llama antes de <see cref="WolverineFailurePolicies.ApplyStandardFailurePolicies(WolverineOptions)"/>, porque gana
/// la primera regla que matchea.
/// </summary>
public static class WolverineConflictPolicy
{
    public const string ConcurrencyConflictCode = "Persistence.ConcurrencyConflict";

    public static WolverineOptions UseTaxVisionConflictPolicy(this WolverineOptions options) =>
        options.UseTaxVisionConflictPolicy(new ConflictRetryOptions());

    /// <summary>
    /// Choque de <c>RowVersion</c>: por HTTP, 409 sin reintento; por mensaje, reintento programado y después cola de
    /// errores. Con la regla general, <c>InvokeAsync</c> reintenta en línea: el perdedor relee, pisa al otro y suelta lo
    /// publicado en el intento revertido. Solo una acción propia corta eso: en <c>InvokeAsync</c> relanza la excepción;
    /// <c>ScheduleRetry</c> no sirve porque <c>InvokeAsync</c> la salta y cae en la regla general.
    /// </summary>
    public static WolverineOptions UseTaxVisionConflictPolicy(this WolverineOptions options, ConflictRetryOptions retry)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(retry);
        var delays = retry.ResolveDelays();

        var rule = options
            .Policies.OnException<ConflictException>(conflict => conflict.Code == ConcurrencyConflictCode)
            .CustomAction(
                async (_, lifecycle, _) => await lifecycle.ReScheduleAsync(DateTimeOffset.UtcNow.Add(delays[0])),
                "Concurrency conflict: 409 over HTTP, scheduled retry for messages"
            );

        if (delays.Length > 1)
            rule = rule.Then.ScheduleRetry(delays[1..]);

        rule.Then.MoveToErrorQueue();

        return options;
    }
}
