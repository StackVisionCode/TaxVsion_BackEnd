using BuildingBlocks.Web.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaxVision.BuildingBlocks.Testing.Messaging;
using Wolverine;
using Wolverine.Runtime;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Architecture;

/// <summary>
/// La regla reutilizable de BB-2 (<see cref="LocalQueueDurabilityRules"/>) contra hosts mínimos en memoria, sin
/// base ni transportes: los servicios que adopten BB-2 la heredan con su host de pruebas.
/// </summary>
public sealed class LocalQueueDurabilityTests
{
    [Fact]
    public async Task WithoutDurableLocalQueues_LocalRouteIsReported()
    {
        var offenders = await OffendersAsync(_ => { });

        Assert.Single(offenders);
        Assert.Contains(nameof(SampleLocalCommand), offenders[0]);
    }

    [Fact]
    public async Task WithDurableLocalQueues_NothingIsReported()
    {
        var offenders = await OffendersAsync(options => options.UseTaxVisionDurableLocalQueues());

        Assert.Empty(offenders);
    }

    [Fact]
    public async Task NamedQueueWithDurableInbox_IsAccepted()
    {
        var offenders = await OffendersAsync(options =>
            options.PublishMessage<SampleLocalCommand>().ToLocalQueue("sample-durable").UseDurableInbox()
        );

        Assert.Empty(offenders);
    }

    [Fact]
    public async Task InvokeOnlyMessage_IsExempt()
    {
        var offenders = await OffendersAsync(_ => { }, exempt: [typeof(SampleLocalCommand)]);

        Assert.Empty(offenders);
    }

    private static async Task<IReadOnlyList<string>> OffendersAsync(
        Action<WolverineOptions> configure,
        Type[]? exempt = null
    )
    {
        using var host = await Host.CreateDefaultBuilder()
            .UseWolverine(options =>
            {
                options.Discovery.DisableConventionalDiscovery().IncludeType(typeof(SampleLocalCommandHandler));
                configure(options);
            })
            .StartAsync();

        var offenders = LocalQueueDurabilityRules.FindNonDurableLocalRoutes(
            host.Services.GetRequiredService<IWolverineRuntime>(),
            exempt
        );
        await host.StopAsync();
        return offenders;
    }

    public sealed record SampleLocalCommand(Guid Id);

    public static class SampleLocalCommandHandler
    {
        public static void Handle(SampleLocalCommand command) { }
    }
}
