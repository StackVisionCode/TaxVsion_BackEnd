using BuildingBlocks.Messaging;
using BuildingBlocks.Results;

namespace TaxVision.Signature.Application.Requests.Public;

/// <summary>Intentos de una escritura pública que choca con otra (<c>Signature:PublicConflictRetry</c>).</summary>
public sealed class PublicSigningConflictRetryOptions
{
    public const string SectionName = "Signature:PublicConflictRetry";

    public int MaxAttempts { get; set; } = 3;
}

/// <summary>
/// Reintenta un comando público que perdió contra otra escritura de la misma solicitud. Cada invocación corre en un
/// contexto y una transacción nuevos: relee el agregado y lo del intento perdido se revierte con él. Agotados los
/// intentos, el <see cref="ConflictException"/> sale y el middleware responde 409.
/// </summary>
public static class PublicSigningConflictRetry
{
    public static async Task<T> InvokeAsync<T>(
        Func<CancellationToken, Task<T>> invoke,
        int maxAttempts,
        CancellationToken ct
    )
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await invoke(ct);
            }
            catch (ConflictException ex)
                when (attempt < maxAttempts && ex.Code == WolverineConflictPolicy.ConcurrencyConflictCode) { }
        }
    }
}
