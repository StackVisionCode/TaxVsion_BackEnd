namespace TaxVision.CloudStorage.Domain.Legal;

public enum DmcaNoticeStatus
{
    /// <summary>Notificacion recibida y accionada: el archivo ya esta bloqueado + bajo legal hold.</summary>
    Received,

    /// <summary>El tenant/uploader presento contranotificacion; en espera de resolucion del equipo legal.</summary>
    CounterNoticeSubmitted,

    /// <summary>El equipo legal reinstalo el archivo (reclamo retirado o contranotificacion aceptada).</summary>
    Reinstated,
}
