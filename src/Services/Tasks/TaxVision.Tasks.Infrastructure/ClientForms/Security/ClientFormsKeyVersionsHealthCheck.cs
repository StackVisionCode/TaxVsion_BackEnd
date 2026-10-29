using BuildingBlocks.Infrastructure.Security;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using TaxVision.Tasks.Infrastructure.Observability;

namespace TaxVision.Tasks.Infrastructure.ClientForms.Security;

/// <summary>
/// Semáforo de rotación (X-9.3). Lee el censo que mantiene <see cref="ClientFormsKeyVersionJob"/>:
/// Healthy si todo está en la versión actual; Degraded mientras quede ciphertext en otra versión;
/// Unhealthy si hay filas en una versión que ya no es la actual ni la anterior (clave retirada → ilegible).
/// No lleva tag "ready": la rotación no debe tumbar el servicio, solo frenar la siguiente rotación.
/// </summary>
public sealed class ClientFormsKeyVersionsHealthCheck(
    ClientFormsMetrics metrics,
    IOptions<RotatingSecretProtectionOptions> options
) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        var snapshot = metrics.CiphertextVersions;
        if (snapshot is null)
            return Task.FromResult(HealthCheckResult.Healthy("Key version census not taken yet."));

        var config = options.Value;
        var current = config.MasterKeyVersion;
        var previous = config.PreviousMasterKeyVersion;

        var live = snapshot.Where(entry => entry.Value > 0).ToList();

        var retired = live.Where(entry => entry.Key != current && entry.Key != previous)
            .Select(entry => entry.Key)
            .ToList();
        if (retired.Count > 0)
            return Task.FromResult(
                HealthCheckResult.Unhealthy(
                    $"Ciphertext on retired key version(s) {string.Join(",", retired)}; current is {current}."
                )
            );

        var stale = live.Where(entry => entry.Key != current).Sum(entry => entry.Value);
        if (stale > 0)
            return Task.FromResult(
                HealthCheckResult.Degraded($"{stale} row(s) not yet re-encrypted to key version {current}.")
            );

        return Task.FromResult(HealthCheckResult.Healthy($"All ciphertext on key version {current}."));
    }
}
