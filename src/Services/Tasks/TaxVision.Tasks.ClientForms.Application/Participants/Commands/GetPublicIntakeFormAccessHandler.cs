using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Consents;
using TaxVision.Tasks.ClientForms.Application.Consents.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;

namespace TaxVision.Tasks.ClientForms.Application.Participants.Commands;

public static class GetPublicIntakeFormAccessHandler
{
    public static async Task<Result<PublicIntakeFormAccessResponse>> Handle(
        GetPublicIntakeFormAccessQuery query,
        IFormIntakeLinkRepository links,
        IFormExternalAccessSecurity security,
        IFormPublicTenantHostValidator hosts,
        IClientFormsPublicCapabilityEvaluator capabilities,
        IFormTemplateVersionRepository versions,
        IConsentNoticeRepository notices,
        CancellationToken ct
    )
    {
        var link = await PublicIntakeFormAccess.LoadAsync(
            query.IntakeToken,
            query.RequestHost,
            query.NowUtc,
            links,
            security,
            hosts,
            capabilities,
            ct
        );
        if (link is null)
            return Result.Failure<PublicIntakeFormAccessResponse>(PublicIntakeFormErrors.NotFound);
        var version = await versions.GetByIdAsync(link.TenantId, link.FormTemplateVersionId, ct);
        var entryNotice = await EntryNoticeSupport.ResolvePinnedAsync(
            link.TenantId,
            link.EntryNoticeId,
            link.EntryNoticeVersionNumber,
            notices,
            ct
        );
        return version.IsFailure || entryNotice is null
            ? Result.Failure<PublicIntakeFormAccessResponse>(PublicIntakeFormErrors.NotFound)
            : Result.Success(
                new PublicIntakeFormAccessResponse(
                    version.Value.Title,
                    query.RequestHost,
                    EntryNoticeSupport.ToResponse(entryNotice)
                )
            );
    }
}
