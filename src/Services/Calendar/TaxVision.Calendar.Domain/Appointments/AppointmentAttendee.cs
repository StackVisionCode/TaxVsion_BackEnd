using BuildingBlocks.Domain;
using TaxVision.Calendar.Domain.ValueObjects;

namespace TaxVision.Calendar.Domain.Appointments;

/// <summary>
/// Un invitado a la cita. El nombre y el correo son snapshot: la cita del ano pasado muestra el
/// nombre que la persona tenia entonces, y crear una cita no llama a nadie.
/// </summary>
public sealed class AppointmentAttendee : BaseEntity
{
    public Guid AppointmentId { get; private set; }

    public AttendeeKind Kind { get; private set; }

    public Guid? UserId { get; private set; }

    public Guid? CustomerId { get; private set; }

    public AttendeeSnapshot Snapshot { get; private set; } = default!;

    public bool IsRequired { get; private set; }

    public AttendeeResponse Response { get; private set; }

    public DateTime? RespondedAtUtc { get; private set; }

    private AppointmentAttendee() { }

    internal static AppointmentAttendee Invite(
        Guid appointmentId,
        AttendeeKind kind,
        Guid? userId,
        Guid? customerId,
        AttendeeSnapshot snapshot,
        bool isRequired
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            AppointmentId = appointmentId,
            Kind = kind,
            UserId = userId,
            CustomerId = customerId,
            Snapshot = snapshot,
            IsRequired = isRequired,
            Response = AttendeeResponse.NeedsAction,
        };

    internal void Respond(AttendeeResponse response, DateTime nowUtc)
    {
        Response = response;
        RespondedAtUtc = nowUtc;
    }

    /// <summary>
    /// Al mover la cita las respuestas vuelven a cero: quien acepto el martes a las 9 no acepto el
    /// jueves a las 4. Dar por buena la respuesta vieja es como se llega a una sala vacia.
    /// </summary>
    internal void ResetResponse()
    {
        Response = AttendeeResponse.NeedsAction;
        RespondedAtUtc = null;
    }

    internal bool Matches(Guid? userId, Guid? customerId, string? email) =>
        (userId is not null && userId == UserId)
        || (customerId is not null && customerId == CustomerId)
        || (
            email is not null
            && Snapshot.Email is not null
            && string.Equals(email, Snapshot.Email, StringComparison.OrdinalIgnoreCase)
        );
}
