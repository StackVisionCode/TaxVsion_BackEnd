namespace TaxVision.Tenant.Domain.Team;

/// <summary>El historial guarda 13 meses: una temporada fiscal completa más un mes.</summary>
public static class ActivityRetention
{
    public const int Months = 13;

    /// <summary>Lo que ocurrió antes de este instante se purga.</summary>
    public static DateTime CutoffFor(DateTime nowUtc) => nowUtc.AddMonths(-Months);
}
