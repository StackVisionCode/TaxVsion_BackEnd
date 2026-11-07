using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;

namespace TaxVision.Notification.Tests.TestDoubles;

/// <summary>Codec para pruebas sin token protegido: si alguien lo usa, la prueba falla.</summary>
internal sealed class NoProtectedPayloadCodec : IProtectedPayloadCodec
{
    public ProtectedPayload Protect<T>(string schema, ProtectedPayloadBinding binding, T content) =>
        throw new InvalidOperationException("Unexpected protect call.");

    public T Unprotect<T>(ProtectedPayload payload, string expectedSchema, ProtectedPayloadBinding expectedBinding) =>
        throw new InvalidOperationException("Unexpected unprotect call.");
}
