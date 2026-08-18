namespace TaxVision.Calendar.Domain.Appointments;

/// <summary>
/// Sobre que recae la edicion de una serie. No tiene valor por defecto y no debe tenerlo: elegir
/// <see cref="EntireSeries"/> en silencio reescribe el pasado, y elegir <see cref="ThisOccurrence"/>
/// frustra al que queria mover todo. El request sin scope se rechaza.
/// </summary>
public enum EditScope
{
    ThisOccurrence = 1,
    ThisAndFollowing = 2,
    EntireSeries = 3,
}
