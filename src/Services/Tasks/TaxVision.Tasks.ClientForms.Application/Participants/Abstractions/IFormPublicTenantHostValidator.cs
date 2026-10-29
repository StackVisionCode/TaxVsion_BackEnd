namespace TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;

public interface IFormPublicTenantHostValidator
{
    Task<bool> MatchesTenantAsync(Guid tenantId, string host, CancellationToken ct = default);
}
