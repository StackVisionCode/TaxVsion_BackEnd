using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Email.Sending.Campaign;

namespace TaxVision.Notification.Infrastructure.Email.Campaign;

/// <summary>
/// Throttle de ritmo pareja (singleton) para el email de campaña: reparte los envíos cada
/// <c>60s / MaxPerMinute</c>, reservando un "turno" bajo lock que es consistente entre handlers paralelos.
/// <c>MaxPerMinute &lt;= 0</c> ⇒ sin límite (no espera).
/// </summary>
public sealed class CampaignEmailThrottle : ICampaignEmailThrottle
{
    private readonly object _gate = new();
    private readonly TimeSpan _interval;
    private DateTime _nextSlotUtc = DateTime.MinValue;

    public CampaignEmailThrottle(IOptions<CampaignEmailOptions> options)
    {
        var perMinute = options.Value.MaxPerMinute;
        _interval = perMinute > 0 ? TimeSpan.FromSeconds(60.0 / perMinute) : TimeSpan.Zero;
    }

    public async Task WaitTurnAsync(CancellationToken ct = default)
    {
        if (_interval <= TimeSpan.Zero)
            return;

        TimeSpan delay;
        lock (_gate)
        {
            var now = DateTime.UtcNow;
            var slot = now > _nextSlotUtc ? now : _nextSlotUtc;
            _nextSlotUtc = slot + _interval;
            delay = slot - now;
        }

        if (delay > TimeSpan.Zero)
            await Task.Delay(delay, ct);
    }
}
