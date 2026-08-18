namespace TaxVision.Tasks.Domain.Series;

public enum SeriesStatus
{
    Active = 1,

    /// <summary>No materializa. Al reanudar siembra desde ese momento, no desde donde quedó.</summary>
    Paused = 2,

    /// <summary>Agotada por fecha de fin, por tope de ocurrencias o por la propia regla.</summary>
    Ended = 3,
}
