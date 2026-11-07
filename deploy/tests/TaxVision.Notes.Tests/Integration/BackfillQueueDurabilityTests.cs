extern alias BbTesting;

using BbTesting::TaxVision.BuildingBlocks.Testing.Messaging;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Notes.Application.Backfill;
using Wolverine.Runtime;
using Xunit;

namespace TaxVision.Notes.Tests.Integration;

/// <summary>NTS-8: el pedido de backfill sobrevive a un reinicio. Se mide sobre las rutas reales del host.</summary>
public sealed class BackfillQueueDurabilityTests(NotesApiFactory factory) : IClassFixture<NotesApiFactory>
{
    [Fact]
    public void BackfillCommand_GoesToADurableLocalQueue()
    {
        var offenders = LocalQueueDurabilityRules.FindNonDurableLocalRoutes(
            factory.Services.GetRequiredService<IWolverineRuntime>()
        );

        Assert.DoesNotContain(offenders, o => o.Contains(nameof(RunTenantCustomerBackfillCommand)));
    }
}
