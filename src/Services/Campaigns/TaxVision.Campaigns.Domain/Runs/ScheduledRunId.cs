using System.Security.Cryptography;
using System.Text;

namespace TaxVision.Campaigns.Domain.Runs;

/// <summary>
/// Id determinista del run de un agendado: el mismo (agendado, slot) siempre da el mismo Id. Así dos
/// disparos del mismo slot chocan por clave primaria y la reserva del Wallet reusa la referencia.
/// </summary>
public static class ScheduledRunId
{
    public static Guid For(Guid scheduleId, DateTime slotUtc)
    {
        // El slot que vuelve de SQL trae Kind Unspecified: se fija Utc para que el texto no cambie.
        var slot = DateTime.SpecifyKind(slotUtc, DateTimeKind.Utc);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{scheduleId:N}|{slot:O}"));
        return new Guid(hash[..16]);
    }
}
