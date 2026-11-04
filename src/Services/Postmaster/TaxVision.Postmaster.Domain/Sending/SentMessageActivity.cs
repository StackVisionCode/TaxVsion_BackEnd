namespace TaxVision.Postmaster.Domain.Sending;

/// <summary>
/// El estado que ve la persona en "Email activity": une el resultado del envío con lo que contó el
/// proveedor después. Se guarda en <see cref="SentMessage.Activity"/> para que el filtro y la fila
/// usen la misma definición.
/// </summary>
public enum SentMessageActivity
{
    Sending,

    /// <summary>Aceptado por el relay y sin noticias todavía.</summary>
    Sent,
    Deferred,
    Delivered,
    Bounced,
    Complained,
    Failed,
    Suppressed,
}
