using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Consents;
using TaxVision.Tasks.ClientForms.Application.Consents.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;

namespace TaxVision.Tasks.ClientForms.Application.Participants.Commands;

public static class GetPublicDirectedFormAccessHandler
{
    public static async Task<Result<PublicDirectedFormAccessResponse>> Handle(
        GetPublicDirectedFormAccessQuery query,
        IFormAccessGrantRepository grants,
        IFormRequestRepository requests,
        IFormParticipantRepository participants,
        IFormExternalAccessSecurity security,
        IFormPublicTenantHostValidator hostValidator,
        IClientFormsPublicCapabilityEvaluator capabilities,
        IConsentNoticeRepository notices,
        CancellationToken ct
    )
    {
        var context = await PublicDirectedFormAccess.LoadAsync(
            query.AccessToken, query.RequestHost, query.NowUtc, grants, requests, participants,
            security, hostValidator, capabilities, ct
        );
        if (context is null)
            return Result.Failure<PublicDirectedFormAccessResponse>(PublicDirectedFormAccessErrors.NotFound);
        var entryNotice = await EntryNoticeSupport.ResolvePinnedAsync(
            context.Grant.TenantId,
            context.Grant.EntryNoticeId,
            context.Grant.EntryNoticeVersionNumber,
            notices,
            ct
        );
        if (entryNotice is null)
            return Result.Failure<PublicDirectedFormAccessResponse>(PublicDirectedFormAccessErrors.NotFound);
        return Result.Success(new PublicDirectedFormAccessResponse(
            context.Request.Title,
            query.RequestHost,
            MaskEmail(context.Participant.Email),
            EntryNoticeSupport.ToResponse(entryNotice)
        ));
    }

    private static string MaskEmail(string email)
    {
        var separator = email.IndexOf('@');
        return separator <= 0 ? "***" : $"{email[0]}***{email[separator..]}";
    }
}
