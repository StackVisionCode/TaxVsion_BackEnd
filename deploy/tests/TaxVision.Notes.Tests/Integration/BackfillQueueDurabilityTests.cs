extern alias BbTesting;

using BbTesting::TaxVision.BuildingBlocks.Testing.Messaging;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Notes.Application.Backfill;
using TaxVision.Notes.Application.Notes.Commands;
using Wolverine.Runtime;
using Xunit;

namespace TaxVision.Notes.Tests.Integration;

/// <summary>
/// NTS-8 y BB-2: lo que Notes manda o programa a un handler propio sobrevive a un reinicio. Se mide sobre las rutas
/// reales del host.
/// </summary>
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

    // La reconciliación del adjunto se programa a 30 s: en una cola en memoria se pierde si Notes reinicia.
    [Fact]
    public void ReconcileAttachmentCommand_GoesToADurableLocalQueue()
    {
        var offenders = LocalQueueDurabilityRules.FindNonDurableLocalRoutes(
            factory.Services.GetRequiredService<IWolverineRuntime>()
        );

        Assert.DoesNotContain(offenders, o => o.Contains(nameof(ReconcilePendingAttachmentCommand)));
    }

    [Fact]
    public void LocallyRoutedMessages_UseDurableQueues()
    {
        var offenders = LocalQueueDurabilityRules.FindNonDurableLocalRoutes(
            factory.Services.GetRequiredService<IWolverineRuntime>()
        );

        Assert.True(offenders.Count == 0, "Rutas locales en memoria:\n  " + string.Join("\n  ", offenders));
    }
}
