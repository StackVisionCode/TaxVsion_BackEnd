namespace TaxVision.Postmaster.Domain.Sending;

/// <summary>Por dónde salió el correo: el proveedor del sistema o el buzón conectado del tenant.</summary>
public enum DeliveryLane
{
    SystemProvider,
    TenantMailbox,
}
