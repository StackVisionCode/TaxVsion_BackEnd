using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using TaxVision.Auth.Application.Abstractions;

namespace TaxVision.Auth.Infrastructure.Security;

/// <summary>Codec compartido de BuildingBlocks; sin codec (clave sin configurar) no protege.</summary>
public sealed class AccountTokenProtector(IProtectedPayloadCodec? codec) : IAccountTokenProtector
{
    public ProtectedPayload? Protect(string schema, ProtectedPayloadBinding binding, string rawToken)
    {
        // Sin tenant la atadura no es válida: ese evento sale solo con RawToken.
        if (codec is null || binding.TenantId == Guid.Empty || string.IsNullOrEmpty(rawToken))
            return null;
        return codec.Protect(schema, binding, new AuthAccountTokenPayload(rawToken));
    }
}
