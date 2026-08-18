using Microsoft.Extensions.Options;

namespace TaxVision.Gateway.LoadShedding;

/// <summary>
/// Capa 1 (load shedder global de flota) del modelo de 4 capas de ADR-017. Sobrecarga = p99 de
/// latencia propia del Gateway o tasa de 5xx por encima del umbral, medidos sobre
/// <see cref="RequestOutcomeWindow"/> y precalculados en <see cref="OverloadSignal"/> (GW-05).
///
/// <para>
/// GW-14 — la política de rechazo es una cascada de tres niveles evaluados <b>en orden</b>; el
/// primero que diga "descarta" gana. Sustituye al criterio anterior ("sheddear a los N tenants de
/// mayor consumo"), que con menos de N tenants activos rechazaba el <b>100%</b> del tráfico: el
/// <c>Take(N)</c> no tenía piso, así que con 3 tenants los 3 estaban en el "top 10". Los niveles
/// nuevos no dependen del número de tenants, así que ese modo de fallo desaparece por construcción
/// y no por ajustar un umbral.
/// </para>
/// </summary>
public sealed class LoadShedder(
    OverloadSignal overloadSignal,
    TenantConsumptionTracker tenantTracker,
    RequestCriticalityClassifier classifier,
    IOptionsMonitor<LoadShedderOptions> options
) : ILoadShedder
{
    public int RetryAfterSeconds => options.CurrentValue.RetryAfterSeconds;

    public SheddingVerdict Evaluate(string tenantKey, PathString path, bool clientDisconnected)
    {
        var current = options.CurrentValue;

        // Nivel 0 — el cliente ya se fue. Trabajo cuyo resultado nadie va a leer: descartarlo libera
        // capacidad con impacto real cero, y por eso va antes incluso de mirar si hay sobrecarga.
        if (clientDisconnected)
            return SheddingVerdict.Abandoned;

        if (!current.Enabled)
            return SheddingVerdict.Allowed;

        // GW-05 — una lectura de campo: la ventana la agrega OverloadSignal cada 200 ms fuera del
        // camino de la peticion.
        if (!overloadSignal.IsOverloaded)
            return SheddingVerdict.Allowed;

        // Nivel 1 — criticidad de la petición, no del remitente. Background cae de todos los tenants
        // antes de tocar un solo Standard; Critical no cae nunca.
        var criticality = classifier.Classify(path);
        if (criticality == RequestCriticality.Background)
            return SheddingVerdict.LowCriticality;

        if (criticality == RequestCriticality.Critical)
            return SheddingVerdict.Allowed;

        // Nivel 2 — exceso sobre la parte justa, no volumen absoluto. Si todos consumen parecido
        // nadie supera el factor y nadie se sheddea: cuando la sobrecarga viene de un downstream
        // lento y no de un tenant abusivo, rechazar tráfico no arregla nada, solo suma errores.
        var consumption = tenantTracker.GetSnapshot(tenantKey);
        return consumption.ExcessOverFairShare > current.FairShareExcessFactor
            ? SheddingVerdict.FairShareExcess
            : SheddingVerdict.Allowed;
    }
}
