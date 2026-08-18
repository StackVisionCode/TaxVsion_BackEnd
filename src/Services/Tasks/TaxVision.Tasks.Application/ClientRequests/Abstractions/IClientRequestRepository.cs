using BuildingBlocks.Results;
using TaxVision.Tasks.Domain.ClientRequests;

namespace TaxVision.Tasks.Application.ClientRequests.Abstractions;

public interface IClientRequestRepository
{
    void Add(ClientRequest request);

    Task<Result<ClientRequest>> GetByIdAsync(Guid tenantId, Guid requestId, CancellationToken ct = default);

    /// <summary>
    /// Lo del cliente y sólo lo suyo: el <c>customerId</c> sale del token, nunca de la petición.
    /// </summary>
    Task<IReadOnlyList<ClientRequest>> ListForCustomerAsync(
        Guid tenantId,
        Guid customerId,
        bool onlyOpen,
        CancellationToken ct = default
    );

    Task<IReadOnlyList<ClientRequest>> ListForTaskAsync(Guid tenantId, Guid taskId, CancellationToken ct = default);

    /// <summary>
    /// Sin tenant: el consumer del escaneo no corre en un scope HTTP y sólo trae el <c>fileId</c>.
    /// Quien llame compara el tenant del evento contra el dueño real antes de mutar.
    /// </summary>
    Task<ClientRequest?> GetByDocumentFileIdAsync(Guid fileId, CancellationToken ct = default);
}
