using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Signature.Domain.Audit;

/// <summary>
/// Evento append-only de la cadena de audit para una <c>SignatureRequest</c> concreta.
/// Cada evento lleva:
/// <list type="bullet">
///   <item><see cref="Sequence"/>: número monotónico 1..N por solicitud.</item>
///   <item><see cref="PayloadJson"/>: snapshot canónico JSON del evento (input del HMAC).</item>
///   <item><see cref="PreviousChainHash"/>: HMAC anterior (o <c>"GENESIS"</c> para el primero).</item>
///   <item><see cref="ChainHash"/>: HMAC-SHA256(TenantAuditSecret, PreviousChainHash || Sequence || Kind || PayloadJson).</item>
/// </list>
///
/// <para>
/// El HMAC se computa afuera (Application/Infrastructure con la <c>TenantAuditSecret</c>
/// descifrada), y aquí se recibe ya calculado. El dominio no toca crypto pero garantiza
/// las invariantes del formato (sequence &gt;= 1, chainHash 64 hex, immutability).
/// </para>
///
/// <para>
/// Regla 6B.5–6B.7 del diseño: la cadena es tamper-evident. Modificar cualquier fila
/// (o borrarla) rompe la cadena en la fila siguiente. El verificador público lo
/// detecta re-computando el HMAC de cada fila y comparando.
/// </para>
/// </summary>
public sealed class SignatureAuditEvent : TenantEntity
{
    public const string GenesisMarker = "GENESIS";
    public const int ChainHashLength = 64;
    public const int MaxPayloadJsonLength = 8192;

    private SignatureAuditEvent() { }

    public Guid SignatureRequestId { get; private set; }
    public long Sequence { get; private set; }
    public SignatureAuditEventKind Kind { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }
    public string PayloadJson { get; private set; } = default!;
    public string PreviousChainHash { get; private set; } = default!;
    public string ChainHash { get; private set; } = default!;

    public static Result<SignatureAuditEvent> Create(
        Guid tenantId,
        Guid signatureRequestId,
        long sequence,
        SignatureAuditEventKind kind,
        DateTime occurredAtUtc,
        string payloadJson,
        string previousChainHash,
        string chainHash
    )
    {
        var validation = ValidateFactoryInputs(
            tenantId,
            signatureRequestId,
            sequence,
            payloadJson,
            previousChainHash,
            chainHash
        );
        if (validation.IsFailure)
            return Result.Failure<SignatureAuditEvent>(validation.Error);

        var evt = new SignatureAuditEvent
        {
            Id = Guid.NewGuid(),
            SignatureRequestId = signatureRequestId,
            Sequence = sequence,
            Kind = kind,
            OccurredAtUtc = occurredAtUtc,
            PayloadJson = payloadJson,
            PreviousChainHash = previousChainHash,
            ChainHash = chainHash.ToLowerInvariant(),
        };
        evt.SetTenant(tenantId);
        return Result.Success(evt);
    }

    // ------------------------------------------------------------------
    // Helpers privados: cada regla en su método
    // ------------------------------------------------------------------

    private static Result ValidateFactoryInputs(
        Guid tenantId,
        Guid signatureRequestId,
        long sequence,
        string payloadJson,
        string previousChainHash,
        string chainHash
    )
    {
        if (tenantId == Guid.Empty)
            return Result.Failure(new Error("Signature.Audit.Tenant", "TenantId is required."));
        if (signatureRequestId == Guid.Empty)
            return Result.Failure(new Error("Signature.Audit.Request", "SignatureRequestId is required."));
        if (sequence < 1)
            return Result.Failure(new Error("Signature.Audit.Sequence", "Sequence must start at 1."));
        if (string.IsNullOrWhiteSpace(payloadJson))
            return Result.Failure(new Error("Signature.Audit.Payload", "PayloadJson is required."));
        if (payloadJson.Length > MaxPayloadJsonLength)
            return Result.Failure(
                new Error("Signature.Audit.PayloadSize", $"PayloadJson cannot exceed {MaxPayloadJsonLength} bytes.")
            );
        if (string.IsNullOrWhiteSpace(previousChainHash))
            return Result.Failure(new Error("Signature.Audit.PreviousHash", "PreviousChainHash is required."));
        if (!IsHexHash(chainHash))
            return Result.Failure(
                new Error("Signature.Audit.ChainHash", $"ChainHash must be a {ChainHashLength}-char hex string.")
            );
        return Result.Success();
    }

    private static bool IsHexHash(string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate) || candidate.Length != ChainHashLength)
            return false;
        foreach (var c in candidate)
        {
            var lower = char.ToLowerInvariant(c);
            var isHex = (lower >= '0' && lower <= '9') || (lower >= 'a' && lower <= 'f');
            if (!isHex)
                return false;
        }
        return true;
    }
}
