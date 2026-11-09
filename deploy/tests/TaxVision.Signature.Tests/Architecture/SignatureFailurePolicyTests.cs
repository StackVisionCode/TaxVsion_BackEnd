using System.Reflection;
using BuildingBlocks.Messaging;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Signature.Application.Sealing;
using Wolverine;
using Wolverine.ErrorHandling;
using Wolverine.Runtime;
using Wolverine.Runtime.Handlers;

namespace TaxVision.Signature.Tests.Architecture;

/// <summary>
/// SIG-BB7: las esperas de "todavía no" del sellado siguen la política estándar de BB-7 (reintento programado,
/// sin retener el consumidor), evaluadas sobre las reglas reales del host como lo hace Wolverine.
/// </summary>
public sealed class SignatureFailurePolicyTests(SignatureRoutingHostFactory factory)
    : IClassFixture<SignatureRoutingHostFactory>
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public static TheoryData<string> NotReadyExceptions =>
        [nameof(SealingLockBusyException), nameof(SignatureImageNotReadyException)];

    [Theory]
    [MemberData(nameof(NotReadyExceptions))]
    public void NotReadyException_UsesTheDependencyNotReadyPolicy(string name)
    {
        Exception exception =
            name == nameof(SealingLockBusyException)
                ? new SealingLockBusyException("busy")
                : new SignatureImageNotReadyException("scanning");

        for (var attempt = 1; attempt <= 6; attempt++)
            Assert.Equal(
                Describe(Continuation(new DependencyNotReadyException("Probe"), attempt)),
                Describe(Continuation(exception, attempt))
            );
    }

    // HandlerGraph y DetermineExecutionContinuation son internos en Wolverine 6.14.
    private object Continuation(Exception exception, int attempt)
    {
        var options = factory.Services.GetRequiredService<IWolverineRuntime>().Options;
        var graph = (HandlerGraph)typeof(WolverineOptions).GetProperty("HandlerGraph", Any)!.GetValue(options)!;
        return typeof(FailureRuleCollection)
            .GetMethod("DetermineExecutionContinuation", Any)!
            .Invoke(graph.Failures, [exception, new Envelope { Attempts = attempt }])!;
    }

    private static string Describe(object continuation) =>
        continuation.GetType().Name
        + ":"
        + string.Join(
            ",",
            continuation
                .GetType()
                .GetProperties(Any)
                .Where(p => p.PropertyType == typeof(TimeSpan) || p.PropertyType == typeof(TimeSpan?))
                .Select(p => $"{p.Name}={p.GetValue(continuation)}")
        );
}
