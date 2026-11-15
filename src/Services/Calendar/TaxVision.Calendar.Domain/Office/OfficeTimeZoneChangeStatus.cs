namespace TaxVision.Calendar.Domain.Office;

public enum OfficeTimeZoneChangeStatus
{
    /// <summary>Esperando que un admin decida qué mover.</summary>
    Pending = 1,

    /// <summary>Se movió lo elegido.</summary>
    Applied = 2,

    /// <summary>El admin decidió no mover nada.</summary>
    Dismissed = 3,

    /// <summary>Llegó otro cambio antes de decidir; ese lo reemplaza.</summary>
    Superseded = 4,
}
