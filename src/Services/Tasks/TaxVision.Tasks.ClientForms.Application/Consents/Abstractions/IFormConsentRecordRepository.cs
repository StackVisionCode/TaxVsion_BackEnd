using TaxVision.Tasks.ClientForms.Domain.Consents;

namespace TaxVision.Tasks.ClientForms.Application.Consents.Abstractions;

public interface IFormConsentRecordRepository
{
    Task<FormConsentRecord?> GetAsync(FormConsentRecordKey key, CancellationToken ct = default);
    Task<IReadOnlyList<FormConsentRecord>> ListForRequestAsync(
        Guid tenantId,
        Guid requestId,
        CancellationToken ct = default
    );
    void Add(FormConsentRecord record);

    // Suelta un registro que perdió la carrera contra el índice único, para no reintentarlo.
    void Discard(FormConsentRecord record);
}
