namespace BuildingBlocks.RateLimiting;

/// <summary>
/// Categoría de rate-limiting de un endpoint o scope — taxonomía de 17 categorías
/// congelada en documents/RateLimit/Plan_Implementacion_Fases.md §4 (ADR_017). Cada
/// categoría fija partición primaria, overlay, algoritmo y consecuencia al exceder;
/// ver <see cref="RateLimitPolicyCatalog"/> para las políticas concretas de cada una.
/// </summary>
public enum RateLimitCategory
{
    /// <summary>Bloque I — Auth pre-tenant (login, refresh, MFA). Partición email + ip, sin tenant.</summary>
    A,

    /// <summary>Bloque I — Password/OTP flow. Partición email + ip, sin tenant.</summary>
    B,

    /// <summary>Bloque I — Onboarding pre-tenant. Partición email + ip, sin tenant (el tenant es lo que se crea).</summary>
    C,

    /// <summary>Bloque I — Público con token (share links, join-by-token). Partición token + ip.</summary>
    D,

    /// <summary>Bloque I — Webhooks externos firmados. Partición ip de origen. Nunca tenant.</summary>
    E,

    /// <summary>Bloque II — GET lectura ligera. Partición (tenant, user) + overlay tenant.</summary>
    F,

    /// <summary>Bloque II — Write ligero. Partición (tenant, user) + overlay tenant.</summary>
    G,

    /// <summary>Bloque II — Búsqueda / listado pesado. Partición (tenant, user) + overlay tenant + cap por endpoint.</summary>
    H,

    /// <summary>Bloque II — Bulk / upload grande. Partición (tenant, user) + overlay tenant + cap por endpoint.</summary>
    I,

    /// <summary>Bloque II — Rendering / cómputo caro. Partición tenant.</summary>
    J,

    /// <summary>Bloque III — Envío a proveedor externo. Partición (tenant, account/provider) + cap global por proveedor.</summary>
    K,

    /// <summary>Bloque III — Financiera, iniciar cobro. Partición (tenant, user) + overlay tenant.</summary>
    L,

    /// <summary>Bloque III — Financiera admin (money-out). Partición tenant; una política puede añadir
    /// User cuando el tenant es el PlatformTenant compartido por todo admin. Audit obligatorio. No
    /// escala por multiplicador de plan, pero sí admite <c>HardOverridePerMinute</c> por plan.</summary>
    M,

    /// <summary>Bloque IV — Reveal de dato sensible. Partición user, nunca tenant. Audit obligatorio.
    /// No escala por multiplicador de plan, pero sí admite <c>HardOverridePerMinute</c> por plan.</summary>
    N,

    /// <summary>Bloque IV — Realtime sockets. Partición (tenant, user) por scope.</summary>
    O,

    /// <summary>Bloque V — Health / observabilidad. Nunca rate-limited — usar [RateLimitExempt], no una política de esta categoría.</summary>
    P,

    /// <summary>Bloque V — Load shedder global de flota (Capa 1, Gateway, Fase 5). No tiene políticas per-servicio en el catálogo.</summary>
    Q,
}
