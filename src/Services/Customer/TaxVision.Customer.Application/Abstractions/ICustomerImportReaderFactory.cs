using TaxVision.Customer.Domain.Imports;

namespace TaxVision.Customer.Application.Abstractions;

public interface ICustomerImportReaderFactory
{
    ICustomerImportReader Resolve(ImportSourceKind sourceKind);
}
