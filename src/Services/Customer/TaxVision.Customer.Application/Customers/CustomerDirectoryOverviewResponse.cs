namespace TaxVision.Customer.Application.Customers;

/// <summary>
/// Resumen del directorio para el dashboard: total exacto + altas por mes + últimas altas. Evita que el
/// front traiga cientos de filas para agrupar en el cliente (backlog 5.1).
/// </summary>
public sealed record CustomerDirectoryOverviewResponse(
    int TotalCount,
    IReadOnlyList<MonthlyNewCustomers> Monthly,
    IReadOnlyList<CustomerSummaryResponse> Recent
);

/// <summary>Altas de clientes de un mes. `Year`/`Month` en UTC.</summary>
public sealed record MonthlyNewCustomers(int Year, int Month, int Count);
