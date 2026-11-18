namespace TaxVision.Customer.Application.Abstractions;

/// <summary>Plantilla XLSX del import, con las ocupaciones activas del catálogo en su lista.</summary>
public interface ICustomerImportXlsxTemplate
{
    Task<byte[]> BuildAsync(CancellationToken ct);
}
