using Microsoft.Extensions.Hosting;

namespace BuildingBlocks.Infrastructure.Hosting;

/// <summary>
/// El host genérico arranca los <see cref="IHostedService"/> en el orden exacto de su registro en
/// DI. Un <see cref="BackgroundService"/> que en su primer tick usa el bus de Wolverine (directo, o
/// indirecto vía un DbContext que auto-publica domain events en SaveChangesAsync) puede ganarle la
/// carrera al hosted service interno que arranca el transporte de Wolverine si se registró antes —
/// revienta con WolverineHasNotStartedException. <see cref="WaitForApplicationStartedAsync"/> se usa
/// como primera línea de ese <c>ExecuteAsync</c> para eliminar la carrera sin importar el orden de
/// registro en Program.cs.
/// </summary>
public static class HostApplicationLifetimeExtensions
{
    public static Task WaitForApplicationStartedAsync(
        this IHostApplicationLifetime lifetime,
        CancellationToken cancellationToken = default
    )
    {
        if (lifetime.ApplicationStarted.IsCancellationRequested)
            return Task.CompletedTask;

        var completionSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var startedRegistration = lifetime.ApplicationStarted.Register(() => completionSource.TrySetResult());
        var cancelRegistration = cancellationToken.Register(() => completionSource.TrySetCanceled(cancellationToken));

        return AwaitAndDisposeAsync(completionSource.Task, startedRegistration, cancelRegistration);
    }

    private static async Task AwaitAndDisposeAsync(
        Task task,
        CancellationTokenRegistration startedRegistration,
        CancellationTokenRegistration cancelRegistration
    )
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        finally
        {
            startedRegistration.Dispose();
            cancelRegistration.Dispose();
        }
    }
}
