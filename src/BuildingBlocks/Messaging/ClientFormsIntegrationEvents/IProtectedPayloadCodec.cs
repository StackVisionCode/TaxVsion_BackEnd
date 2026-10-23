namespace BuildingBlocks.Messaging.ClientFormsIntegrationEvents;

public sealed record ProtectedPayloadBinding(
    Guid TenantId,
    Guid? FormRequestId,
    ClientFormParticipant? Participant,
    Guid? IntakeLinkId = null,
    Guid? ChallengeId = null
);

public interface IProtectedPayloadCodec
{
    ProtectedPayload Protect<T>(string schema, ProtectedPayloadBinding binding, T content);

    T Unprotect<T>(ProtectedPayload payload, string expectedSchema, ProtectedPayloadBinding expectedBinding);
}
