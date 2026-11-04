using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Limits;
using TaxVision.Signature.Domain.Settings;

namespace TaxVision.Signature.Application.Documents;

/// <summary>Tamaño y páginas vigentes: lo que eligió la oficina, acotado a los techos del plan.</summary>
public sealed class TenantDocumentLimitsResolver(ISignatureLimitsResolver limits) : IDocumentLimitsResolver
{
    public async Task<DocumentLimits> ResolveAsync(Guid tenantId, CancellationToken ct = default)
    {
        var effective = (await limits.ResolveAsync(tenantId, ct)).ForRequests;
        var withSize = DocumentLimits.Default().WithMaxPdfBytes(effective.MaxPdfBytes);
        var withPages = withSize.Value.WithMaxPages(effective.MaxPagesPerDocument);
        return withPages.Value;
    }
}
