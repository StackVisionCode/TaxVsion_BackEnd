using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Participants;

namespace TaxVision.Tasks.ClientForms.Application.Participants.Commands;

// Igual que la lista: se lee con el flag apagado. El token sigue exigiéndolo (GetFormIntakeLinkTokenHandler).
public static class GetFormIntakeLinkHandler
{
    public static async Task<Result<FormIntakeLinkResponse>> Handle(
        GetFormIntakeLinkQuery query,
        IFormIntakeLinkRepository links,
        CancellationToken ct
    )
    {
        var link = await links.GetAsync(query.TenantId, query.LinkId, ct);
        return link is null
            ? Result.Failure<FormIntakeLinkResponse>(FormParticipantErrors.IntakeLinkNotFound)
            : Result.Success(FormIntakeLinkResponse.From(link));
    }
}
