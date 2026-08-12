namespace TaxVision.Billing.Application.Invoices.CreateInvoiceDraft;

/// <summary>Crea un borrador de factura tenant→cliente con sus líneas. Montos en centavos; el
/// impuesto por línea se deriva de los basis points. Ver 07_Invoices_Use_Case_Catalog.md (UC-01).</summary>
public sealed record CreateInvoiceDraftCommand(
    Guid TenantId,
    Guid ActorUserId,
    InvoiceCustomerInput Customer,
    string Currency,
    IReadOnlyList<InvoiceLineInput> Lines,
    string? Notes,
    InvoiceIssuerInput? Issuer
);

public sealed record InvoiceCustomerInput(
    Guid CustomerId,
    string Name,
    string? Email,
    string? Phone,
    string? TaxId,
    InvoiceAddressInput? Billing
);

public sealed record InvoiceIssuerInput(
    string Name,
    InvoiceAddressInput Address,
    string? Phone,
    string? Email,
    string? Website,
    Guid? LogoFileId,
    string? TaxId = null
);

public sealed record InvoiceAddressInput(
    string Line1,
    string? Line2,
    string City,
    string State,
    string Zip,
    string Country
);

public sealed record InvoiceLineInput(
    string Description,
    int Quantity,
    long UnitAmountCents,
    int TaxBasisPoints,
    Guid? CatalogItemId = null
);
