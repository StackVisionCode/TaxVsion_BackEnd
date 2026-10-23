using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Participants;

namespace TaxVision.Tasks.ClientForms.Application.Participants.Commands;

public static class ListFormIntakeLinksHandler
{
    public static async Task<Result<IReadOnlyList<FormIntakeLinkResponse>>> Handle(
        ListFormIntakeLinksQuery query,
        IClientFormsPublicCapabilityEvaluator capabilities,
        IFormIntakeLinkRepository links,
        CancellationToken ct
    )
    {
        if (!await capabilities.IsIntakeLinkAvailableAsync(query.TenantId, ct))
            return Result.Failure<IReadOnlyList<FormIntakeLinkResponse>>(FormParticipantErrors.IntakeLinkUnavailable);
        var rows = await links.ListAsync(query.TenantId, ct);
        return Result.Success<IReadOnlyList<FormIntakeLinkResponse>>(
            rows.Select(FormIntakeLinkResponse.From).ToArray()
        );
    }
}
