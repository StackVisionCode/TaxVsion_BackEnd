using TaxVision.Signature.Application.Templates.Queries.List;

namespace TaxVision.Signature.Application.Abstractions;

public interface ISignatureTemplateReadService
{
    Task<ListTemplatesResult> ListAsync(ListTemplatesQuery query, CancellationToken ct = default);
}
