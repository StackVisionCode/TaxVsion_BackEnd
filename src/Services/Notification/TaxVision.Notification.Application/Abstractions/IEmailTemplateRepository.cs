using TaxVision.Notification.Domain.Emailing;
using TaxVision.Notification.Domain.Emailing.Templates;

namespace TaxVision.Notification.Application.Abstractions;

public interface IEmailTemplateRepository
{
    Task AddAsync(EmailTemplate template, CancellationToken ct = default);

    Task AddVersionAsync(EmailTemplateVersion version, CancellationToken ct = default);

    /// <summary>Obtiene una plantilla restringida al scope visible: la del tenant o una System.</summary>
    Task<EmailTemplate?> GetByIdAsync(Guid id, Guid? tenantId, CancellationToken ct = default);

    Task<EmailTemplate?> GetByKeyAsync(
        EmailScope scope,
        Guid? tenantId,
        string templateKey,
        CancellationToken ct = default
    );

    Task<EmailTemplateVersion?> GetVersionAsync(Guid templateId, Guid versionId, CancellationToken ct = default);

    Task<int> GetNextVersionNumberAsync(Guid templateId, CancellationToken ct = default);

    Task<IReadOnlyList<EmailTemplate>> ListAsync(Guid? tenantId, bool includeSystem, CancellationToken ct = default);

    Task<IReadOnlyList<EmailTemplateVersion>> ListVersionsAsync(Guid templateId, CancellationToken ct = default);
}
