using System.Security.Cryptography;
using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using Microsoft.Extensions.Options;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Security;

public sealed class JsonProtectedPayloadCodecTests
{
    [Fact]
    public void Round_trip_preserves_content_without_exposing_plaintext()
    {
        var codec = Codec(Key(), version: 3);
        var binding = Binding();

        var protectedPayload = codec.Protect("client-form-summary.v1", binding, new Content("Sensitive answer", 2026));
        var restored = codec.Unprotect<Content>(protectedPayload, "client-form-summary.v1", binding);

        Assert.Equal(new Content("Sensitive answer", 2026), restored);
        Assert.Equal(3, protectedPayload.KeyVersion);
        Assert.DoesNotContain("Sensitive answer", protectedPayload.Cipher, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_payload_when_binding_does_not_match_the_message_envelope()
    {
        var codec = Codec(Key(), version: 1);
        var binding = Binding();
        var protectedPayload = codec.Protect("external-access.v1", binding, new Content("secret", 1));
        var otherRequest = binding with { FormRequestId = Guid.NewGuid() };

        Assert.Throws<CryptographicException>(() =>
            codec.Unprotect<Content>(protectedPayload, "external-access.v1", otherRequest)
        );
    }

    [Fact]
    public void Rejects_wrong_schema_and_tampered_ciphertext()
    {
        var codec = Codec(Key(), version: 1);
        var binding = Binding();
        var protectedPayload = codec.Protect("external-access.v1", binding, new Content("secret", 1));

        Assert.Throws<CryptographicException>(() =>
            codec.Unprotect<Content>(protectedPayload, "external-code.v1", binding)
        );

        var tampered = protectedPayload with { Cipher = protectedPayload.Cipher[..^1] + "x" };
        Assert.Throws<CryptographicException>(() => codec.Unprotect<Content>(tampered, "external-access.v1", binding));
    }

    [Fact]
    public void Decrypts_previous_key_version_during_rotation()
    {
        var oldKey = Key();
        var binding = Binding();
        var oldPayload = Codec(oldKey, version: 1)
            .Protect("customer-profile-update.v1", binding, new Content("value", 1));
        var rotated = new JsonProtectedPayloadCodec(
            new AesGcmRotatingSecretProtector(
                Options.Create(
                    new RotatingSecretProtectionOptions
                    {
                        MasterKey = Convert.ToBase64String(Key()),
                        MasterKeyVersion = 2,
                        PreviousMasterKey = Convert.ToBase64String(oldKey),
                        PreviousMasterKeyVersion = 1,
                    }
                )
            )
        );

        var restored = rotated.Unprotect<Content>(oldPayload, "customer-profile-update.v1", binding);

        Assert.Equal(new Content("value", 1), restored);
    }

    [Fact]
    public void Round_trip_supports_intake_binding_before_a_request_exists()
    {
        var codec = Codec(Key(), version: 1);
        var binding = new ProtectedPayloadBinding(Guid.NewGuid(), null, null, Guid.NewGuid());

        var payload = codec.Protect("intake-code.v1", binding, new Content("secret", 1));
        var restored = codec.Unprotect<Content>(payload, "intake-code.v1", binding);

        Assert.Equal(new Content("secret", 1), restored);
    }

    [Fact]
    public void Rejects_schema_swapped_in_the_envelope_because_it_is_authenticated_inside_the_cipher()
    {
        var codec = Codec(Key(), version: 1);
        var binding = Binding();
        var protectedPayload = codec.Protect(
            ClientFormProtectedPayloadSchemas.AccessLink,
            binding,
            new Content("secret", 1)
        );
        var swapped = protectedPayload with { Schema = ClientFormProtectedPayloadSchemas.AccessCode };

        Assert.Throws<CryptographicException>(() =>
            codec.Unprotect<Content>(swapped, ClientFormProtectedPayloadSchemas.AccessCode, binding)
        );
    }

    [Fact]
    public void Rejects_swapped_challenge_participant_or_intake_link()
    {
        var codec = Codec(Key(), version: 1);
        var requestBinding = Binding() with { ChallengeId = Guid.NewGuid() };
        var requestPayload = codec.Protect("code.v1", requestBinding, new Content("123456", 1));
        var intakeBinding = new ProtectedPayloadBinding(Guid.NewGuid(), null, null, Guid.NewGuid(), Guid.NewGuid());
        var intakePayload = codec.Protect("code.v1", intakeBinding, new Content("123456", 1));

        Assert.Throws<CryptographicException>(() =>
            codec.Unprotect<Content>(requestPayload, "code.v1", requestBinding with { ChallengeId = Guid.NewGuid() })
        );
        Assert.Throws<CryptographicException>(() =>
            codec.Unprotect<Content>(requestPayload, "code.v1", requestBinding with { ChallengeId = null })
        );
        Assert.Throws<CryptographicException>(() =>
            codec.Unprotect<Content>(
                requestPayload,
                "code.v1",
                requestBinding with
                {
                    Participant = requestBinding.Participant! with { CustomerId = Guid.NewGuid() },
                }
            )
        );
        Assert.Throws<CryptographicException>(() =>
            codec.Unprotect<Content>(intakePayload, "code.v1", intakeBinding with { IntakeLinkId = Guid.NewGuid() })
        );
        Assert.Throws<CryptographicException>(() =>
            codec.Unprotect<Content>(intakePayload, "code.v1", intakeBinding with { TenantId = Guid.NewGuid() })
        );
        Assert.Equal(new Content("123456", 1), codec.Unprotect<Content>(requestPayload, "code.v1", requestBinding));
    }

    [Fact]
    public void Rejects_payload_moved_to_a_command_with_another_idempotency_key()
    {
        var codec = Codec(Key(), version: 1);
        var binding = Binding() with { IdempotencyKey = "request:r1:create_client" };
        var payload = codec.Protect(ClientFormProtectedPayloadSchemas.CustomerCreation, binding, new Content("x", 1));

        Assert.Throws<CryptographicException>(() =>
            codec.Unprotect<Content>(
                payload,
                ClientFormProtectedPayloadSchemas.CustomerCreation,
                binding with
                {
                    IdempotencyKey = "request:r2:create_client",
                }
            )
        );
        Assert.Throws<CryptographicException>(() =>
            codec.Unprotect<Content>(
                payload,
                ClientFormProtectedPayloadSchemas.CustomerCreation,
                binding with
                {
                    IdempotencyKey = null,
                }
            )
        );
        Assert.Throws<ArgumentException>(() =>
            codec.Protect("code.v1", binding with { IdempotencyKey = " " }, new Content("x", 1))
        );
        Assert.Equal(
            new Content("x", 1),
            codec.Unprotect<Content>(payload, ClientFormProtectedPayloadSchemas.CustomerCreation, binding)
        );
    }

    [Fact]
    public void Incoherent_binding_from_the_envelope_is_a_validation_failure_not_an_argument_error()
    {
        var codec = Codec(Key(), version: 1);
        var binding = Binding();
        var protectedPayload = codec.Protect("external-access.v1", binding, new Content("secret", 1));

        Assert.Throws<CryptographicException>(() =>
            codec.Unprotect<Content>(
                protectedPayload,
                "external-access.v1",
                binding with
                {
                    IntakeLinkId = Guid.NewGuid(),
                }
            )
        );
        Assert.Throws<ArgumentException>(() =>
            codec.Protect("external-access.v1", binding with { ChallengeId = Guid.Empty }, new Content("secret", 1))
        );
    }

    private static JsonProtectedPayloadCodec Codec(byte[] key, short version) =>
        new(
            new AesGcmRotatingSecretProtector(
                Options.Create(
                    new RotatingSecretProtectionOptions
                    {
                        MasterKey = Convert.ToBase64String(key),
                        MasterKeyVersion = version,
                    }
                )
            )
        );

    private static ProtectedPayloadBinding Binding() =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new ClientFormParticipant { Kind = "Customer", CustomerId = Guid.NewGuid() }
        );

    private static byte[] Key() => RandomNumberGenerator.GetBytes(32);

    private sealed record Content(string Value, int Year);
}
