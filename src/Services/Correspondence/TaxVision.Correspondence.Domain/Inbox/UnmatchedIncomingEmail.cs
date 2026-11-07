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
/// <see cref="UnmatchedReason.NoCustomerMatch"/> ya no se escribe (D-COR-3: ese correo entra sin cliente), mientras que
/// <see cref="UnmatchedReason.AuthenticationFailed"/> es un registro de seguridad (siempre se
/// escribe, sin gate de config) — por eso <see cref="ExpiresAtUtc"/> usa un TTL distinto según
/// el motivo en vez de un valor fijo de 24h para toda la tabla.
/// </para>
/// </summary>
public sealed class UnmatchedIncomingEmail : ITenantOwned
{
    public const int SubjectMaxLength = 1000;
    public const int ProviderMessageIdMaxLength = 200;
    public const int RejectionCodeMaxLength = 100;
    public const int ProviderCodeMaxLength = 20;
    public const int InternetMessageIdMaxLength = 500;

    /// <summary>Ruido de debug (sender desconocido) — TTL corto per plan §14/§24.</summary>
    private static readonly TimeSpan NoCustomerMatchTtl = TimeSpan.FromHours(24);

    /// <summary>
    /// Registro de seguridad (posible spoofing) — TTL mucho más largo que el caso de debug
    /// porque puede hacer falta para una investigación de incidente días o semanas después,
    /// no solo para el diagnóstico inmediato de un mismatch de proyección. Se alinea con el
    /// retention de 90 días que el plan §26 ya usa para <c>CorrespondenceAuditLog</c>.
    /// </summary>
    private static readonly TimeSpan AuthenticationFailedTtl = TimeSpan.FromDays(90);

    // Correo de cliente rechazado: queda lo necesario para reprocesarlo.
    private static readonly TimeSpan RejectedTtl = TimeSpan.FromDays(30);

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

    // Solo en InvalidSender/IngestRejected.
    public string? RejectionCode { get; private set; }
    public Guid? AccountId { get; private set; }
    public string? ProviderCode { get; private set; }
    public string? InternetMessageId { get; private set; }

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

        var validationError = Validate(tenantId, providerMessageId);
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
                Subject = FitSubject(subject),
                ProviderMessageId = providerMessageId,
                ReceivedAtUtc = receivedAtUtc,
                Reason = reason,
                CreatedAtUtc = now,
                ExpiresAtUtc = now.Add(ttl),
            }
        );
    }

    /// <summary>
    /// Registro de un correo que no se pudo ingerir (remitente inválido o rechazo del dominio). Recorta en vez
    /// de fallar para que el rastro siempre quede; solo falla sin tenant o sin id de proveedor.
    /// </summary>
    public static Result<UnmatchedIncomingEmail> CreateRejected(
        Guid tenantId,
        string? rawFrom,
        string? subject,
        string providerMessageId,
        DateTime receivedAtUtc,
        UnmatchedReason reason,
        string? rejectionCode,
        Guid? accountId,
        string? providerCode,
        string? internetMessageId
    )
    {
        providerMessageId = Fit(providerMessageId, ProviderMessageIdMaxLength)!;
        var validationError = Validate(tenantId, providerMessageId);
        if (validationError is not null)
            return Result.Failure<UnmatchedIncomingEmail>(validationError);

        var now = DateTime.UtcNow;
        var from = string.IsNullOrWhiteSpace(rawFrom) ? "(unknown)" : rawFrom.Trim().ToLowerInvariant();

        return Result.Success(
            new UnmatchedIncomingEmail
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                FromAddress = Fit(from, EmailAddress.MaxLength)!,
                Subject = FitSubject(subject),
                ProviderMessageId = providerMessageId,
                ReceivedAtUtc = receivedAtUtc,
                Reason = reason,
                CreatedAtUtc = now,
                ExpiresAtUtc = now.Add(RejectedTtl),
                RejectionCode = Fit(rejectionCode, RejectionCodeMaxLength),
                AccountId = accountId == Guid.Empty ? null : accountId,
                ProviderCode = Fit(providerCode, ProviderCodeMaxLength),
                InternetMessageId = Fit(internetMessageId, InternetMessageIdMaxLength),
            }
        );
    }

    private static string FitSubject(string? subject) =>
        string.IsNullOrWhiteSpace(subject) ? EmailThread.NoSubject : Fit(subject, SubjectMaxLength)!;

    private static string? Fit(string? value, int maxLength) =>
        value is { Length: > 0 } && value.Length > maxLength ? value[..maxLength] : value;

    private static Error? Validate(Guid tenantId, string providerMessageId)
    {
        if (tenantId == Guid.Empty)
            return new Error("UnmatchedIncomingEmail.TenantIdRequired", "TenantId is required.");
        if (string.IsNullOrWhiteSpace(providerMessageId) || providerMessageId.Length > ProviderMessageIdMaxLength)
            return new Error(
                "UnmatchedIncomingEmail.ProviderMessageIdInvalid",
                "ProviderMessageId is required and must not exceed 200 characters."
            );

        return null;
    }
}
