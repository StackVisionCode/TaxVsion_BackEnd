namespace TaxVision.Postmaster.Domain.Sending;

/// <summary>Por qué falló un envío por el buzón de la oficina, en nuestro idioma.</summary>
public enum MailboxSendFailure
{
    /// <summary>El buzón no puede enviar: desconectado, token vencido, sin permiso para enviar, cupo diario agotado.</summary>
    MailboxUnavailable,

    /// <summary>El proveedor o Connectors no respondió bien: caído, 5xx, timeout, error de red.</summary>
    ProviderUnavailable,

    /// <summary>Cupo por minuto: se difiere y se reintenta, no se cambia de vía.</summary>
    Throttled,

    /// <summary>El correo en sí está mal (dirección inválida, rechazado): por otra vía fallaría igual.</summary>
    InvalidMessage,

    Unknown,
}
