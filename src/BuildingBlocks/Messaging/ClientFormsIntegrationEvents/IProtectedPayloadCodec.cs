namespace BuildingBlocks.Messaging.ClientFormsIntegrationEvents;

// IdempotencyKey ata el contenido a una ejecución concreta: un payload copiado a otro comando no valida.
public sealed record ProtectedPayloadBinding(
    Guid TenantId,
    Guid? FormRequestId,
    ClientFormParticipant? Participant,
    Guid? IntakeLinkId = null,
    Guid? ChallengeId = null,
    string? IdempotencyKey = null
);

public interface IProtectedPayloadCodec
{
    ProtectedPayload Protect<T>(string schema, ProtectedPayloadBinding binding, T content);

    T Unprotect<T>(ProtectedPayload payload, string expectedSchema, ProtectedPayloadBinding expectedBinding);
}
