namespace TaxVision.Templates.Renderer;

/// <summary>
/// Primitiva de render: dado un cache key ya conocido por el caller (mismo formato que usa Scribe —
/// <c>scribe:{tenant}:{identifier}:{version}:{kind}</c>) y variables, parsea+renderiza Liquid. No
/// resuelve EventKey→TemplateKey, no compone template+layout, no resuelve el logo del tenant — esa
/// orquestación vive en Scribe (que sí tiene BD). Este paquete es deliberadamente "tonto": sirve para
/// que un consumidor (ej. Postmaster) pueda seguir renderizando un template que YA se renderizó
/// exitosamente al menos una vez (y por lo tanto ya está en el Redis compartido) si Scribe está caído.
/// </summary>
public interface IFluidRendererCore
{
    Task<FluidRenderResult> RenderAsync(
        string redisKey,
        IReadOnlyDictionary<string, object?> variables,
        bool htmlEncode,
        CancellationToken ct = default
    );
}
