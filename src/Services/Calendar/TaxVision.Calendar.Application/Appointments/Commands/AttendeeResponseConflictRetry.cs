using BuildingBlocks.Messaging;
using BuildingBlocks.Results;

namespace TaxVision.Calendar.Application.Appointments.Commands;

/// <summary>Intentos de un RSVP que choca con otra escritura de la cita (<c>Calendar:AttendeeResponseConflictRetry</c>).</summary>
public sealed class AttendeeResponseConflictRetryOptions
{
    public const string SectionName = "Calendar:AttendeeResponseConflictRetry";

    public int MaxAttempts { get; set; } = 3;
}

/// <summary>
/// Reintenta un RSVP que perdió contra otra escritura de la misma cita. Cada invocación corre en un contexto y una
/// transacción nuevos y relee la cita: contra otro RSVP guarda, contra un cambio de hora lo rechaza el handler.
/// Agotados los intentos, el <see cref="ConflictException"/> sale y el middleware responde 409.
/// </summary>
public static class AttendeeResponseConflictRetry
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
