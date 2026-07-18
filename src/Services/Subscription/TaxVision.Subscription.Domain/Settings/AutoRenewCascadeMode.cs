namespace TaxVision.Subscription.Domain.Settings;

/// <summary>
/// Por default los ciclos de renovación de la suscripción base y de cada seat son
/// independientes (None). CoordinateCommonPeriod es un opt-in explícito del tenant
/// para agrupar cobros de seats cuyo vencimiento cae cerca del de la suscripción base.
/// </summary>
public enum AutoRenewCascadeMode
{
    None,
    CoordinateCommonPeriod,
}
