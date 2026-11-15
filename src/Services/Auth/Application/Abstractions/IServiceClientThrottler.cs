namespace TaxVision.Auth.Application.Abstractions;

/// <summary>
/// Freno por dirección para el grant M2M (<c>POST /auth/service-token</c>): cuenta los intentos con
/// credenciales de cliente inválidas. Es una defensa extra; el Gateway ya no deja llegar el grant desde fuera.
/// </summary>
public interface IServiceClientThrottler
{
    /// <summary>Espera restante si la dirección superó el umbral de fallos; null si puede intentar.</summary>
    Task<TimeSpan?> GetRetryAfterAsync(string? ipAddress, CancellationToken ct = default);

    Task RegisterFailureAsync(string? ipAddress, CancellationToken ct = default);
}
