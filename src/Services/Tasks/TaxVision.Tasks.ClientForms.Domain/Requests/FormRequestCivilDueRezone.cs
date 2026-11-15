using BuildingBlocks.TimeZones;

namespace TaxVision.Tasks.ClientForms.Domain.Requests;

/// <summary>
/// P9: la oficina cambió de zona. Un formulario abierto que vencía al fin del día en la zona anterior pasa al fin del
/// mismo día en la nueva. Vive aparte para no engordar el agregado (tope de líneas).
/// </summary>
public static class FormRequestCivilDueRezone
{
    public static bool RezoneCivilDue(this FormRequest request, string previousTimeZoneId, ResolvedTimeZone newZone)
    {
        if (request.Status is FormRequestStatus.Completed or FormRequestStatus.Cancelled)
            return false;
        if (
            request.DueOn is not { } day
            || request.DueAtUtc != LocalDay.EndUtc(day, ResolvedTimeZone.Resolve(previousTimeZoneId))
        )
            return false;
        request.DueAtUtc = LocalDay.EndUtc(day, newZone);
        return true;
    }
}
