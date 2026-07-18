namespace TaxVision.Correspondence.Domain.Inbox;

/// <summary>
/// "¿Ya se pidió el body de este correo al menos una vez?" — NO es un indicador de cache.
/// Correspondence nunca persiste el body en BD ni en CloudStorage (decisión 2026-07-17 del
/// plan de diseño, §17): cada vez que el usuario abre el mensaje se vuelve a pedir el body a
/// Connectors en vivo. Este flag solo evita, por ejemplo, mostrar un ícono de "nunca abierto".
/// </summary>
public enum BodyStatus
{
    BodyPending = 0,
    BodyReady = 1,
}
