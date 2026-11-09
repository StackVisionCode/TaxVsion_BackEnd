using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using TaxVision.Auth.Application.Abstractions;

namespace TaxVision.Auth.Tests.Application;

/// <summary>Protector sin clave: los eventos salen solo con RawToken, como en Auth sin Encryption:MasterKey.</summary>
internal sealed class NullAccountTokenProtector : IAccountTokenProtector
{
    public ProtectedPayload? Protect(string schema, ProtectedPayloadBinding binding, string rawToken) => null;
}
