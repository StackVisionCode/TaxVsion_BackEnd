namespace TaxVision.Billing.Infrastructure.Persistence.Archive;

/// <summary>
/// Factura de alta (serie ONB-) que Billing emitió cuando todavía guardaba cobros de la plataforma. Se archivó al
/// sacarlas del agregado Invoice (2026-11-11): el documento del cobro ahora es el recibo del pago en PaymentApp.
/// Solo lectura histórica; no es parte del dominio ni la ve ninguna oficina.
/// </summary>
public sealed class ArchivedPlatformInvoice
{
    public Guid Id { get; init; }
    public string? InvoiceNumber { get; init; }
    public Guid TenantId { get; init; }
    public Guid OnboardingId { get; init; }
    public Guid? PaymentId { get; init; }
    public Guid? PdfFileId { get; init; }
    public string Total { get; init; } = string.Empty;
    public DateTime IssueDateUtc { get; init; }

    /// <summary>La factura completa al archivarla (cabecera, líneas, pagos, ajustes y cambios de estado) en JSON.</summary>
    public string SnapshotJson { get; init; } = string.Empty;
    public DateTime ArchivedAtUtc { get; init; }
}
