using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Billing.Domain.ValueObjects;

namespace TaxVision.Billing.Domain.Settings;

/// <summary>
/// Ajustes de facturación propios de Billing (los datos de la oficina viven en Tenant). Una fila por
/// tenant: plazo de pago, instrucciones de pago y pie de la factura. Cada factura los congela al crearse
/// (<see cref="ToSnapshot"/>), así cambiarlos no altera las ya emitidas.
/// </summary>
public sealed class TenantBillingSettings : TenantEntity
{
    /// <summary>Igual al largo de las columnas de texto (aquí y en Invoices).</summary>
    public const int TextMaxLength = 500;

    public static readonly Error PaymentTermsInvalid = new(
        "Billing.InvoiceSettings.PaymentTerms",
        "Payment terms must be one of: DueOnReceipt, Net7, Net15, Net30, Net45."
    );

    public static readonly Error PaymentInstructionsTooLong = new(
        "Billing.InvoiceSettings.PaymentInstructions",
        "Payment instructions cannot exceed 500 characters."
    );

    public static readonly Error InvoiceFooterTooLong = new(
        "Billing.InvoiceSettings.InvoiceFooter",
        "The invoice footer cannot exceed 500 characters."
    );

    public PaymentTerms PaymentTerms { get; private set; } = PaymentTerms.DueOnReceipt;
    public string? PaymentInstructions { get; private set; }
    public string? InvoiceFooter { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    private TenantBillingSettings() { }

    public static TenantBillingSettings Create(Guid tenantId, DateTime nowUtc)
    {
        var settings = new TenantBillingSettings { CreatedAtUtc = nowUtc, UpdatedAtUtc = nowUtc };
        settings.SetTenant(tenantId);
        return settings;
    }

    /// <summary>Estado completo. Texto vacío = sin dato; uno demasiado largo rechaza todo sin cambiar nada.</summary>
    public Result Update(PaymentTerms paymentTerms, string? paymentInstructions, string? invoiceFooter, DateTime nowUtc)
    {
        if (!Enum.IsDefined(paymentTerms))
            return Result.Failure(PaymentTermsInvalid);

        var instructions = Clean(paymentInstructions);
        if (instructions?.Length > TextMaxLength)
            return Result.Failure(PaymentInstructionsTooLong);

        var footer = Clean(invoiceFooter);
        if (footer?.Length > TextMaxLength)
            return Result.Failure(InvoiceFooterTooLong);

        PaymentTerms = paymentTerms;
        PaymentInstructions = instructions;
        InvoiceFooter = footer;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    public InvoiceTermsSnapshot ToSnapshot() => new(PaymentTerms, PaymentInstructions, InvoiceFooter);

    private static string? Clean(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
