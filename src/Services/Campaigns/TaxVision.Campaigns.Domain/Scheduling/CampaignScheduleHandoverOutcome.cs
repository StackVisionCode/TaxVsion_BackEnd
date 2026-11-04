namespace TaxVision.Campaigns.Domain.Scheduling;

/// <summary>Qué le pasó a un agendado cuando quien lo disparaba se retiró o dejó de poder enviar.</summary>
public enum CampaignScheduleHandoverOutcome
{
    /// <summary>Pasó al sucesor (dispara en su nombre y con su visibilidad).</summary>
    Reassigned = 0,

    /// <summary>Sin sucesor elegible: quedó pausado hasta que un admin decida.</summary>
    Paused = 1,

    /// <summary>Un admin lo reanudó y, como el ejecutor ya no podía enviar, pasó a dispararse en su nombre.</summary>
    ResumedByAdmin = 2,
}
