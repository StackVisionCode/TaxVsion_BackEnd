using BuildingBlocks.Results;

namespace TaxVision.Sms.Application.Webhooks;

/// <summary>
/// Reintenta una escritura que perdió el choque de <c>RowVersion</c>. Cada invocación corre en un contexto nuevo y relee
/// la fila. Es para lo que no se puede repetir después: un DLR que el proveedor no reenvía, o el resultado de un SMS ya
/// enviado. Agotados los intentos, el <see cref="ConflictException"/> sale.
/// </summary>
public static class SmsConflictRetry
{
    private const string ConcurrencyConflict = "Persistence.ConcurrencyConflict";

    public static async Task<T> InvokeAsync<T>(Func<Task<T>> invoke, int maxAttempts)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await invoke();
            }
            catch (ConflictException ex) when (attempt < maxAttempts && ex.Code == ConcurrencyConflict) { }
        }
    }
}
