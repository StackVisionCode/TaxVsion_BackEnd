using BuildingBlocks.Results;

namespace TaxVision.Wallet.Application.Reservations;

/// <summary>Intentos de una reserva o liquidación que choca con otra del mismo monedero (<c>Wallet:ReservationConflictRetry</c>).</summary>
public sealed class ReservationConflictRetryOptions
{
    public const string SectionName = "Wallet:ReservationConflictRetry";

    public int MaxAttempts { get; set; } = 3;
}

/// <summary>
/// Reintenta por HTTP una reserva o liquidación que perdió contra otra del mismo monedero. Cada invocación corre
/// en un contexto nuevo y relee el saldo; la misma referencia cae en la rama idempotente. Agotados los intentos,
/// el <see cref="ConflictException"/> sale y el middleware responde 409.
/// </summary>
public static class ReservationConflictRetry
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
            catch (ConflictException ex) when (attempt < maxAttempts && IsRetryable(ex)) { }
        }
    }

    private static bool IsRetryable(ConflictException ex) =>
        ex.Code is "Persistence.ConcurrencyConflict" or "Persistence.UniqueConstraint";
}
