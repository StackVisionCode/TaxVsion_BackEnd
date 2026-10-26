using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Participants;

namespace TaxVision.Tasks.ClientForms.Application.Participants.Commands;

// Sin el flag IntakeLinksEnabled se sigue listando: la UI necesita ver los enlaces para pausarlos o revocarlos.
public static class ListFormIntakeLinksHandler
{
    public static async Task<Result<IReadOnlyList<FormIntakeLinkResponse>>> Handle(
        ListFormIntakeLinksQuery query,
        IFormIntakeLinkRepository links,
        CancellationToken ct
    )
    {
        var rows = await links.ListAsync(query.TenantId, ct);
        return Result.Success<IReadOnlyList<FormIntakeLinkResponse>>(
            rows.Select(FormIntakeLinkResponse.From).ToArray()
        );
    }
}
