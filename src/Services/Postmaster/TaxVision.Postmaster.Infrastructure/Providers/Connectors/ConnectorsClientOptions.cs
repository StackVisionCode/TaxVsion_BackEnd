namespace TaxVision.Postmaster.Infrastructure.Providers.Connectors;

/// <summary>Base URL del microservicio Connectors (D3 §4.4) — mismo patrón que <c>CloudStorageClientOptions</c>.</summary>
public sealed class ConnectorsClientOptions
{
    public const string SectionName = "Postmaster:Connectors";

    public string BaseUrl { get; set; } = "http://localhost:5390";

    /// <summary>Espera máxima de Postmaster a la respuesta de Connectors. Connectors espera a su vez hasta 30 s al
    /// proveedor del buzón (Gmail/Microsoft). Con el MISMO valor de 30 s los dos topes vencen casi a la vez y Postmaster se
    /// rinde primero: el envío queda como «resultado desconocido» y NO cae al correo del sistema, o sea, un buzón lento
    /// deja la factura sin salir. Con margen, llega el error estructurado de Connectors (<c>Timeout</c> →
    /// <c>ProviderUnavailable</c>) y el envío se reintenta por el carril del sistema.</summary>
    public int TimeoutSeconds { get; set; } = DefaultTimeoutSeconds;

    /// <summary>Margen sobre los 30 s de Connectors hacia el proveedor.</summary>
    public const int DefaultTimeoutSeconds = 45;

    /// <summary>Piso: por debajo del tope de Connectors volvería el problema de arriba.</summary>
    public const int MinimumTimeoutSeconds = 35;

    /// <summary>Timeout efectivo: el configurado, nunca menor que el piso.</summary>
    public TimeSpan EffectiveTimeout => TimeSpan.FromSeconds(Math.Max(MinimumTimeoutSeconds, TimeoutSeconds));
}
