namespace BuildingBlocks.RateLimiting;

/// <summary>
/// Definición estática de una política de rate-limit — una fila de las tablas de
/// Plan_Implementacion_Fases.md §4, con nombre canónico resuelto (§6.1). Vive en
/// <see cref="RateLimitPolicyCatalog"/>. Consumida por <c>IRateLimitPolicyRegistry</c>
/// (Fase 2) para resolver la cuota efectiva por tenant/plan.
/// </summary>
public sealed record RateLimitPolicyDefinition
{
    public required RateLimitPolicyName Name { get; init; }
    public required RateLimitCategory Category { get; init; }

    /// <summary>Dimensión(es) de la clave de Capa 3/primaria — la partición que efectivamente se cuenta.</summary>
    public required RateLimitPartitionDimension PrimaryPartition { get; init; }

    /// <summary>
    /// Capas adicionales evaluadas por encima de <see cref="PrimaryPartition"/> (p.ej. Tenant como
    /// Capa 2 sobre Bloque II, o Ip como capa secundaria sobre Token en Bloque I categoría D).
    /// Cuando el Bloque II define un segundo número explícito para el overlay tenant (ver §4 —
    /// F/G/H/I/L listan "N/min user, M/min tenant"), ese número vive en
    /// <see cref="OverlayQuotaPerMinute"/> — decisión de Fase 3 al conectar el evaluador real
    /// (ver ADR_017 §2.2), reemplaza la nota anterior de "política hermana" para el caso común de
    /// un único overlay con su propio número. Categorías con overlays cualitativos ya cubiertos por
    /// un mecanismo propio (p.ej. K — cap global por proveedor, ya implementado por
    /// <c>IProviderRateLimiter</c> desde F26/Fase 0.3) dejan este campo en null a propósito: ese
    /// overlay no pasa por este evaluador genérico.
    /// </summary>
    public IReadOnlyCollection<RateLimitPartitionDimension> OverlayLayers { get; init; } = [];

    /// <summary>
    /// Cupo base para plan Standard, otorgado dentro de <see cref="WindowSeconds"/>. El nombre viene
    /// del diseño original del plan — pese a decir "PerMinute", no siempre es una ventana de 60s (ver
    /// WindowSeconds); es el número de "N" en "N/ventana".
    /// </summary>
    public required int BaseQuotaPerMinute { get; init; }

    /// <summary>
    /// Cupo base para plan Standard de la(s) <see cref="OverlayLayers"/> — mismo criterio de nombre
    /// que <see cref="BaseQuotaPerMinute"/>, misma <see cref="WindowSeconds"/>. Null cuando la
    /// categoría no tiene overlay numérico propio (p.ej. N, que nunca cuota por tenant; o M, que no
    /// escala; o K, cuyo overlay lo maneja un mecanismo aparte — ver doc de <see cref="OverlayLayers"/>).
    /// </summary>
    public int? OverlayQuotaPerMinute { get; init; }

    public required int WindowSeconds { get; init; }
    public required RateLimitAlgorithm Algorithm { get; init; }

    /// <summary>Capa 4 (H/I) — cap agregado cross-tenant, a diferencia de <see cref="OverlayQuotaPerMinute"/> que es por tenant. Null si no aplica.</summary>
    public int? EndpointCapPerWindow { get; init; }
}
