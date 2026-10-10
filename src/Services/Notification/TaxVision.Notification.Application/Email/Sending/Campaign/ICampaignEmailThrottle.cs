namespace TaxVision.Notification.Application.Email.Sending.Campaign;

/// <summary>
/// Limitador de ritmo compartido del ejecutor de email de campaña: pacea los envíos a N/min para respetar
/// el límite de la cuenta del proveedor, incluso cuando varios dispatch se procesan en paralelo. Los
/// adapters llaman <see cref="WaitTurnAsync"/> antes de cada envío.
/// </summary>
public interface ICampaignEmailThrottle
{
    Task WaitTurnAsync(CancellationToken ct = default);
}
