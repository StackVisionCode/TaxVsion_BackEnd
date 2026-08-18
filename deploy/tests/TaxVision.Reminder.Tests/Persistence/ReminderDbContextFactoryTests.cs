using TaxVision.Reminder.Infrastructure.Persistence;
using Xunit;

namespace TaxVision.Reminder.Tests.Persistence;

/// <summary>
/// El <c>ReminderDbContext</c> toma <c>ITenantContext</c> por constructor, así que
/// <c>dotnet ef</c> no puede instanciarlo solo: sin este factory las migraciones fallan con un
/// error críptico sobre el constructor. Es el mismo tropiezo que ya costó tiempo en Billing.
/// </summary>
public sealed class ReminderDbContextFactoryTests
{
    [Fact]
    public void CreateDbContext_BuildsContextWithoutTenant()
    {
        using var context = new ReminderDbContextFactory().CreateDbContext([]);

        Assert.NotNull(context.Model);
    }
}
