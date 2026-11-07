using BuildingBlocks.Results;
using Wolverine;

namespace TaxVision.Postmaster.Application.Sending.Commands.RecordSendResult;

/// <summary>Intentos al registrar un resultado que choca con un aviso del proveedor (<c>Postmaster:RecordSendResultRetry</c>).</summary>
public sealed class RecordSendResultRetryOptions
{
    public const string SectionName = "Postmaster:RecordSendResultRetry";

    public int MaxAttempts { get; set; } = 3;
}

/// <summary>
/// El aviso del proveedor puede llegar antes de que se registre el resultado. Si el conflicto saliera, el comando de
/// envío se repetiría y cerraría como desconocido un correo ya aceptado. Cada invocación corre en un contexto nuevo y
/// relee. Se registra aunque se esté apagando el host: el proveedor ya contestó.
/// </summary>
public static class RecordSendResultRetry
{
    public const string ConcurrencyConflictCode = "Persistence.ConcurrencyConflict";

    public static async Task InvokeAsync(
        IMessageBus bus,
        RecordSendResultCommand command,
        RecordSendResultRetryOptions options
    )
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await bus.InvokeAsync(command, CancellationToken.None);
                return;
            }
            catch (ConflictException ex) when (ex.Code == ConcurrencyConflictCode && attempt < options.MaxAttempts) { }
        }
    }
}
