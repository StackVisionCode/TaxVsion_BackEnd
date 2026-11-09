using BuildingBlocks.Messaging;
using BuildingBlocks.Results;

namespace TaxVision.CloudStorage.Application.Sharing;

/// <summary>Intentos de un acceso que choca con otro del mismo enlace (<c>CloudStorage:PublicShareConflictRetry</c>).</summary>
public sealed class PublicShareConflictRetryOptions
{
    public const string SectionName = "CloudStorage:PublicShareConflictRetry";

    public int MaxAttempts { get; set; } = 3;
}

/// <summary>
/// Reintenta un acceso, público o privado, que perdió contra otro del mismo enlace. Cada invocación corre en un contexto y una
/// transacción nuevos y relee el enlace: si ya se agotó o lo revocaron, el handler niega como siempre. Agotados los
/// intentos, el <see cref="ConflictException"/> sale y el middleware responde 409.
/// </summary>
public static class PublicShareConflictRetry
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
