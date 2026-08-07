namespace TaxVision.Notes.Domain.Projections;

/// <summary>Espejo local del status real de Customer (ver <c>CustomerStatusFilter</c> del servicio Customer).</summary>
public enum CustomerDirectoryStatus
{
    Active,
    Inactive,
    Archived,
}
