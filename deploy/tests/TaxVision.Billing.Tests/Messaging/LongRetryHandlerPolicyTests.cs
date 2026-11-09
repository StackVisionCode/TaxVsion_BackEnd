using System.Reflection;
using System.Text.Json;
using BuildingBlocks.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaxVision.Billing.Application.Invoices.EnsureInvoicePaymentLink;
using TaxVision.Billing.Application.Invoices.GenerateInvoicePdf;
using TaxVision.Billing.Application.Invoices.SyncInvoiceStock;
using TaxVision.Billing.Infrastructure.Messaging;
using Wolverine;
using Wolverine.ErrorHandling;
using Wolverine.Runtime;
using Wolverine.Runtime.Handlers;
using Xunit;

namespace TaxVision.Billing.Tests.Messaging;

/// <summary>
/// BIL-3b: el enlace de cobro y el sync de stock no van a la cola de errores antes del último reintento
/// largo; el resto de mensajes de Billing sigue con la política estándar. Evalúa las reglas de fallo que
/// Wolverine combina para cada cadena (las de la cadena primero, luego las globales).
/// </summary>
public sealed class LongRetryHandlerPolicyTests : IAsyncLifetime
{
    private IHost _host = null!;
    private HandlerGraph _graph = null!;

    public async Task InitializeAsync()
    {
        _host = await Host.CreateDefaultBuilder()
            .UseWolverine(options =>
            {
                options
                    .Discovery.DisableConventionalDiscovery()
                    .IncludeType(typeof(EnsureInvoicePaymentLinkHandler))
                    .IncludeType(typeof(SyncInvoiceStockHandler))
                    .IncludeType(typeof(GenerateInvoicePdfHandler));
                options.Durability.Mode = DurabilityMode.MediatorOnly;
                options.Policies.Add(LongRetryHandlerPolicy.ForSyncCommands());
                options.ApplyStandardFailurePolicies();
            })
            .StartAsync();
        // HandlerGraph es interno en WolverineOptions (6.14): se lee por reflexión solo para inspeccionar.
        var options = _host.Services.GetRequiredService<IWolverineRuntime>().Options;
        _graph = (HandlerGraph)
            typeof(WolverineOptions)
                .GetProperty("HandlerGraph", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
                .GetValue(options)!;
    }

    public async Task DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
    }

    [Theory]
    [InlineData(typeof(EnsureInvoicePaymentLinkCommand))]
    [InlineData(typeof(SyncInvoiceStockCommand))]
    public void TransientFailure_SchedulesEveryLongRetry_ThenErrorQueue(Type messageType)
    {
        for (var attempt = 1; attempt <= LongRetryHandlerPolicy.Cooldowns.Length; attempt++)
        {
            var continuation = Continuation(messageType, new InvalidOperationException("down"), attempt);
            Assert.Equal("ScheduledRetryContinuation", continuation.GetType().Name);
            Assert.Equal(LongRetryHandlerPolicy.Cooldowns[attempt - 1], Delay(continuation));
        }

        var last = Continuation(
            messageType,
            new InvalidOperationException("down"),
            LongRetryHandlerPolicy.Cooldowns.Length + 1
        );
        Assert.Equal("MoveToErrorQueue", last.GetType().Name);
    }

    [Theory]
    [InlineData(typeof(EnsureInvoicePaymentLinkCommand))]
    [InlineData(typeof(SyncInvoiceStockCommand))]
    public void PermanentFailure_GoesStraightToErrorQueue(Type messageType)
    {
        var continuation = Continuation(messageType, new JsonException("bad payload"), 1);

        Assert.Equal("MoveToErrorQueue", continuation.GetType().Name);
    }

    [Fact]
    public void OtherMessages_KeepTheStandardPolicy()
    {
        var first = Continuation(typeof(GenerateInvoicePdfCommand), new InvalidOperationException("down"), 1);
        var afterThree = Continuation(typeof(GenerateInvoicePdfCommand), new InvalidOperationException("down"), 4);

        Assert.Equal("RetryInlineContinuation", first.GetType().Name);
        Assert.Equal("MoveToErrorQueue", afterThree.GetType().Name);
    }

    // Igual que el Executor de Wolverine: reglas de la cadena combinadas con las globales (API interna en 6.14).
    private object Continuation(Type messageType, Exception exception, int attempt)
    {
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var chainRules = _graph.ChainFor(messageType).Failures;
        var combined = typeof(FailureRuleCollection).GetMethod("CombineRules", Any)!.Invoke(chainRules, [_graph.Failures])!;
        return typeof(FailureRuleCollection)
            .GetMethod("DetermineExecutionContinuation", Any)!
            .Invoke(combined, [exception, new Envelope { Attempts = attempt }])!;
    }

    private static TimeSpan? Delay(object continuation) =>
        (TimeSpan?)continuation.GetType().GetProperty("Delay")!.GetValue(continuation);
}
