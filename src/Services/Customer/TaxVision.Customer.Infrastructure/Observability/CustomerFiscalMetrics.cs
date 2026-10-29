using System.Diagnostics.Metrics;

namespace TaxVision.Customer.Infrastructure.Observability;

/// <summary>
/// Métricas del ciphertext fiscal de Customer (B-9/X-9). El Meter <see cref="MeterName"/> se declara como
/// meter adicional en Program.cs; sin eso no exporta nada. El censo por versión de clave lo refresca un job
/// periódico y lo leen el gauge y el health check. Sin tag de tenant, por cardinalidad.
/// </summary>
public sealed class CustomerFiscalMetrics : IDisposable
{
    public const string MeterName = "TaxVision.Customer.Fiscal";

    private readonly Meter _meter;
    private volatile IReadOnlyDictionary<short, long>? _ciphertextVersions;

    /// <param name="meterName">Solo los tests lo cambian, para no escuchar las mediciones de otra suite.</param>
    public CustomerFiscalMetrics(string? meterName = null)
    {
        _meter = new Meter(meterName ?? MeterName);
        CreateCiphertextGauge();
    }

    // Censo de ciphertext por versión de clave. Lo refresca un job periódico; lo lee el gauge y el health check.
    public void UpdateCiphertextVersions(IReadOnlyDictionary<short, long> versions) => _ciphertextVersions = versions;

    public IReadOnlyDictionary<short, long>? CiphertextVersions => _ciphertextVersions;

    public void Dispose() => _meter.Dispose();

    // Una medición por versión viva. Sin tag de tenant, por cardinalidad. Antes del primer censo no emite nada.
    private void CreateCiphertextGauge() =>
        _meter.CreateObservableGauge(
            "customer.fiscal_ciphertext_rows",
            () =>
                _ciphertextVersions is { } versions
                    ? versions.Select(entry => new Measurement<long>(
                        entry.Value,
                        new KeyValuePair<string, object?>("key_version", entry.Key)
                    ))
                    : [],
            description: "Encrypted fiscal rows by key version"
        );
}
