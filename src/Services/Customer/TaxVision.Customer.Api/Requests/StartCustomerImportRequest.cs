using TaxVision.Customer.Domain.Imports;

namespace TaxVision.Customer.Api.Requests;

/// <summary>
/// Body del POST /customers/imports (multipart/form-data).
/// El file viene como IFormFile en el controller; este DTO solo tiene los campos del form.
/// </summary>
public sealed class StartCustomerImportRequest
{
    /// <summary>Politica para duplicados detectados. Default Skip.</summary>
    public DuplicateStrategy Strategy { get; set; } = DuplicateStrategy.Skip;
}
