namespace TaxVision.Wallet.Infrastructure.Scheduling;

/// <summary>
/// Vencimiento de reservas abiertas (<c>Wallet:Reservations</c>). TTL por tipo de referencia según D-WAL-TTL:
/// 72 h para <c>campaign-run</c> y 1 h para <c>individual-sms</c>; cualquier otro tipo usa <see cref="DefaultOpenTtl"/>.
/// Encendido en appsettings por decisión del dueño (D-WAL-JOB, 2026-11-04); el default del código sigue apagado.
/// </summary>
public sealed class ReservationExpiryOptions
{
    public const string SectionName = "Wallet:Reservations";

    public bool ExpiryEnabled { get; set; }

    public Dictionary<string, TimeSpan> OpenTtl { get; set; } =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["campaign-run"] = TimeSpan.FromHours(72),
            ["individual-sms"] = TimeSpan.FromHours(1),
        };

    public TimeSpan DefaultOpenTtl { get; set; } = TimeSpan.FromHours(72);

    public TimeSpan ExpiryInterval { get; set; } = TimeSpan.FromMinutes(15);

    public int ExpiryBatchSize { get; set; } = 200;

    public bool IsValid() =>
        DefaultOpenTtl > TimeSpan.Zero
        && OpenTtl.Values.All(ttl => ttl > TimeSpan.Zero)
        && ExpiryInterval > TimeSpan.Zero
        && ExpiryBatchSize > 0;
}
