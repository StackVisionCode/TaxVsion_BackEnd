namespace TaxVision.Signature.Application.Sealing;

/// <summary>
/// Otro nodo tiene el candado de sellado de la solicitud. No se descarta el mensaje: Wolverine lo vuelve
/// a entregar con <see cref="Cooldowns"/> y, al agotarlos, va a la cola de errores. El job de
/// reconciliación de sellado cubre lo que quede. La política vive en <c>Program.cs</c>, antes de las estándar.
/// </summary>
public sealed class SealingLockBusyException(string message) : Exception(message)
{
    public static readonly TimeSpan[] Cooldowns =
    [
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(60),
    ];
}
