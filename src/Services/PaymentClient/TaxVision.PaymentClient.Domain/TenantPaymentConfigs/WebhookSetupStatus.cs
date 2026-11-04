namespace TaxVision.PaymentClient.Domain.TenantPaymentConfigs;

/// <summary>Cómo quedó el webhook de la oficina en el proveedor. Null en el config = nunca se intentó
/// el registro automático y no hay secreto manual.</summary>
public enum WebhookSetupStatus
{
    /// <summary>Registro automático encolado o en curso.</summary>
    Pending = 1,

    /// <summary>Lo creó el backend en el proveedor (se guarda su id para poder borrarlo).</summary>
    Automatic = 2,

    /// <summary>La oficina pegó el secreto a mano; el registro automático nunca lo pisa.</summary>
    Manual = 3,

    /// <summary>Se intentó y no se pudo tras agotar reintentos; queda opcional como antes.</summary>
    Failed = 4,
}
