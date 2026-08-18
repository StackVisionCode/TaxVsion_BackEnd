using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Calendar.Domain.Appointments;

/// <summary>
/// Una ocurrencia de la serie que no sigue la regla: se canceló o se cambió.
///
/// <para>
/// <b>Es entidad hija, no agregado propio</b>: no tiene sentido fuera de su serie, siempre se carga
/// con ella, y su invariante —no puede haber dos excepciones para la misma ocurrencia— necesita ver a
/// las hermanas. O sea, cabe justo dentro del límite de consistencia.
/// </para>
/// </summary>
public sealed class AppointmentException : BaseEntity, ITenantOwned
{
    public Guid AppointmentId { get; private set; }

    public Guid TenantId { get; private set; }

    /// <summary>
    /// El <c>RECURRENCE-ID</c> del RFC 5545: identifica <b>qué</b> ocurrencia. Es el inicio que la
    /// serie habria producido, no el nuevo. No tiene setter porque cambiarlo no edita la
    /// excepción: la reapunta a otra ocurrencia y deja huérfana a la original.
    /// </summary>
    public DateTime OriginalStartUtc { get; private set; }

    public ExceptionKind Kind { get; private set; }

    public DateTime? NewStartUtc { get; private set; }

    public DateTime? NewEndUtc { get; private set; }

    public string? NewTitle { get; private set; }

    public string? NewLocation { get; private set; }

    public Guid ModifiedByUserId { get; private set; }

    public DateTime ModifiedAtUtc { get; private set; }

    /// <summary>
    /// Explícito a propósito: el filtro global fail-closed necesita la interfaz, pero re-tenantear
    /// una excepción no es una operación que exista. El tenant lo pone la serie al crearla.
    /// </summary>
    void ITenantOwned.SetTenant(Guid tenantId) => TenantId = tenantId;

    private AppointmentException() { }

    internal static AppointmentException Cancel(
        Guid appointmentId,
        Guid tenantId,
        DateTime originalStartUtc,
        Guid modifiedByUserId,
        DateTime nowUtc
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            AppointmentId = appointmentId,
            TenantId = tenantId,
            OriginalStartUtc = originalStartUtc,
            Kind = ExceptionKind.Cancelled,
            ModifiedByUserId = modifiedByUserId,
            ModifiedAtUtc = nowUtc,
        };

    internal static Result<AppointmentException> Override(
        Guid appointmentId,
        Guid tenantId,
        DateTime originalStartUtc,
        DateTime? newStartUtc,
        DateTime? newEndUtc,
        string? newTitle,
        string? newLocation,
        Guid modifiedByUserId,
        DateTime nowUtc
    )
    {
        // Una excepcion que no cambia nada es una fila que no explica nada.
        if (newStartUtc is null && newEndUtc is null && newTitle is null && newLocation is null)
            return Result.Failure<AppointmentException>(RecurrenceErrors.EmptyOverride);

        if (newStartUtc is not null && newEndUtc is not null && newEndUtc <= newStartUtc)
            return Result.Failure<AppointmentException>(TimingErrors.EndBeforeStart);

        if (
            (newStartUtc is not null && newStartUtc.Value.Kind != DateTimeKind.Utc)
            || (newEndUtc is not null && newEndUtc.Value.Kind != DateTimeKind.Utc)
        )
        {
            return Result.Failure<AppointmentException>(TimingErrors.NotUtc);
        }

        return Result.Success(
            new AppointmentException
            {
                Id = Guid.NewGuid(),
                AppointmentId = appointmentId,
                TenantId = tenantId,
                OriginalStartUtc = originalStartUtc,
                Kind = ExceptionKind.Overridden,
                NewStartUtc = newStartUtc,
                NewEndUtc = newEndUtc,
                NewTitle = newTitle,
                NewLocation = newLocation,
                ModifiedByUserId = modifiedByUserId,
                ModifiedAtUtc = nowUtc,
            }
        );
    }
}
