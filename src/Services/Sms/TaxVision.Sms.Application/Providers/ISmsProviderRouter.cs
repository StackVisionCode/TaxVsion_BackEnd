using Microsoft.Extensions.Options;

namespace TaxVision.Sms.Application.Providers;

/// <summary>
/// Decide POR QUÉ proveedor(es) sale un mensaje — decisión de PLATAFORMA (SaaS), no del tenant.
/// Devuelve la cadena priorizada de adapters: el handler intenta el primero y, si rechaza o está
/// caído, hace failover al siguiente. Aísla la política de ruteo del handler: hoy es "orden fijo
/// por config"; mañana podría ser por país/prefijo o costo, sin tocar el handler ni el endpoint.
/// </summary>
public interface ISmsProviderRouter
{
    /// <summary>Adapters a intentar, en orden de prioridad. Nunca vacío si hay config válida.</summary>
    IReadOnlyList<ISmsProvider> ResolveOrder();
}

/// <summary>
/// Router por configuración del servicio: usa <see cref="SmsOptions.ProviderOrder"/> si está poblada
/// (cadena de failover), o cae a <see cref="SmsOptions.DefaultProvider"/> (un solo proveedor, sin
/// failover — comportamiento clásico). Deduplica preservando orden.
/// </summary>
public sealed class SmsProviderRouter(ISmsAdapterFactory factory, IOptions<SmsOptions> options) : ISmsProviderRouter
{
    public IReadOnlyList<ISmsProvider> ResolveOrder()
    {
        // Filtra vacíos PRIMERO: los slots de env (Sms__ProviderOrder__0/1/2) llegan como cadenas
        // vacías cuando no se usan, no ausentes — sin este filtro, [""], ["",""] contarían como
        // "orden configurada" y, al descartarse todas, dejarían la lista vacía (sms.noProvider).
        var fromOrder = (options.Value.ProviderOrder ?? [])
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .ToList();

        var configured = fromOrder.Count > 0 ? fromOrder : [options.Value.DefaultProvider];

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var providers = new List<ISmsProvider>(configured.Count);
        foreach (var code in configured)
        {
            if (string.IsNullOrWhiteSpace(code) || !seen.Add(code))
                continue;
            providers.Add(factory.Resolve(code));
        }
        return providers;
    }
}
