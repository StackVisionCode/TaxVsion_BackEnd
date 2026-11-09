namespace BuildingBlocks.Messaging;

/// <summary>
/// BB-11. Esperas entre reintentos programados de un conflicto de concurrencia que llegó por mensaje. Al
/// agotarse, el mensaje va a la cola de errores. Por HTTP no aplican: ahí no hay reintento.
/// </summary>
public sealed class ConflictRetryOptions
{
    public const string SectionName = "Messaging:ConcurrencyConflict";

    public static IReadOnlyList<TimeSpan> DefaultDelays { get; } =
    [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(10)];

    /// <summary>Nulo usa <see cref="DefaultDelays"/>. Sin valor inicial: el binder concatena sobre arrays con elementos.</summary>
    public TimeSpan[]? Delays { get; set; }

    internal TimeSpan[] ResolveDelays()
    {
        var delays = Delays ?? [.. DefaultDelays];
        if (delays.Length == 0 || delays.Any(d => d <= TimeSpan.Zero))
            throw new ArgumentException(
                "Concurrency conflict delays must contain at least one positive value.",
                nameof(Delays)
            );
        return delays;
    }
}
