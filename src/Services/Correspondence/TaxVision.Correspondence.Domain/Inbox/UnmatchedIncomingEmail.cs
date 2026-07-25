using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Correspondence.Domain.ValueObjects;

namespace TaxVision.Correspondence.Domain.Inbox;

/// <summary>
/// Tabla de cuarentena/debug para mensajes de <c>connectors.raw_message_received.v1</c> que NO
/// se convirtieron en <see cref="IncomingEmail"/> (plan §14/§36 Fase 4). Nunca guarda body ni
/// attachments — solo la metadata mínima para diagnosticar por qué el mensaje quedó afuera.
///
/// <para>
/// Dos motivos muy distintos comparten esta tabla (ver <see cref="UnmatchedReason"/>):
/// <see cref="UnmatchedReason.NoCustomerMatch"/> es ruido de debug (opt-in vía
/// <c>Correspondence:EnableUnmatchedDebug</c>, TTL corto), mientras que
/// <see cref="UnmatchedReason.AuthenticationFailed"/> es un registro de seguridad (siempre se
/// escribe, sin gate de config) — por eso <see cref="ExpiresAtUtc"/> usa un TTL distinto según
/// el motivo en vez de un valor fijo de 24h para toda la tabla.
/// </para>
/// </summary>
public sealed class UnmatchedIncomingEmail : ITenantOwned
{
    public const int SubjectMaxLength = 1000;
    public const int ProviderMessageIdMaxLength = 200;

    /// <summary>Ruido de debug (sender desconocido) — TTL corto per plan §14/§24.</summary>
    private static readonly TimeSpan NoCustomerMatchTtl = TimeSpan.FromHours(24);

    /// <summary>
    /// Registro de seguridad (posible spoofing) — TTL mucho más largo que el caso de debug
    /// porque puede hacer falta para una investigación de incidente días o semanas después,
    /// no solo para el diagnóstico inmediato de un mismatch de proyección. Se alinea con el
    /// retention de 90 días que el plan §26 ya usa para <c>CorrespondenceAuditLog</c>.
    /// </summary>
    private static readonly TimeSpan AuthenticationFailedTtl = TimeSpan.FromDays(90);

    private UnmatchedIncomingEmail() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string FromAddress { get; private set; } = default!;
    public string Subject { get; private set; } = default!;
    public string ProviderMessageId { get; private set; } = default!;
    public DateTime ReceivedAtUtc { get; private set; }
    public UnmatchedReason Reason { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }

    /// <summary>RBAC Fase 5 (RBAC_Hardening_Plan.md) — ver <see cref="Compose.Draft.SetTenant"/>.</summary>
    public void SetTenant(Guid tenantId) => TenantId = tenantId;

    public static Result<UnmatchedIncomingEmail> Create(
        Guid tenantId,
        EmailAddress fromAddress,
        string subject,
        string providerMessageId,
        DateTime receivedAtUtc,
        UnmatchedReason reason
    )
    {
        ArgumentNullException.ThrowIfNull(fromAddress);

        var validationError = Validate(tenantId, subject, providerMessageId);
        if (validationError is not null)
            return Result.Failure<UnmatchedIncomingEmail>(validationError);

        var now = DateTime.UtcNow;
        var ttl = reason == UnmatchedReason.AuthenticationFailed ? AuthenticationFailedTtl : NoCustomerMatchTtl;

        return Result.Success(
            new UnmatchedIncomingEmail
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                FromAddress = fromAddress.NormalizedValue,
                Subject = subject,
                ProviderMessageId = providerMessageId,
                ReceivedAtUtc = receivedAtUtc,
                Reason = reason,
                CreatedAtUtc = now,
                ExpiresAtUtc = now.Add(ttl),
            }
        );
    }

    private static Error? Validate(Guid tenantId, string subject, string providerMessageId)
    {
        if (tenantId == Guid.Empty)
            return new Error("UnmatchedIncomingEmail.TenantIdRequired", "TenantId is required.");
        if (string.IsNullOrWhiteSpace(subject) || subject.Length > SubjectMaxLength)
            return new Error(
                "UnmatchedIncomingEmail.SubjectInvalid",
                "Subject is required and must not exceed 1000 characters."
            );
        if (string.IsNullOrWhiteSpace(providerMessageId) || providerMessageId.Length > ProviderMessageIdMaxLength)
            return new Error(
                "UnmatchedIncomingEmail.ProviderMessageIdInvalid",
                "ProviderMessageId is required and must not exceed 200 characters."
            );

        return null;
    }
}
