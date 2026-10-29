namespace TaxVision.Customer.Infrastructure.Employees;

/// <summary>Reconciliación periódica del directorio de empleados con el staff de Auth.</summary>
public sealed class EmployeeDirectoryReconciliationOptions
{
    public const string SectionName = "EmployeeDirectoryReconciliation";

    public bool Enabled { get; set; } = true;

    public int IntervalHours { get; set; } = 6;

    public int PageSize { get; set; } = 200;
}
