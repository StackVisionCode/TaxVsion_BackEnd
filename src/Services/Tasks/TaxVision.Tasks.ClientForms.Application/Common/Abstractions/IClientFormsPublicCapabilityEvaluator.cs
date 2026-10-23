namespace TaxVision.Tasks.ClientForms.Application.Common.Abstractions;

public interface IClientFormsPublicCapabilityEvaluator
{
    Task<bool> IsExternalParticipantAvailableAsync(Guid tenantId, CancellationToken ct = default);
    Task<bool> IsIntakeLinkAvailableAsync(Guid tenantId, CancellationToken ct = default);
}
