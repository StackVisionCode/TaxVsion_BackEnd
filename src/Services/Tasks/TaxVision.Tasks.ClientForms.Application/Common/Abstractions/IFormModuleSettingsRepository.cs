using TaxVision.Tasks.ClientForms.Domain.Settings;

namespace TaxVision.Tasks.ClientForms.Application.Common.Abstractions;

public interface IFormModuleSettingsRepository
{
    Task<FormModuleSettings?> GetAsync(Guid tenantId, CancellationToken ct = default);
    Task<IReadOnlyList<FormModuleSettings>> ListAsync(CancellationToken ct = default);
    void Add(FormModuleSettings settings);
}
