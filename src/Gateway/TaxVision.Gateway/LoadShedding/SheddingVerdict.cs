namespace TaxVision.Gateway.LoadShedding;

/// <summary>
/// Resultado de la cascada de <see cref="LoadShedder"/> (GW-14, §9.1). Es un enum y no un `bool`
/// porque el <b>motivo</b> del descarte es la señal que hace falta en métricas y logs para saber si
/// el shedder está protegiendo o amplificando el incidente.
/// </summary>
public enum SheddingVerdict
{
    /// <summary>Sigue adelante.</summary>
    Allowed = 0,

    /// <summary>Nivel 0 — el cliente ya cortó. Trabajo cuyo resultado nadie va a leer: descartarlo
    /// libera capacidad con impacto real cero.</summary>
    Abandoned = 1,

    /// <summary>Nivel 1 — hay sobrecarga y la petición es <see cref="RequestCriticality.Background"/>.
    /// Se descarta de <b>todos</b> los tenants antes de tocar un solo <c>Standard</c>.</summary>
    LowCriticality = 2,

    /// <summary>Nivel 2 — hay sobrecarga y este tenant consume desproporcionadamente por encima de la
    /// media de tenants activos. Es una condición continua, no un top-N.</summary>
    FairShareExcess = 3,
}
