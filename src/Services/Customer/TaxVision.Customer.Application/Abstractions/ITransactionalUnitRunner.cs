namespace TaxVision.Customer.Application.Abstractions;

/// <summary>
/// Unidad transaccional fuera de un handler de Wolverine (BB-3): scope y transacción propios, y lo guardado y lo
/// publicado se confirman juntos o no queda nada. Si la unidad no confirma, relanza el error.
/// </summary>
public interface ITransactionalUnitRunner
{
    /// <param name="work">Trabajo de la unidad. Devuelve <c>false</c> si no había nada que hacer (no guarda).</param>
    /// <param name="ct">Cancelación.</param>
    Task RunAsync(Func<TransactionalUnit, CancellationToken, Task<bool>> work, CancellationToken ct);
}

/// <summary>Lo que ve el trabajo de la unidad: su scope (los repositorios comparten el DbContext) y su outbox.</summary>
public sealed class TransactionalUnit(IServiceProvider services, Func<object, ValueTask> publish)
{
    public IServiceProvider Services { get; } = services;

    /// <summary>Publica por el outbox de la unidad: sale solo si la unidad confirma.</summary>
    public ValueTask PublishAsync(object message) => publish(message);
}
