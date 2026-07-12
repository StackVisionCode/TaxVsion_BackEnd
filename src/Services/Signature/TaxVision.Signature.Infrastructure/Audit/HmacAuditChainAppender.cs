using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BuildingBlocks.Results;
using BuildingBlocks.Security;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Audit;

namespace TaxVision.Signature.Infrastructure.Audit;

/// <summary>
/// Append-only HMAC chain sobre <see cref="SignatureAuditEvent"/>. Cada evento hashea:
/// <c>PreviousChainHash || Sequence || Kind || PayloadJson</c> con HMAC-SHA256 usando el
/// <c>TenantAuditSecret</c> descifrado. El primer evento usa <c>"GENESIS"</c> como hash
/// previo.
///
/// <para>Fases (métodos privados con nombre autoexplicativo):</para>
/// <list type="number">
///   <item>Leer las settings del tenant y descifrar el secret con <see cref="ISecretProtector"/>.</item>
///   <item>Obtener el tail actual (o crear inicial GENESIS/sequence 1).</item>
///   <item>Serializar el payload al JSON canónico (property names estables).</item>
///   <item>Computar HMAC y crear el <see cref="SignatureAuditEvent"/> vía dominio.</item>
///   <item>Persistir vía repositorio.</item>
/// </list>
/// </summary>
public sealed class HmacAuditChainAppender(
    ISignatureAuditRepository repository,
    ITenantSignatureSettingsRepository settingsRepository,
    ISecretProtector secretProtector
) : IAuditChainAppender
{
    private static readonly JsonSerializerOptions CanonicalJson = new()
    {
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public async Task<Result<SignatureAuditEvent>> AppendAsync(
        Guid tenantId,
        Guid signatureRequestId,
        SignatureAuditEventKind kind,
        DateTime occurredAtUtc,
        object payload,
        CancellationToken ct = default
    )
    {
        var secretResult = await LoadAuditSecretAsync(tenantId, ct);
        if (secretResult.IsFailure)
            return Result.Failure<SignatureAuditEvent>(secretResult.Error);

        var tail = await repository.GetTailAsync(tenantId, signatureRequestId, ct);
        var sequence = (tail?.LastSequence ?? 0) + 1;
        var previousHash = tail?.LastChainHash ?? SignatureAuditEvent.GenesisMarker;

        var payloadJson = SerializePayload(payload);
        var chainHash = ComputeHmac(secretResult.Value, previousHash, sequence, kind, payloadJson);

        var eventResult = SignatureAuditEvent.Create(
            tenantId,
            signatureRequestId,
            sequence,
            kind,
            occurredAtUtc,
            payloadJson,
            previousHash,
            chainHash
        );
        if (eventResult.IsFailure)
            return eventResult;

        await repository.AddAsync(eventResult.Value, ct);
        return eventResult;
    }

    // ------------------------------------------------------------------
    // Métodos privados: cada uno una única responsabilidad
    // ------------------------------------------------------------------

    private async Task<Result<byte[]>> LoadAuditSecretAsync(Guid tenantId, CancellationToken ct)
    {
        var settings = await settingsRepository.GetByTenantIdAsync(tenantId, ct);
        if (settings is null)
            return Result.Failure<byte[]>(
                new Error("Signature.Audit.Settings", "Tenant signature settings not found.")
            );

        var decrypted = secretProtector.Unprotect(settings.AuditSecretEncrypted);
        if (string.IsNullOrEmpty(decrypted))
            return Result.Failure<byte[]>(
                new Error("Signature.Audit.SecretDecrypt", "Audit secret could not be decrypted.")
            );

        try
        {
            return Result.Success(Convert.FromBase64String(decrypted));
        }
        catch (FormatException)
        {
            // El generador lo produce en base64; si no lo es, lo tratamos como bytes UTF-8.
            return Result.Success(Encoding.UTF8.GetBytes(decrypted));
        }
    }

    private static string SerializePayload(object payload) => JsonSerializer.Serialize(payload, CanonicalJson);

    private static string ComputeHmac(
        byte[] key,
        string previousChainHash,
        long sequence,
        SignatureAuditEventKind kind,
        string payloadJson
    )
    {
        var material = $"{previousChainHash}|{sequence}|{kind}|{payloadJson}";
        var mac = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(material));
        return Convert.ToHexString(mac).ToLowerInvariant();
    }
}
