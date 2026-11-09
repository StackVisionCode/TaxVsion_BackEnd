namespace TaxVision.Correspondence.Application.Compose;

/// <summary>Plazos del job que cierra los borradores en <c>Sending</c> (COR-08).</summary>
public sealed class DraftSendReconciliationOptions
{
    public const string SectionName = "Correspondence:DraftSendReconciliation";

    public bool Enabled { get; set; } = true;

    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Menor que la espera larga de BB-7: el aviso de entrega adelantado encuentra el borrador cerrado.</summary>
    public TimeSpan StuckAfter { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Sin resultado de Postmaster pasado este plazo, se cierra como resultado desconocido.</summary>
    public TimeSpan GiveUpAfter { get; set; } = TimeSpan.FromHours(2);

    public int BatchSize { get; set; } = 50;
}
