namespace TaxVision.Tasks.Domain.Projections;

/// <summary>Espejo local del status real de Customer (ver <c>CustomerStatusFilter</c> en ese servicio).</summary>
public enum CustomerDirectoryStatus
{
    Active,
    Inactive,
    Archived,
}
