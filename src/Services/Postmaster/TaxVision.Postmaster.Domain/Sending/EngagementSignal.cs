namespace TaxVision.Postmaster.Domain.Sending;

/// <summary>Lo que hizo el destinatario con el correo. No es un estado: se cuenta, no reemplaza al outcome.</summary>
public enum EngagementSignal
{
    Opened,
    Clicked,
    Unsubscribed,
}
