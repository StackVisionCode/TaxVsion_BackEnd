namespace TaxVision.Tasks.ClientForms.Domain.Participants;

/// <summary>
/// TSK-6: envíos de captación de un tenant en un día UTC. Solo lo escribe la sentencia atómica del repositorio, dentro
/// de la transacción del envío: si el envío se revierte, el cupo vuelve solo.
/// </summary>
public sealed class FormIntakeDailyCounter
{
    private FormIntakeDailyCounter() { }

    public Guid TenantId { get; private set; }
    public DateTime DayUtc { get; private set; }
    public int Count { get; private set; }
}
