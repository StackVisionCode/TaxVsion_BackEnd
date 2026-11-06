namespace BuildingBlocks.Messaging;

/// <summary>
/// BB-7. Esperas entre reintentos de <see cref="DependencyNotReadyException"/>. Al agotarse, el
/// mensaje va a la cola de errores. Se puede enlazar desde configuración (<c>"00:00:05"</c>).
/// </summary>
public sealed class DependencyNotReadyRetryOptions
{
    public const string SectionName = "Messaging:DependencyNotReady";

    public static IReadOnlyList<TimeSpan> DefaultDelays { get; } =
    [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(10)];

    /// <summary>
    /// Nulo usa <see cref="DefaultDelays"/>. Sin valor inicial a propósito: el binder de
    /// configuración concatena sobre los arrays que ya traen elementos.
    /// </summary>
    public TimeSpan[]? Delays { get; set; }

    internal TimeSpan[] ResolveDelays()
    {
        var delays = Delays ?? [.. DefaultDelays];
        if (delays.Length == 0 || delays.Any(d => d <= TimeSpan.Zero))
            throw new ArgumentException(
                "DependencyNotReady delays must contain at least one positive value.",
                nameof(Delays)
            );
        return delays;
    }
}
