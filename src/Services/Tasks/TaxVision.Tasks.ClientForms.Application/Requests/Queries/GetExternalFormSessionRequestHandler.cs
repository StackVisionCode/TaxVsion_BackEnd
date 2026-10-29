using BuildingBlocks.Results;
using TaxVision.DataCollection.Engine;
using TaxVision.Tasks.ClientForms.Application.Consents;
using TaxVision.Tasks.ClientForms.Application.Consents.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.ClientForms.Application.Requests.Queries;

public sealed record GetExternalFormSessionRequestQuery(ParticipantContext Context);

public static class GetExternalFormSessionRequestHandler
{
    public static async Task<Result<PortalFormRequestDetailResponse>> Handle(
        GetExternalFormSessionRequestQuery query,
        IFormRequestRepository requests,
        IFormTemplateVersionRepository versions,
        IAnswersProtector protector,
        IFormEngine engine,
        IConsentNoticeRepository notices,
        IFormConsentRecordRepository consentRecords,
        CancellationToken ct
    )
    {
        var found = await requests.GetForReviewAsync(
            query.Context.TenantId,
            query.Context.BoundRequestId ?? Guid.Empty,
            ct
        );
        if (found.IsFailure || !FormRequestApplicationSupport.Matches(query.Context, found.Value))
            return Result.Failure<PortalFormRequestDetailResponse>(FormRequestErrors.NotFound);
        var version = await versions.GetByIdAsync(query.Context.TenantId, found.Value.FormTemplateVersionId, ct);
        if (version.IsFailure)
            return Result.Failure<PortalFormRequestDetailResponse>(FormRequestErrors.TemplateNotPublished);
        var definition = engine.ParseDefinition(version.Value.Definition.Json);
        if (definition.IsFailure)
            return Result.Failure<PortalFormRequestDetailResponse>(FormRequestErrors.TemplateNotPublished);
        var consents = await FormConsentPresentationBuilder.BuildAsync(
            query.Context,
            found.Value.Id,
            definition.Value,
            notices,
            consentRecords,
            ct
        );

        return Result.Success(PortalFormRequestDetailMapper.Map(found.Value, version.Value, protector, consents));
    }
}
