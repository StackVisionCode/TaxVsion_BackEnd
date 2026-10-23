namespace TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;

public interface IFormResponsibleUserValidator
{
    Task<bool> IsActiveAsync(Guid tenantId, Guid userId, CancellationToken ct = default);
}
