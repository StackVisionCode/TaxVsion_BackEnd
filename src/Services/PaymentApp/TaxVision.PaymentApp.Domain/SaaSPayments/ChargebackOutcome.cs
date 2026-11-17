namespace TaxVision.PaymentApp.Domain.SaaSPayments;

/// <summary>Cómo cerró la disputa de un contracargo.</summary>
public enum ChargebackOutcome
{
    /// <summary>El comercio ganó: el proveedor devolvió los fondos.</summary>
    Won = 1,

    /// <summary>El comprador ganó: los fondos no vuelven.</summary>
    Lost = 2,
}
