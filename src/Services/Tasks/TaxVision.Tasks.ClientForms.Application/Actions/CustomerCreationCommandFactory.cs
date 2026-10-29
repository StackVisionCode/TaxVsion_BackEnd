using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using TaxVision.Tasks.ClientForms.Application.Requests;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.ClientForms.Application.Actions;

public static class CustomerCreationCommandFactory
{
    // El payload queda atado a tenant, request, participante y clave: copiado a otro comando no valida.
    public static CustomerCreationFromFormRequestedIntegrationEvent Create(
        FormRequest request,
        FormActionRun run,
        int targetCatalogVersion,
        CustomerCreationFromFormPayload payload,
        IProtectedPayloadCodec codec,
        string correlationId
    )
    {
        var participant = FormRequestIntegrationEventFactory.Participant(request);
        var binding = new ProtectedPayloadBinding(
            request.TenantId,
            request.Id,
            participant,
            IdempotencyKey: run.IdempotencyKey
        );
        return new CustomerCreationFromFormRequestedIntegrationEvent
        {
            EventId = run.CommandId!.Value,
            TenantId = request.TenantId,
            CorrelationId = correlationId,
            RequestId = run.CommandId.Value,
            IdempotencyKey = run.IdempotencyKey,
            SourceService = CustomerCreationFromFormOutcomes.ClientFormsSourceService,
            SourceReferenceId = request.Id,
            RevisionNumber = run.RevisionNumber,
            RequestedByUserId = request.ResponsibleUserId!.Value,
            TargetCatalogVersion = targetCatalogVersion,
            Participant = participant,
            Payload = codec.Protect(ClientFormProtectedPayloadSchemas.CustomerCreation, binding, payload),
        };
    }

    public static FormActionFailedIntegrationEvent Failed(
        FormRequest request,
        string actionType,
        string errorCode,
        string correlationId
    ) =>
        new()
        {
            TenantId = request.TenantId,
            CorrelationId = correlationId,
            FormRequestId = request.Id,
            ActionType = actionType,
            ErrorCode = errorCode,
            ResponsibleUserId = request.ResponsibleUserId ?? request.RequestedByUserId,
        };
}
