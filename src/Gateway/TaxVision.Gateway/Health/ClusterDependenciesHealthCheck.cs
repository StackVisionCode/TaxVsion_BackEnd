using Microsoft.Extensions.Diagnostics.HealthChecks;
using Yarp.ReverseProxy;
using Yarp.ReverseProxy.Model;

namespace TaxVision.Gateway.Health;

/// <summary>
/// Estado de los clusters upstream, leído del propio estado de YARP (<see cref="IProxyStateLookup"/>)
/// en vez de con HTTP propio.
///
/// <para>
/// GW-06 — el planteo original ("completar los 4 health checks a 18") era la corrección equivocada:
/// si <c>/health/ready</c> del Gateway falla porque 1 de 18 servicios está caído, el orquestador lo
/// saca del balanceador y convierte una <b>degradación parcial en una caída total</b>. Por eso esto
/// devuelve <see cref="HealthStatus.Degraded"/>, nunca <c>Unhealthy</c>, y se expone en
/// <c>/health/dependencies</c> — separado del readiness, que solo responde "¿puedo aceptar
/// tráfico?". De paso elimina los 4 <c>HttpEndpointHealthCheck</c> manuales y sus
/// <c>Configuration[...]!</c>, que reventaban al arrancar si faltaba una clave.
/// </para>
///
/// <para>
/// La señal viene de <c>ActiveHealthCheck</c> (GW-07). Con <c>HealthyOrPanic</c> y un solo destino
/// por cluster no cambia el enrutado — su valor es de observabilidad y de fail-fast, no de
/// balanceo.
/// </para>
/// </summary>
public sealed class ClusterDependenciesHealthCheck(IProxyStateLookup lookup) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        var unhealthy = new List<string>();
        var unknown = new List<string>();

        foreach (var cluster in lookup.GetClusters())
        {
            foreach (var destination in cluster.DestinationsState.AllDestinations)
            {
                var name = $"{cluster.ClusterId}/{destination.DestinationId}";
                switch (destination.Health.Active)
                {
                    case DestinationHealth.Unhealthy:
                        unhealthy.Add(name);
                        break;
                    case DestinationHealth.Unknown:
                        unknown.Add(name);
                        break;
                }
            }
        }

        var data = new Dictionary<string, object>
        {
            ["unhealthy"] = unhealthy,
            // Unknown = todavía sin sondear (arranque) o con el active check apagado. No es un fallo.
            ["unknown"] = unknown,
        };

        return Task.FromResult(
            unhealthy.Count == 0
                ? HealthCheckResult.Healthy("All upstream clusters are reachable.", data)
                : HealthCheckResult.Degraded(
                    $"{unhealthy.Count} upstream destination(s) unhealthy: {string.Join(", ", unhealthy)}",
                    data: data
                )
        );
    }
}
