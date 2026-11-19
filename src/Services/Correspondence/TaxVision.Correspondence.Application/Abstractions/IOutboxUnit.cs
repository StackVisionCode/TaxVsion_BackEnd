using Wolverine;

namespace TaxVision.Correspondence.Application.Abstractions;

/// <summary>
/// Transacción corta con outbox para lo que corre fuera de la transacción de Wolverine (envío y su reconciliación):
/// lo que se guarda y lo que se publica se confirman juntos. La llamada a Postmaster va antes, nunca adentro.
/// </summary>
public interface IOutboxUnit
{
    /// <param name="work">Publica por el bus que recibe. Devuelve <c>true</c> para confirmar; con <c>false</c> no queda nada.</param>
    Task RunAsync(Func<IMessageBus, CancellationToken, Task<bool>> work, CancellationToken ct = default);
}
