using TaxVision.Subscription.Infrastructure.Scheduling;

namespace TaxVision.Subscription.Tests.Infrastructure;

public sealed class SubscriberMigrationJobTests
{
    // El aviso mínimo es de 30 días: revisar cada minuto solo gasta consultas y locks.
    [Fact]
    public void Runs_every_fifteen_minutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(15), SubscriberMigrationJob.Interval);
    }
}
