using BuildingBlocks.Persistence;
using Wolverine;

namespace TaxVision.Connectors.Application.Abstractions;

/// <summary>
/// Transacción corta con outbox para los handlers <c>[NonTransactional]</c> (CON-03): lo que la acción cambia y
/// publica se confirma junto, y al volver ya está confirmado. Las llamadas al proveedor van antes o después, nunca adentro.
/// </summary>
public interface IOutboxUnit
{
    /// <param name="work">Publica por el bus que recibe. Devuelve <c>true</c> para confirmar; con <c>false</c> no queda nada.</param>
    Task RunAsync(Func<IMessageBus, CancellationToken, Task<bool>> work, CancellationToken ct = default);
}

/// <summary>Dentro de un handler transaccional la unidad es la del propio handler: su guardado y su bus.</summary>
public sealed class HandlerTransactionUnit(IUnitOfWork unitOfWork, IMessageBus bus) : IOutboxUnit
{
    public async Task RunAsync(Func<IMessageBus, CancellationToken, Task<bool>> work, CancellationToken ct = default)
    {
        if (await work(bus, ct))
            await unitOfWork.SaveChangesAsync(ct);
    }
}
