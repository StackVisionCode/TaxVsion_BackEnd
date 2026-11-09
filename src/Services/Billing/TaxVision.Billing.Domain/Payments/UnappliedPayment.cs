using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Billing.Domain.Payments;

public enum UnappliedPaymentReason
{
    InvoiceVoided,
    CurrencyMismatch,
    InvoiceNotFound,
    InvalidReference,
    NotPayable,
}

public enum UnappliedPaymentResolution
{
    Refunded,
    AppliedAsCredit,
}

/// <summary>
/// Cobro confirmado por PaymentClient que no se pudo aplicar a una factura. Queda visible para la
/// oficina hasta que una persona lo resuelve (reembolso o crédito). Uno por (TenantId, TenantPaymentId).
/// </summary>
public sealed class UnappliedPayment : TenantEntity
{
    public const int ReferenceMaxLength = 128;
    public const int NoteMaxLength = 500;

    // ISO 4217 "sin moneda": se usa cuando el evento trae una moneda inválida.
    public const string UnknownCurrency = "XXX";

    public Guid TenantPaymentId { get; private set; }
    public Guid? InvoiceId { get; private set; }
    public string ExternalReferenceId { get; private set; } = string.Empty;
    public long AmountCents { get; private set; }
    public string Currency { get; private set; } = UnknownCurrency;
    public UnappliedPaymentReason Reason { get; private set; }
    public DateTime ReceivedAtUtc { get; private set; }
    public DateTime? ResolvedAtUtc { get; private set; }
    public UnappliedPaymentResolution? Resolution { get; private set; }
    public string? ResolutionNote { get; private set; }

    [UserReference(UserReferenceKind.Provenance)]
    public Guid? ResolvedByUserId { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    private UnappliedPayment() { }

    public static UnappliedPayment Record(
        Guid tenantId,
        Guid tenantPaymentId,
        Guid? invoiceId,
        string? externalReferenceId,
        long amountCents,
        string? currency,
        UnappliedPaymentReason reason,
        DateTime receivedAtUtc
    )
    {
        var payment = new UnappliedPayment
        {
            TenantPaymentId = tenantPaymentId,
            InvoiceId = invoiceId,
            ExternalReferenceId = Truncate(externalReferenceId?.Trim() ?? string.Empty, ReferenceMaxLength),
            AmountCents = amountCents,
            Currency = NormalizeCurrency(currency),
            Reason = reason,
            ReceivedAtUtc = receivedAtUtc,
        };
        payment.SetTenant(tenantId);
        return payment;
    }

    /// <summary>Una persona cierra el caso (reembolso en PaymentClient o crédito). Nunca es automático.</summary>
    public Result Resolve(UnappliedPaymentResolution resolution, string? note, Guid actorUserId, DateTime nowUtc)
    {
        if (ResolvedAtUtc is not null)
            return Result.Failure(
                new Error("Billing.UnappliedPayment.AlreadyResolved", "This payment was already resolved.")
            );

        if (!Enum.IsDefined(resolution))
            return Result.Failure(
                new Error(
                    "Billing.UnappliedPayment.InvalidResolution",
                    "Resolution must be Refunded or AppliedAsCredit."
                )
            );

        var text = note?.Trim();
        if (string.IsNullOrEmpty(text))
            return Result.Failure(new Error("Billing.UnappliedPayment.NoteRequired", "A resolution note is required."));

        if (text.Length > NoteMaxLength)
            return Result.Failure(
                new Error(
                    "Billing.UnappliedPayment.NoteTooLong",
                    $"The resolution note cannot exceed {NoteMaxLength} characters."
                )
            );

        Resolution = resolution;
        ResolutionNote = text;
        ResolvedByUserId = actorUserId;
        ResolvedAtUtc = nowUtc;
        return Result.Success();
    }

    private static string NormalizeCurrency(string? currency)
    {
        var code = currency?.Trim().ToUpperInvariant() ?? string.Empty;
        return code.Length == 3 && code.All(char.IsAsciiLetterUpper) ? code : UnknownCurrency;
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
