namespace TaxVision.Codes.Domain.Definitions;

/// <summary>Cuánto dura el descuento de un código.</summary>
public enum CodeDurationKind
{
    /// <summary>Solo el primer cobro.</summary>
    Once = 1,

    /// <summary>Durante N meses desde el primer cobro.</summary>
    Months = 2,

    /// <summary>Mientras la suscripción siga activa.</summary>
    Forever = 3,
}
