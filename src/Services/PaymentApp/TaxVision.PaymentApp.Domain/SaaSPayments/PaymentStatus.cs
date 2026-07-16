namespace TaxVision.PaymentApp.Domain.SaaSPayments;

/// <summary>
/// Estado del pago SaaS. Persistido como string (HasConversion&lt;string&gt;) — nunca
/// se infiere el estado desde otro campo (anti-patrón encontrado en el CRM legado, donde
/// el status se derivaba de "current balance == 0").
/// </summary>
public enum PaymentStatus
{
    /// <summary>Creado, aún no se envió al provider.</summary>
    Pending = 1,

    /// <summary>Enviado al provider, esperando confirmación (p.ej. 3DS, webhook async).</summary>
    Processing = 2,

    /// <summary>Requiere acción adicional del pagador (3DS / SCA) antes de poder confirmarse.</summary>
    RequiresAction = 3,

    /// <summary>Provider confirmó el cobro exitoso.</summary>
    Succeeded = 4,

    /// <summary>Provider rechazó el cobro o el intento falló.</summary>
    Failed = 5,

    /// <summary>Cancelado antes de completarse (nunca llegó a cobrarse).</summary>
    Cancelled = 6,

    /// <summary>Reembolsado parcialmente — sigue teniendo saldo cobrado.</summary>
    PartiallyRefunded = 7,

    /// <summary>Reembolsado en su totalidad.</summary>
    Refunded = 8,

    /// <summary>El emisor de la tarjeta revirtió el cargo (dispute perdido).</summary>
    ChargedBack = 9,
}
