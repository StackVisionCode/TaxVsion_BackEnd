namespace TaxVision.Tasks.ClientForms.Application.Common.Abstractions;

public interface IClientFormsAvailabilityEvaluator
{
    Task<ClientFormsAvailability> EvaluateAsync(Guid tenantId, CancellationToken ct = default);
}
