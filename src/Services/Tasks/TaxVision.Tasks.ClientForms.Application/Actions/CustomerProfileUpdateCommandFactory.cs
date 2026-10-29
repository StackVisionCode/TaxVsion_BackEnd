using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using TaxVision.Tasks.ClientForms.Application.Requests;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.ClientForms.Application.Actions;

public static class CustomerProfileUpdateCommandFactory
{
    // El payload queda atado a tenant, request, participante y clave: copiado a otro comando no valida.
    public static CustomerProfileUpdateFromFormRequestedIntegrationEvent Create(
        FormRequest request,
        Guid commandId,
        string idempotencyKey,
        int targetCatalogVersion,
        CustomerProfileUpdateFromFormPayload payload,
        IProtectedPayloadCodec codec,
        string correlationId
    )
    {
        var participant = FormRequestIntegrationEventFactory.Participant(request);
        var binding = new ProtectedPayloadBinding(
            request.TenantId,
            request.Id,
            participant,
            IdempotencyKey: idempotencyKey
        );
        return new CustomerProfileUpdateFromFormRequestedIntegrationEvent
        {
            EventId = commandId,
            TenantId = request.TenantId,
            CorrelationId = correlationId,
            CommandId = commandId,
            IdempotencyKey = idempotencyKey,
            SourceService = CustomerCreationFromFormOutcomes.ClientFormsSourceService,
            SourceReferenceId = request.Id,
            RevisionNumber = request.CurrentRevisionNumber,
            TargetCatalogVersion = targetCatalogVersion,
            Participant = participant,
            Payload = codec.Protect(ClientFormProtectedPayloadSchemas.CustomerProfileUpdate, binding, payload),
        };
    }
}
