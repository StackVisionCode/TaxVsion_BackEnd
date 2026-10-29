namespace BuildingBlocks.Messaging.ClientFormsIntegrationEvents;

// Destinos de la operación UpdateCustomerProfile, ya normalizados, más quién lo pidió (autenticado por el AEAD).
// El tax identifier viaja en claro SOLO aquí dentro, cifrado por el ProtectedPayload y atado a tenant+cliente+request.
public sealed record CustomerProfileUpdateFromFormPayload(
    Guid RequestedByUserId,
    string RequestedByActorType,
    IReadOnlyList<ClientFormTargetValue> Values
);
