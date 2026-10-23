using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Application.Consents.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests;

namespace TaxVision.Tasks.ClientForms.Application.Consents;

internal static class FormConsentRevisionBinder
{
    // Toda la evidencia pendiente del participante (aceptaciones, avisos y rechazos) queda en el envío.
    internal static async Task<Result> BindPendingAsync(
        ParticipantContext context,
        Guid requestId,
        int revisionNumber,
        IFormConsentRecordRepository records,
        CancellationToken ct
    )
    {
        var pending = (await records.ListForRequestAsync(context.TenantId, requestId, ct)).Where(item =>
            !item.RevisionNumber.HasValue && FormConsentGate.MatchesParticipant(context, item)
        );
        foreach (var record in pending)
        {
            var bound = record.BindToRevision(revisionNumber);
            if (bound.IsFailure)
                return bound;
        }
        return Result.Success();
    }
}
