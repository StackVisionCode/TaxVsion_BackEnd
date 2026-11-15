using System.Security.Cryptography;
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

    [UserReference(
        UserReferenceKind.ActiveWork,
        HandledBy = "CalendarUserOffboardedConsumer",
        Note = "Asistente interno; sale de las citas vigentes."
    )]
    public Guid? UserId { get; private set; }

    public Guid? CustomerId { get; private set; }

    public AttendeeSnapshot Snapshot { get; private set; } = default!;

    public bool IsRequired { get; private set; }

    public AttendeeResponse Response { get; private set; }

    public DateTime? RespondedAtUtc { get; private set; }

    /// <summary>SHA-256 del enlace de respuesta del correo. Null en los invitados anteriores al enlace.</summary>
    public byte[]? RsvpTokenHash { get; private set; }

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

    /// <summary>Un enlace nuevo invalida el anterior: el de la hora vieja ya no responde por la nueva.</summary>
    internal AttendeeRsvpToken IssueRsvpToken()
    {
        var token = RsvpToken.Create();
        RsvpTokenHash = token.Hash;
        return new AttendeeRsvpToken(Id, token.Value);
    }

    internal bool HasRsvpToken(byte[] hash) =>
        RsvpTokenHash is not null && CryptographicOperations.FixedTimeEquals(RsvpTokenHash, hash);

    internal void Respond(AttendeeResponse response, DateTime nowUtc)
    {
        Response = response;
        RespondedAtUtc = nowUtc;
    }

    /// <summary>
    /// Al mover la cita las respuestas vuelven a cero: quien acepto el martes a las 9 no acepto el
    /// jueves a las 4. Dar por buena la respuesta vieja es como se llega a una sala vacia.
    /// </summary>
    /// <summary>Borra quién era (anonimización): queda la fila, sin nombre real, correo ni enlace de respuesta.</summary>
    internal void Forget(AttendeeSnapshot anonymized)
    {
        Snapshot = anonymized;
        RsvpTokenHash = null;
    }

    internal void ResetResponse()
    {
        Response = AttendeeResponse.NeedsAction;
        RespondedAtUtc = null;
    }

    /// <summary>
    /// La misma persona: por su usuario o su cliente. El correo solo cuenta si uno de los dos es un
    /// externo (sin usuario ni cliente): un empleado y un cliente que comparten correo son dos personas.
    /// </summary>
    internal bool Matches(Guid? userId, Guid? customerId, string? email) =>
        (userId is not null && userId == UserId)
        || (customerId is not null && customerId == CustomerId)
        || (
            email is not null
            && Snapshot.Email is not null
            && string.Equals(email, Snapshot.Email, StringComparison.OrdinalIgnoreCase)
            && ((userId is null && customerId is null) || (UserId is null && CustomerId is null))
        );
}
