namespace BuildingBlocks.RateLimiting;

/// <summary>
/// Dimensión(es) que componen la clave de partición de una política — invariante §3.2
/// ("toda partición debe ser explícita"). Combinable: p.ej. Bloque II usa
/// <c>Tenant | User</c> como partición primaria (una sola clave combinada, Capa 3) más
/// <c>Tenant</c> solo como overlay (Capa 2, clave separada más amplia).
/// </summary>
[Flags]
public enum RateLimitPartitionDimension
{
    None = 0,
    Ip = 1 << 0,
    Email = 1 << 1,
    Tenant = 1 << 2,
    User = 1 << 3,
    Token = 1 << 4,
    AccountOrProvider = 1 << 5,
}
