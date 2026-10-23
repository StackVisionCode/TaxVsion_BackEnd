using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Participants;

namespace TaxVision.Tasks.ClientForms.Application.Participants.Commands;

public static class GetFormIntakeLinkHandler
{
    public static async Task<Result<FormIntakeLinkResponse>> Handle(
        GetFormIntakeLinkQuery query,
        IClientFormsPublicCapabilityEvaluator capabilities,
        IFormIntakeLinkRepository links,
        CancellationToken ct
    )
    {
        if (!await capabilities.IsIntakeLinkAvailableAsync(query.TenantId, ct))
            return Result.Failure<FormIntakeLinkResponse>(FormParticipantErrors.IntakeLinkUnavailable);
        var link = await links.GetAsync(query.TenantId, query.LinkId, ct);
        return link is null
            ? Result.Failure<FormIntakeLinkResponse>(FormParticipantErrors.IntakeLinkNotFound)
            : Result.Success(FormIntakeLinkResponse.From(link));
    }
}
