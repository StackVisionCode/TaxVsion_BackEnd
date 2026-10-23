namespace TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;

public interface IClientFormsCustomerReader
{
    Task<ClientFormsCustomerRecord?> GetAsync(Guid tenantId, Guid customerId, CancellationToken ct = default);
}

public sealed record ClientFormsCustomerRecord(Guid CustomerId, bool IsActive, string DisplayName);

public interface IClientFormsTaskReader
{
    Task<bool> BelongsToCustomerAsync(Guid tenantId, Guid taskId, Guid customerId, CancellationToken ct = default);
}
