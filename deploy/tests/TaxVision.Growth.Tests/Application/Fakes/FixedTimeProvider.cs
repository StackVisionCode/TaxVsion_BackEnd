namespace TaxVision.Growth.Tests.Application.Fakes;

internal sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    private DateTimeOffset _utcNow = utcNow.ToUniversalTime();

    public override DateTimeOffset GetUtcNow() => _utcNow;

    internal void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
}
