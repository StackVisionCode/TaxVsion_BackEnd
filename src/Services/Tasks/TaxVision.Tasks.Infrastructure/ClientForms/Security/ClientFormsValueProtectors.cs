using System.Text.Json;
using BuildingBlocks.Security;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.Infrastructure.ClientForms.Security;

internal sealed class AnswersProtector(IRotatingSecretProtector protector) : IAnswersProtector
{
    public ProtectedAnswers Protect(string json)
    {
        var value = protector.Protect(json);
        return new ProtectedAnswers(Pack(value), value.KeyVersion);
    }

    public string Unprotect(ProtectedAnswers value) => protector.Unprotect(Unpack(value.Cipher, value.KeyVersion));

    internal static string Pack(RotatingProtectedSecret value) => JsonSerializer.Serialize(
        new PackedSecret(
            Convert.ToBase64String(value.Ciphertext),
            Convert.ToBase64String(value.Nonce),
            Convert.ToBase64String(value.Tag)
        )
    );

    internal static RotatingProtectedSecret Unpack(string value, short keyVersion)
    {
        var packed = JsonSerializer.Deserialize<PackedSecret>(value)
            ?? throw new InvalidOperationException("The protected value is invalid.");
        return new RotatingProtectedSecret(
            Convert.FromBase64String(packed.Ciphertext),
            Convert.FromBase64String(packed.Nonce),
            Convert.FromBase64String(packed.Tag),
            keyVersion
        );
    }

    private sealed record PackedSecret(string Ciphertext, string Nonce, string Tag);
}

internal sealed class SensitiveValueProtector(IRotatingSecretProtector protector) : ISensitiveValueProtector
{
    public ProtectedSensitiveValue Protect(string normalized)
    {
        var value = protector.Protect(normalized);
        return new ProtectedSensitiveValue(AnswersProtector.Pack(value), value.KeyVersion);
    }

    public string Unprotect(ProtectedSensitiveValue value) =>
        protector.Unprotect(AnswersProtector.Unpack(value.Cipher, value.KeyVersion));
}
