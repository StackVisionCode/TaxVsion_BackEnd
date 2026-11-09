using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.ClientForms.Application.Actions.Consumers;

/// <summary>
/// P-18 — apoyo compartido de los consumers de resultados de Signature: autofiltra por ExternalReference
/// (solo las firmas originadas en un formulario) y resuelve la revisión por el SourceId "{requestId}:r{n}".
/// </summary>
internal static class FormSignatureResultSupport
{
    internal static bool TryResolve(SignatureExternalReference? reference, out Guid requestId, out int revision)
    {
        requestId = Guid.Empty;
        revision = 0;
        return reference is not null
            && reference.SourceService == ClientFormSignatureReference.SourceService
            && ClientFormSignatureReference.TryParse(reference.SourceId, out requestId, out revision);
    }

    // TSK-4: true si no hay revisión. Una transición que el estado no admite sigue su curso, pero queda en el log.
    internal static bool Failed(Result<bool> transition, string name, Guid requestId, int revision, ILogger logger)
    {
        if (transition.IsFailure)
            return true;
        if (!transition.Value)
            logger.LogInformation(
                "Signature transition {Transition} ignored on form request {RequestId}, revision {Revision}.",
                name,
                requestId,
                revision
            );
        return false;
    }

    internal static FormActionFailedIntegrationEvent BuildNotice(
        FormRequest request,
        string errorCode,
        string correlationId
    ) =>
        new()
        {
            TenantId = request.TenantId,
            CorrelationId = correlationId,
            FormRequestId = request.Id,
            ActionType = "RequestSignature",
            ErrorCode = errorCode,
            ResponsibleUserId = request.ResponsibleUserId ?? request.RequestedByUserId,
        };
}
