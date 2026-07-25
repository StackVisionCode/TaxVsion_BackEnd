using Xunit;

namespace TaxVision.BuildingBlocks.Tests.ActorTypeAuthorization;

/// <summary>
/// RBAC Fase 10 — <see cref="BuildingBlocks.ActorTypeAuthorization.AuthorizationMetrics"/> usa un
/// <see cref="System.Diagnostics.Metrics.Meter"/> con nombre fijo (proceso-wide, ver
/// <c>AuthorizationMetrics.MeterName</c>). Un <see cref="System.Diagnostics.Metrics.MeterListener"/>
/// suscripto por nombre recibe mediciones de CUALQUIER instancia de ese Meter, incluida la de otro
/// test corriendo en paralelo — agrupar acá todas las clases que crean/aserten sobre ese Meter para
/// que xUnit nunca las corra concurrentemente entre sí (sí pueden seguir corriendo en paralelo con
/// el resto de la suite, que no toca este Meter).
/// </summary>
[CollectionDefinition(Name)]
public sealed class AuthorizationMetricsCollection
{
    public const string Name = "AuthorizationMetrics (serialized — shared Meter)";
}
