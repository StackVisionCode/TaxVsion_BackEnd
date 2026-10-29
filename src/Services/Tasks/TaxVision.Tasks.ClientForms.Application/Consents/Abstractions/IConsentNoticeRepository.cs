using TaxVision.Tasks.ClientForms.Domain.Consents;

namespace TaxVision.Tasks.ClientForms.Application.Consents.Abstractions;

public interface IConsentNoticeRepository
{
    Task<ConsentNotice?> GetAsync(Guid tenantId, Guid noticeId, CancellationToken ct = default);
    Task<ConsentNotice?> GetByKeyAsync(Guid tenantId, string key, CancellationToken ct = default);
    Task<ConsentNoticeVersion?> GetVersionAsync(
        Guid tenantId,
        Guid noticeId,
        int versionNumber,
        CancellationToken ct = default
    );
    Task<IReadOnlyList<ConsentNotice>> ListAsync(Guid tenantId, CancellationToken ct = default);
    void Add(ConsentNotice notice);
}
