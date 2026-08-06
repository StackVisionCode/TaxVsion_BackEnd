namespace TaxVision.Correspondence.Infrastructure.Customers;

/// <summary>Credenciales client-credentials para obtener tokens M2M desde Auth (worker background).</summary>
public sealed class ServiceAuthClientOptions
{
    public const string SectionName = "Correspondence:ServiceAuth";

    public string AuthBaseUrl { get; set; } = "http://localhost:5124";
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
}

/// <summary>
/// Base URL del microservicio Customer + toggle/intervalo del job de reconciliación de la proyección
/// <c>CustomerEmailAddress</c>. En Docker: http://customer-api:8080.
/// </summary>
public sealed class CustomerClientOptions
{
    public const string SectionName = "Correspondence:Customer";

    public string BaseUrl { get; set; } = "http://localhost:5263";

    /// <summary>Si el job periódico de reconciliación corre. Default true (self-healing).</summary>
    public bool ReconciliationEnabled { get; set; } = true;

    /// <summary>Cada cuántas horas re-pagina la fuente completa. Default 6h.</summary>
    public int ReconciliationIntervalHours { get; set; } = 12;

    /// <summary>Tamaño de página al paginar customers/internal/reconciliation. Default 200.</summary>
    public int ReconciliationPageSize { get; set; } = 200;
}
