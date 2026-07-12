using System.Security.Cryptography;
using System.Text;
using BuildingBlocks.Security;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Audit;

namespace TaxVision.Signature.Infrastructure.Audit;

/// <summary>
/// Recomputa la HMAC-chain para cada evento en orden y compara contra el hash almacenado.
/// Encuentra el primer defecto (sequence roto, hash mismatch, chain-hash inicial no GENESIS)
/// y lo reporta.
/// </summary>
public sealed class HmacAuditChainVerifier(
    ITenantSignatureSettingsRepository settingsRepository,
    ISecretProtector secretProtector
) : IAuditChainVerifier
{
    public async Task<AuditChainVerification> VerifyAsync(
        Guid tenantId,
        Guid signatureRequestId,
        IReadOnlyList<SignatureAuditEvent> events,
        CancellationToken ct = default
    )
    {
        if (events.Count == 0)
            return new AuditChainVerification(true, 0, 0, null);

        var keyResult = await LoadKeyAsync(tenantId, ct);
        if (keyResult is null)
            return new AuditChainVerification(
                false,
                events.Count,
                events[^1].Sequence,
                new AuditChainDefect(events[0].Sequence, "Tenant audit secret is missing or unreadable.")
            );

        var expectedPrevious = SignatureAuditEvent.GenesisMarker;
        for (var i = 0; i < events.Count; i++)
        {
            var current = events[i];
            var defect = InspectEvent(current, i, expectedPrevious, keyResult);
            if (defect is not null)
                return new AuditChainVerification(false, events.Count, current.Sequence, defect);

            expectedPrevious = current.ChainHash;
        }
        return new AuditChainVerification(true, events.Count, events[^1].Sequence, null);
    }

    // ------------------------------------------------------------------
    // Métodos privados: cada uno una única responsabilidad
    // ------------------------------------------------------------------

    private async Task<byte[]?> LoadKeyAsync(Guid tenantId, CancellationToken ct)
    {
        var settings = await settingsRepository.GetByTenantIdAsync(tenantId, ct);
        if (settings is null)
            return null;
        var decrypted = secretProtector.Unprotect(settings.AuditSecretEncrypted);
        if (string.IsNullOrEmpty(decrypted))
            return null;
        try
        {
            return Convert.FromBase64String(decrypted);
        }
        catch (FormatException)
        {
            return Encoding.UTF8.GetBytes(decrypted);
        }
    }

    private static AuditChainDefect? InspectEvent(
        SignatureAuditEvent current,
        int zeroBasedIndex,
        string expectedPrevious,
        byte[] key
    )
    {
        var expectedSequence = zeroBasedIndex + 1L;
        if (current.Sequence != expectedSequence)
            return new AuditChainDefect(
                current.Sequence,
                $"Expected sequence {expectedSequence} but found {current.Sequence}."
            );
        if (current.PreviousChainHash != expectedPrevious)
            return new AuditChainDefect(current.Sequence, "PreviousChainHash does not match the previous event.");

        var recomputed = ComputeHmac(
            key,
            current.PreviousChainHash,
            current.Sequence,
            current.Kind,
            current.PayloadJson
        );
        if (!string.Equals(recomputed, current.ChainHash, StringComparison.OrdinalIgnoreCase))
            return new AuditChainDefect(current.Sequence, "ChainHash mismatch: payload or metadata has been tampered.");

        return null;
    }

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
