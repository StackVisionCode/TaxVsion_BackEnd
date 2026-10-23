using System.Security.Cryptography;
using System.Text.Json;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using BuildingBlocks.Security;

namespace BuildingBlocks.Infrastructure.Security;

public sealed class JsonProtectedPayloadCodec(IRotatingSecretProtector protector) : IProtectedPayloadCodec
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public ProtectedPayload Protect<T>(string schema, ProtectedPayloadBinding binding, T content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);
        ArgumentNullException.ThrowIfNull(binding);
        if (!IsValid(binding))
            throw new ArgumentException("The protected payload binding is invalid.", nameof(binding));
        ArgumentNullException.ThrowIfNull(content);

        // El schema y el binding viajan dentro del texto cifrado: GCM los autentica junto con el contenido.
        var plaintext = JsonSerializer.Serialize(new PayloadEnvelope<T>(schema, binding, content), JsonOptions);
        var protectedValue = protector.Protect(plaintext);
        return new ProtectedPayload
        {
            Cipher = Pack(protectedValue),
            KeyVersion = protectedValue.KeyVersion,
            Schema = schema,
        };
    }

    public T Unprotect<T>(ProtectedPayload payload, string expectedSchema, ProtectedPayloadBinding expectedBinding)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedSchema);
        ArgumentNullException.ThrowIfNull(expectedBinding);

        // El binding esperado sale del sobre del mensaje: si es incoherente, es manipulación, no un bug local.
        if (!IsValid(expectedBinding) || !string.Equals(payload.Schema, expectedSchema, StringComparison.Ordinal))
            throw new CryptographicException("Protected payload validation failed.");

        try
        {
            var plaintext = protector.Unprotect(Unpack(payload.Cipher, payload.KeyVersion));
            var envelope =
                JsonSerializer.Deserialize<PayloadEnvelope<T>>(plaintext, JsonOptions)
                ?? throw new CryptographicException("Protected payload validation failed.");
            if (
                !string.Equals(envelope.Schema, expectedSchema, StringComparison.Ordinal)
                || envelope.Binding != expectedBinding
            )
                throw new CryptographicException("Protected payload validation failed.");
            return envelope.Content;
        }
        catch (Exception exception)
            when (exception is FormatException or JsonException or OverflowException or ArgumentException)
        {
            throw new CryptographicException("Protected payload validation failed.", exception);
        }
    }

    private static bool IsValid(ProtectedPayloadBinding binding)
    {
        var requestBound =
            binding.FormRequestId is { } requestId
            && requestId != Guid.Empty
            && binding.Participant is not null
            && binding.IntakeLinkId is null;
        var intakeBound =
            binding.IntakeLinkId is { } intakeLinkId
            && intakeLinkId != Guid.Empty
            && binding.FormRequestId is null
            && binding.Participant is null;
        return binding.TenantId != Guid.Empty && requestBound != intakeBound && binding.ChallengeId != Guid.Empty;
    }

    private static string Pack(RotatingProtectedSecret value) =>
        JsonSerializer.Serialize(
            new PackedSecret(
                Convert.ToBase64String(value.Ciphertext),
                Convert.ToBase64String(value.Nonce),
                Convert.ToBase64String(value.Tag)
            ),
            JsonOptions
        );

    private static RotatingProtectedSecret Unpack(string cipher, int keyVersion)
    {
        if (keyVersion is < short.MinValue or > short.MaxValue)
            throw new OverflowException("The key version is invalid.");
        var packed =
            JsonSerializer.Deserialize<PackedSecret>(cipher, JsonOptions)
            ?? throw new CryptographicException("Protected payload validation failed.");
        return new RotatingProtectedSecret(
            Convert.FromBase64String(packed.Ciphertext),
            Convert.FromBase64String(packed.Nonce),
            Convert.FromBase64String(packed.Tag),
            (short)keyVersion
        );
    }

    private sealed record PayloadEnvelope<T>(string Schema, ProtectedPayloadBinding Binding, T Content);

    private sealed record PackedSecret(string Ciphertext, string Nonce, string Tag);
}
