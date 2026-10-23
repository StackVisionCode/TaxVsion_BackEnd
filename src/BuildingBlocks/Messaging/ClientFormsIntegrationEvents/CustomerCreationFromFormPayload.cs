namespace BuildingBlocks.Messaging.ClientFormsIntegrationEvents;

// Solo destinos de la operación CreateCustomer, ya normalizados por el mapeo de la versión fijada.
public sealed record CustomerCreationFromFormPayload(IReadOnlyList<ClientFormTargetValue> Values);
