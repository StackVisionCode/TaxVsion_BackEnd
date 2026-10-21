namespace TaxVision.Customer.Application.Customers;

/// <summary>Columna por la que se ordena el listado de clientes.</summary>
public enum CustomerSortField
{
    Name, // default — DisplayName
    Created, // CreatedAtUtc
}

/// <summary>Sentido del orden del listado de clientes.</summary>
public enum CustomerSortDirection
{
    Asc, // default
    Desc,
}
