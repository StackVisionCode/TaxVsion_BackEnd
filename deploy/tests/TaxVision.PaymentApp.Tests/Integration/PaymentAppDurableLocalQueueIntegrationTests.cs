using BuildingBlocks.Results;
using BuildingBlocks.Web.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Application.HostedCheckout.Commands;
using TaxVision.PaymentApp.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.PaymentApp.Tests.Integration;

/// <summary>
/// PAY-BB2 contra SQL real: la expiración de la sesión del proveedor que se publicó y no se llegó a procesar sigue
/// ahí después de reiniciar el host. En el primer host el canceller se queda esperando y el host se apaga; en el
/// segundo, el canceller recibe una llamada. Esquema propio, borrado al terminar. No llama a ningún proveedor real.
/// </summary>
public sealed class PaymentAppDurableLocalQueueIntegrationTests : IAsyncLifetime
{
    private readonly string _schema = PaymentAppWolverineHost.NewSchema("paybb2");

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => PaymentAppWolverineHost.DropSchemaAsync(_schema);

    [Fact(Timeout = 180_000)]
    public async Task ExpireProviderCheckoutSession_survives_restart()
    {
        var command = new ExpireProviderCheckoutSessionCommand(
            Guid.NewGuid(),
            PaymentProviderCode.Stripe,
            $"cs_paybb2_{Guid.NewGuid():N}"
        );

        var firstRun = new RecordingCanceller(hold: true);
        using (var host = await StartHostAsync(firstRun))
        {
            await host.Services.GetRequiredService<IMessageBus>().PublishAsync(command);
            Assert.True(
                await firstRun.Attempted.Task.WaitAsync(TimeSpan.FromSeconds(30)) == command.ProviderSessionId,
                "El primer host no recibió el comando."
            );
            using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await host.StopAsync(stopTimeout.Token);
        }

        var secondRun = new RecordingCanceller(hold: false);
        using (var host = await StartHostAsync(secondRun))
        {
            var processed = await Task.WhenAny(secondRun.Completed.Task, Task.Delay(TimeSpan.FromSeconds(45)));
            await host.StopAsync();
            Assert.True(processed == secondRun.Completed.Task, "El comando se perdió con el reinicio.");
            Assert.Equal(1, secondRun.Calls);
        }
    }

    // Mismas rutas que Program.cs: la cola con nombre de PAY-1 y la política global de BB-2.
    private Task<IHost> StartHostAsync(RecordingCanceller canceller) =>
        PaymentAppWolverineHost.StartAsync(
            _schema,
            options =>
            {
                options.Discovery.IncludeType(typeof(ExpireProviderCheckoutSessionHandler));
                options.Services.AddSingleton<IHostedCheckoutSessionCanceller>(canceller);
                options.LocalQueue("payment-app-provider-calls").UseDurableInbox();
                options
                    .PublishMessage<ExpireProviderCheckoutSessionCommand>()
                    .ToLocalQueue("payment-app-provider-calls");
                options.UseTaxVisionDurableLocalQueues();
            }
        );

    private sealed class RecordingCanceller(bool hold) : IHostedCheckoutSessionCanceller
    {
        private int _calls;

        public int Calls => _calls;

        public TaskCompletionSource<string> Attempted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<string> Completed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<Result<CancelHostedCheckoutResult>> CancelAsync(
            PaymentProviderCode providerCode,
            string providerSessionId,
            CancellationToken ct
        )
        {
            Attempted.TrySetResult(providerSessionId);
            if (hold)
                await Task.Delay(Timeout.Infinite, CancellationToken.None);

            Interlocked.Increment(ref _calls);
            Completed.TrySetResult(providerSessionId);
            return Result.Success(new CancelHostedCheckoutResult(AlreadyExpired: false));
        }
    }
}
