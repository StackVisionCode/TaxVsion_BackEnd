using BuildingBlocks.Results;

namespace TaxVision.Billing.Application.Abstractions;

/// <summary>
/// Puerto M2M hacia PaymentClient: asegura (find-or-create) el ancla ESTABLE de cobro de una factura
/// y devuelve su URL absoluta. PaymentClient es dueño de la URL (dominio, ruta, versionado); Billing
/// solo la guarda y la embebe en el PDF. El link con token se crea perezosamente del lado PaymentClient
/// al abrirse la URL. Montos en centavos (contrato de PaymentClient). Idempotente por factura.
/// </summary>
public interface IInvoicePaymentLinkClient
{
    Task<Result<InvoicePayableResult>> EnsurePayableAsync(
        long amountCents,
        string currency,
        Guid invoiceId,
        Guid tenantId,
        CancellationToken ct = default
    );
}

/// <summary>Resultado del ensure: el id del payable (para correlación Fase 3) y la URL estable de cobro
/// (la del botón/QR del PDF), ya compuesta y absoluta por PaymentClient.</summary>
public sealed record InvoicePayableResult(Guid PayableId, string CheckoutUrl);
