using BuildingBlocks.Results;

namespace TaxVision.Notification.Application.Abstractions;

/// <summary>
/// Orquesta la entrega efectiva de un correo saliente ya persistido: resuelve la configuración de
/// proveedor, envía (SMTP hoy; API en fases futuras), actualiza estado/tracking y persiste. Lo invoca
/// el consumer async (fuera del request). El cuerpo ya viene renderizado en el mensaje.
/// </summary>
public interface IEmailDeliveryService
{
    Task<Result> DeliverAsync(Guid messageId, CancellationToken ct = default);
}
