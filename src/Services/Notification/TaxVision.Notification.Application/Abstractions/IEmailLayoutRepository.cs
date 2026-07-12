using TaxVision.Notification.Domain.Emailing;
using TaxVision.Notification.Domain.Emailing.Layouts;

namespace TaxVision.Notification.Application.Abstractions;

public interface IEmailLayoutRepository
{
    Task AddAsync(EmailLayout layout, CancellationToken ct = default);

    Task<EmailLayout?> GetByIdAsync(Guid id, Guid? tenantId, CancellationToken ct = default);

    Task<IReadOnlyList<EmailLayout>> ListAsync(Guid? tenantId, bool includeSystem, CancellationToken ct = default);

    /// <summary>Layout default efectivo: el del tenant y, si no hay, el global del SaaS.</summary>
    Task<EmailLayout?> GetDefaultAsync(Guid tenantId, CancellationToken ct = default);

    Task ClearDefaultsAsync(EmailScope scope, Guid? tenantId, CancellationToken ct = default);
}
