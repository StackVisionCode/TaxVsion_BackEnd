namespace BuildingBlocks.RateLimiting;

/// <summary>Algoritmo de conteo de una política — ver columna "Algoritmo" en Plan_Implementacion_Fases.md §4.</summary>
public enum RateLimitAlgorithm
{
    /// <summary>Ventana fija — cupo se resetea al cruzar el borde de la ventana. Barato, suficiente para pre-auth/webhooks/financiero.</summary>
    FixedWindow,

    /// <summary>Ventana deslizante — precisión real sin el efecto de ráfaga en el borde de la ventana fija. Usado donde la precisión importa (búsqueda pesada).</summary>
    SlidingWindow,

    /// <summary>Token bucket — tolera ráfagas cortas con una tasa sostenida de refill. Usado en el 80% del tráfico autenticado (F/G) y en rendering (J).</summary>
    TokenBucket,

    /// <summary>Leaky bucket — suaviza hacia un tercero externo con capacidad fija (envío a Gmail/Graph/SMTP). Nunca ráfaga hacia el proveedor.</summary>
    LeakyBucket,
}
