namespace TaxVision.Subscription.Domain.Subscriptions;

/// <summary>De dónde salió el descuento de la suscripción.</summary>
public enum DiscountSource
{
    /// <summary>Un código de Growth canjeado (en el alta o por una oficina existente).</summary>
    Code = 1,

    /// <summary>Lo dio el PlatformAdmin (retención o acuerdo), sin código.</summary>
    Admin = 2,
}

/// <summary>Cuánto dura el descuento.</summary>
public enum DiscountDuration
{
    /// <summary>Solo el cobro de la fecha de inicio.</summary>
    Once = 1,

    /// <summary>Durante N meses desde la fecha de inicio.</summary>
    Months = 2,

    /// <summary>Hasta que se termine a mano o por cambio de plan.</summary>
    Forever = 3,
}

/// <summary>Qué cobros cubre el descuento.</summary>
public enum DiscountAppliesTo
{
    /// <summary>Solo la suscripción base (plan).</summary>
    Base = 1,

    /// <summary>La base, los asientos y los add-ons.</summary>
    BaseSeatsAddOns = 2,
}

/// <summary>El tipo de cobro que se está calculando.</summary>
public enum DiscountChargeKind
{
    Base = 1,
    Seats = 2,
    AddOns = 3,
}
