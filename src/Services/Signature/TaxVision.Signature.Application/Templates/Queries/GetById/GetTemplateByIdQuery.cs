using TaxVision.Signature.Application.Abstractions;

namespace TaxVision.Signature.Application.Templates.Queries.GetById;

public sealed record GetTemplateByIdQuery(Guid TenantId, Guid TemplateId);

public static class GetTemplateByIdHandler
{
    public static async Task<SignatureTemplateResponse?> Handle(
        GetTemplateByIdQuery query,
        ISignatureTemplateRepository repository,
        CancellationToken ct
    )
    {
        var template = await repository.GetByIdAsync(query.TenantId, query.TemplateId, ct);
        return template is null ? null : SignatureTemplateResponse.From(template);
    }
}
