namespace BuildingBlocks.Messaging;

/// <summary>
/// BB-7. El mensaje llegó antes que su prerrequisito (la fila, la proyección o el estado que espera
/// todavía no existe). Lanzarla en vez de hacer <c>return</c>: la política estándar la reintenta con
/// esperas largas (<see cref="DependencyNotReadyRetryOptions"/>) y después la manda a la cola de errores.
/// Nunca se descarta en silencio.
/// </summary>
public class DependencyNotReadyException : Exception
{
    public DependencyNotReadyException(string dependency, string? detail = null, Exception? innerException = null)
        : base(BuildMessage(dependency, detail), innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dependency);
        Dependency = dependency;
    }

    /// <summary>Qué falta, para el log y la DLQ (por ejemplo <c>"CampaignRun"</c>).</summary>
    public string Dependency { get; }

    private static string BuildMessage(string dependency, string? detail) =>
        string.IsNullOrWhiteSpace(detail)
            ? $"Dependency '{dependency}' is not ready yet."
            : $"Dependency '{dependency}' is not ready yet: {detail}";
}
