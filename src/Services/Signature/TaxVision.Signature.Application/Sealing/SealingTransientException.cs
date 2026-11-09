namespace TaxVision.Signature.Application.Sealing;

/// <summary>
/// El sellado falló por algo que puede mejorar reintentando (CloudStorage o MinIO caídos, timeout, token
/// M2M ausente). Revierte la transacción del intento y Wolverine vuelve a entregar el mismo mensaje con
/// <see cref="Cooldowns"/>. En el último intento el consumidor ya no la lanza: avisa el fallo y marca la
/// solicitud. La política vive en <c>Program.cs</c>, antes de las estándar.
/// </summary>
public sealed class SealingTransientException(string message) : Exception(message)
{
    public static readonly TimeSpan[] Cooldowns =
    [
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(10),
    ];

    /// <summary>Envelope.Attempts empieza en 1: el primero más un reintento por pausa.</summary>
    public static int FinalAttempt => Cooldowns.Length + 1;
}
