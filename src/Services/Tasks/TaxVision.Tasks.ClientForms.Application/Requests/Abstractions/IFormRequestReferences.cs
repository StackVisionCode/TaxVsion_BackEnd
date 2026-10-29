namespace TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;

public interface IClientFormsCustomerReader
{
    Task<ClientFormsCustomerRecord?> GetAsync(Guid tenantId, Guid customerId, CancellationToken ct = default);
}

// Búsqueda por email en la proyección local de clientes: nunca llama a Customer en el camino caliente.
public interface IClientFormsCustomerEmailDirectory
{
    // Cliente no archivado del tenant con ese email ya normalizado; null si no hay.
    Task<Guid?> FindNonArchivedCustomerIdAsync(Guid tenantId, string normalizedEmail, CancellationToken ct = default);
}

public sealed record ClientFormsCustomerRecord(Guid CustomerId, bool IsActive, string DisplayName);

public interface IClientFormsTaskReader
{
    Task<bool> BelongsToCustomerAsync(Guid tenantId, Guid taskId, Guid customerId, CancellationToken ct = default);
}
