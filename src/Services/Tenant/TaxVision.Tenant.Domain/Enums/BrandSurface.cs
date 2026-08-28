namespace TaxVision.Tenant.Domain.Enums;

/// <summary>
/// Superficie de producto donde vive una identidad visual del tenant. Es una lista CERRADA que
/// controla el equipo (no el TenantAdmin): son las pantallas donde el software se muestra, no
/// configuración libre del cliente. Agregar una superficie nueva es una línea aquí, sin migración
/// (la columna se guarda como texto). v1 usa solo <see cref="Crm"/> y <see cref="Portal"/>;
/// <see cref="Mobile"/> y <see cref="Email"/> quedan modelados para el futuro sin tocar el esquema.
/// </summary>
public enum BrandSurface
{
    Crm,
    Portal,
    Mobile,
    Email,
}
