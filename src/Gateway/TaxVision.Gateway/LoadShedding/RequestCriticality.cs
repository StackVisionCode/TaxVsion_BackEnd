namespace TaxVision.Gateway.LoadShedding;

/// <summary>
/// Eje primario de la decisión de shedding (GW-14, §9.1): bajo sobrecarga se descarta por lo que la
/// petición <b>hace</b>, no por quién la manda. Es preferible que a todo el mundo le falle el reporte
/// de analytics a que a alguien le falle el cobro — el criterio que Google SRE (<i>Handling
/// Overload</i>) y la AWS Builders' Library ponen primero.
/// </summary>
public enum RequestCriticality
{
    /// <summary>Analítica, marketing, auditoría y correo saliente: su emisor reintenta, o su pérdida
    /// no le cambia nada al usuario ahora mismo. Es lo primero que se descarta.</summary>
    Background = 0,

    /// <summary>El trabajo normal del producto. Se descarta solo si además el tenant está muy por
    /// encima de su parte justa.</summary>
    Standard = 1,

    /// <summary>Login, alta de tenant y cobro. Nunca se descarta por sobrecarga: rechazarlo convierte
    /// una degradación en una caída de negocio.</summary>
    Critical = 2,
}
