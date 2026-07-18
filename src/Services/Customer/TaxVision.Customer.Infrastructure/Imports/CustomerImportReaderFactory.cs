using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Domain.Imports;

namespace TaxVision.Customer.Infrastructure.Imports;

internal sealed class CustomerImportReaderFactory(CsvCustomerImportReader csv, XlsxCustomerImportReader xlsx)
    : ICustomerImportReaderFactory
{
    public ICustomerImportReader Resolve(ImportSourceKind sourceKind) =>
        sourceKind switch
        {
            ImportSourceKind.Csv => csv,
            ImportSourceKind.Xlsx => xlsx,
            _ => throw new ArgumentOutOfRangeException(nameof(sourceKind), sourceKind, "Unknown SourceKind."),
        };
}
