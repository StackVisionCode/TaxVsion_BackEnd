using BuildingBlocks.Messaging;
using JasperFx.CodeGeneration.Model;
using Microsoft.Extensions.Hosting;
using TaxVision.Growth.Api.IntegrationEvents;
using TaxVision.Growth.Api.Messaging;
using Wolverine;

namespace TaxVision.Growth.Tests.Messaging;

/// <summary>
/// Host mínimo en memoria con las políticas de Growth + las estándar: una regla de negocio fija no
/// gasta los reintentos de 1 s/5 s/15 s. Sin RabbitMQ ni SQL.
/// </summary>
public sealed class GrowthFailurePolicyTests
{
    [Fact]
    public async Task GrowthBusinessRuleException_MovesToErrorQueueWithoutRetry()
    {
        BusinessRuleProbeHandler.Reset();
        using var host = await Host.CreateDefaultBuilder()
            .UseWolverine(options =>
            {
                options.Discovery.DisableConventionalDiscovery().IncludeType(typeof(BusinessRuleProbeHandler));
                options.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;
                options.ApplyGrowthFailurePolicies();
                options.ApplyStandardFailurePolicies();
            })
            .StartAsync();

        await host.MessageBus().PublishAsync(new BusinessRuleProbe(Guid.NewGuid()));
        await BusinessRuleProbeHandler.FirstAttempt.Task.WaitAsync(TimeSpan.FromSeconds(10));
        // El primer cooldown estándar es 1 s: si hubiera reintento, ya habría un segundo intento.
        await Task.Delay(TimeSpan.FromSeconds(2.5));
        await host.StopAsync();

        Assert.Equal(1, BusinessRuleProbeHandler.Attempts);
    }
}

public sealed record BusinessRuleProbe(Guid Id);

public static class BusinessRuleProbeHandler
{
    private static int _attempts;

    public static int Attempts => Volatile.Read(ref _attempts);

    public static TaskCompletionSource FirstAttempt { get; private set; } = NewSignal();

    public static void Reset()
    {
        Interlocked.Exchange(ref _attempts, 0);
        FirstAttempt = NewSignal();
    }

    public static void Handle(BusinessRuleProbe message)
    {
        Interlocked.Increment(ref _attempts);
        FirstAttempt.TrySetResult();
        throw new GrowthBusinessRuleException("Codes.CommitReservation.LateCommitPendingReview", "probe");
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
