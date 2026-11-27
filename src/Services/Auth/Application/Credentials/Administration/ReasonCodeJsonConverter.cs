using System.Text.Json;
using System.Text.Json.Serialization;
using TaxVision.Auth.Domain.Credentials;

namespace TaxVision.Auth.Application.Credentials.Administration;

/// <summary>
/// Lee el motivo sin romper el binding: un nombre o número desconocido llega como valor indefinido y el dominio
/// responde <c>Auth.CredentialAdmin.InvalidReason</c> (03 §15) en vez del 400 genérico de ASP.NET.
/// </summary>
public sealed class ReasonCodeJsonConverter : JsonConverter<CredentialAdministrationReasonCode>
{
    private const CredentialAdministrationReasonCode Unknown = 0;

    public override CredentialAdministrationReasonCode Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                var name = reader.GetString();
                foreach (var code in Enum.GetValues<CredentialAdministrationReasonCode>())
                    if (string.Equals(code.ToString(), name, StringComparison.OrdinalIgnoreCase))
                        return code;
                return Unknown;
            case JsonTokenType.Number:
                return reader.TryGetByte(out var value) ? (CredentialAdministrationReasonCode)value : Unknown;
            default:
                reader.Skip();
                return Unknown;
        }
    }

    public override void Write(
        Utf8JsonWriter writer,
        CredentialAdministrationReasonCode value,
        JsonSerializerOptions options
    ) => writer.WriteStringValue(value.ToString());
}
