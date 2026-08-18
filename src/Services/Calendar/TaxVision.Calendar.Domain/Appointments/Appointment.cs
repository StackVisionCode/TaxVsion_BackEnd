using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Calendar.Domain.Appointments.Events;
using TaxVision.Calendar.Domain.Scheduling;
using TaxVision.Calendar.Domain.ValueObjects;

namespace TaxVision.Calendar.Domain.Appointments;

/// <summary>
/// El compromiso: cuando, quien asiste y en que zona horaria. La sala de video es de Communication;
/// aca solo se guarda el codigo que devuelve.
///
/// <para>
/// Solo el organizador mueve o cancela; los asistentes responden y nada mas. Sin esa regla dos
/// personas mueven la misma cita a la vez y gana la ultima en guardar.
/// </para>
/// </summary>
public sealed class Appointment : AggregateRoot, IHasOwner
{
    public const int MaxAttendees = 100;

    public AppointmentTitle Title { get; private set; } = default!;

    public string? Description { get; private set; }

    public Location? Location { get; private set; }

    public AppointmentStatus Status { get; private set; }

    public Guid AppointmentTypeId { get; private set; }

    /// <summary>Organizador. Unico que mueve o cancela.</summary>
    public Guid OrganizerUserId { get; private set; }

    public EventTiming Timing { get; private set; } = default!;

    /// <summary>Null = cita puntual. No lleva zona: la de la serie es la de <see cref="Timing"/>.</summary>
    public RecurrenceRule? Recurrence { get; private set; }

    public bool IsRecurring => Recurrence is not null;

    /// <summary>
    /// De que serie salio esta al partirse. Sin el rastro, nadie entiende por que hay dos series casi
    /// iguales y alguien «arregla» borrando una.
    /// </summary>
    public Guid? SplitFromSeriesId { get; private set; }

    public Guid? CustomerId { get; private set; }

    public int? TaxYear { get; private set; }

    public bool IsVirtual { get; private set; }

    /// <summary>Lo llena Communication por evento, nunca una llamada HTTP desde aca.</summary>
    public Guid? MeetingId { get; private set; }

    public string? MeetingShortCode { get; private set; }

    /// <summary>
    /// Minutos de antelacion del aviso, o null si no se pidio. Calendar no entrega el recordatorio:
    /// le pide a Reminder que lo haga.
    /// </summary>
    public int? ReminderLeadMinutes { get; private set; }

    public string? CancellationReason { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime? CancelledAtUtc { get; private set; }

    public byte[] RowVersion { get; private set; } = default!;

    private readonly List<AppointmentAttendee> _attendees = [];
    public IReadOnlyList<AppointmentAttendee> Attendees => _attendees;

    private readonly List<AppointmentException> _exceptions = [];
    public IReadOnlyList<AppointmentException> Exceptions => _exceptions;

    Guid IHasOwner.CreatedByUserId => OrganizerUserId;

    private Appointment() { }

    public static Result<Appointment> Schedule(
        Guid tenantId,
        AppointmentTitle title,
        EventTiming timing,
        Guid appointmentTypeId,
        Guid organizerUserId,
        DateTime nowUtc,
        string? description = null,
        Location? location = null,
        Guid? customerId = null,
        int? taxYear = null,
        bool isVirtual = false
    )
    {
        if (organizerUserId == Guid.Empty)
            return Result.Failure<Appointment>(AppointmentErrors.OrganizerRequired);

        if (appointmentTypeId == Guid.Empty)
            return Result.Failure<Appointment>(AppointmentErrors.TypeRequired);

        var appointment = new Appointment
        {
            Id = Guid.NewGuid(),
            Title = title,
            Description = description,
            Location = location,
            Status = AppointmentStatus.Confirmed,
            AppointmentTypeId = appointmentTypeId,
            OrganizerUserId = organizerUserId,
            Timing = timing,
            CustomerId = customerId,
            TaxYear = taxYear,
            IsVirtual = isVirtual,
            CreatedAtUtc = nowUtc,
        };
        appointment.SetTenant(tenantId);

        appointment.AddDomainEvent(
            new AppointmentScheduledDomainEvent(
                appointment.Id,
                tenantId,
                organizerUserId,
                appointmentTypeId,
                isVirtual,
                nowUtc
            )
        );

        return Result.Success(appointment);
    }

    public Result Reschedule(EventTiming timing, Guid actingUserId, DateTime nowUtc)
    {
        var allowed = EnsureOrganizer(actingUserId);
        if (allowed.IsFailure)
            return allowed;

        var previousStartUtc = Timing.StartUtc;
        Timing = timing;

        // Quien acepto el martes a las 9 no acepto el jueves a las 4.
        foreach (var attendee in _attendees)
            attendee.ResetResponse();

        AddDomainEvent(
            new AppointmentRescheduledDomainEvent(Id, TenantId, previousStartUtc, timing.StartUtc, actingUserId, nowUtc)
        );

        return Result.Success();
    }

    public Result Cancel(Guid actingUserId, string? reason, DateTime nowUtc)
    {
        if (Status == AppointmentStatus.Cancelled)
            return Result.Failure(AppointmentErrors.AlreadyCancelled);

        if (actingUserId != OrganizerUserId)
            return Result.Failure(AppointmentErrors.NotTheOrganizer);

        Status = AppointmentStatus.Cancelled;
        CancellationReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        CancelledAtUtc = nowUtc;

        AddDomainEvent(new AppointmentCancelledDomainEvent(Id, TenantId, actingUserId, CancellationReason, nowUtc));

        return Result.Success();
    }

    public Result ChangeTitle(AppointmentTitle title, Guid actingUserId)
    {
        var allowed = EnsureOrganizer(actingUserId);
        if (allowed.IsFailure)
            return allowed;

        Title = title;
        return Result.Success();
    }

    public Result ChangeLocation(Location? location, Guid actingUserId)
    {
        var allowed = EnsureOrganizer(actingUserId);
        if (allowed.IsFailure)
            return allowed;

        Location = location;
        return Result.Success();
    }

    public Result<AppointmentAttendee> AddAttendee(
        AttendeeKind kind,
        Guid? userId,
        Guid? customerId,
        AttendeeSnapshot snapshot,
        bool isRequired,
        Guid actingUserId,
        DateTime nowUtc
    )
    {
        var allowed = EnsureOrganizer(actingUserId);
        if (allowed.IsFailure)
            return Result.Failure<AppointmentAttendee>(allowed.Error);

        if (_attendees.Count >= MaxAttendees)
            return Result.Failure<AppointmentAttendee>(AppointmentErrors.TooManyAttendees);

        if (Find(userId, customerId, snapshot.Email) is not null)
            return Result.Failure<AppointmentAttendee>(AppointmentErrors.AttendeeAlreadyAdded);

        var attendee = AppointmentAttendee.Invite(Id, kind, userId, customerId, snapshot, isRequired);
        _attendees.Add(attendee);

        AddDomainEvent(new AppointmentAttendeeInvitedDomainEvent(Id, TenantId, attendee.Id, kind, nowUtc));

        return Result.Success(attendee);
    }

    public Result RemoveAttendee(Guid attendeeId, Guid actingUserId)
    {
        var allowed = EnsureOrganizer(actingUserId);
        if (allowed.IsFailure)
            return allowed;

        var attendee = FindById(attendeeId);
        if (attendee is null)
            return Result.Failure(AppointmentErrors.AttendeeNotFound);

        if (attendee.UserId == OrganizerUserId)
            return Result.Failure(AppointmentErrors.OrganizerCannotBeRemoved);

        _attendees.Remove(attendee);
        return Result.Success();
    }

    /// <summary>
    /// Lo unico que un asistente puede hacer sin ser organizador: si responder tambien lo exigiera, el
    /// RSVP no existiria.
    /// </summary>
    public Result RespondAsAttendee(
        AttendeeResponse response,
        Guid? userId,
        Guid? customerId,
        string? email,
        DateTime nowUtc
    )
    {
        if (Status == AppointmentStatus.Cancelled)
            return Result.Failure(AppointmentErrors.CancelledIsFinal);

        var attendee = Find(userId, customerId, email);
        if (attendee is null)
            return Result.Failure(AppointmentErrors.AttendeeNotFound);

        attendee.Respond(response, nowUtc);

        AddDomainEvent(new AppointmentAttendeeRespondedDomainEvent(Id, TenantId, attendee.Id, response, nowUtc));

        return Result.Success();
    }

    public void RequestReminder(int? leadMinutes) => ReminderLeadMinutes = leadMinutes is > 0 ? leadMinutes : null;

    /// <summary>La sala llega por evento desde Communication; si divergen, manda la cita.</summary>
    public void LinkMeeting(Guid meetingId, string shortCode)
    {
        MeetingId = meetingId;
        MeetingShortCode = shortCode;
    }

    // ── Serie ────────────────────────────────────────────────────────────────────────────────────

    public Result MakeRecurring(RecurrenceRule recurrence, EventTiming timing, Guid actingUserId)
    {
        var allowed = EnsureOrganizer(actingUserId);
        if (allowed.IsFailure)
            return allowed;

        // La regla dice cada cuánto; el timing dice desde cuándo y en qué zona. Uno sin el otro no
        // produce ocurrencias, así que entran juntos.
        if (timing.Kind != TimingKind.Recurring)
            return Result.Failure(TimingErrors.RecurringMustBeLocal);

        Timing = timing;
        Recurrence = recurrence;
        return Result.Success();
    }

    /// <summary>Cancela una ocurrencia sin tocar el resto de la serie.</summary>
    public Result CancelOccurrence(DateTime originalStartUtc, Guid actingUserId, DateTime nowUtc)
    {
        var allowed = EnsureCanEditOccurrence(originalStartUtc, actingUserId);
        if (allowed.IsFailure)
            return allowed;

        _exceptions.Add(AppointmentException.Cancel(Id, TenantId, originalStartUtc, actingUserId, nowUtc));

        AddDomainEvent(
            new AppointmentOccurrenceCancelledDomainEvent(Id, TenantId, originalStartUtc, actingUserId, nowUtc)
        );

        return Result.Success();
    }

    /// <summary>Mueve o cambia una sola ocurrencia.</summary>
    public Result OverrideOccurrence(
        DateTime originalStartUtc,
        DateTime? newStartUtc,
        DateTime? newEndUtc,
        string? newTitle,
        string? newLocation,
        Guid actingUserId,
        DateTime nowUtc
    )
    {
        var allowed = EnsureCanEditOccurrence(originalStartUtc, actingUserId);
        if (allowed.IsFailure)
            return allowed;

        var exception = AppointmentException.Override(
            Id,
            TenantId,
            originalStartUtc,
            newStartUtc,
            newEndUtc,
            newTitle,
            newLocation,
            actingUserId,
            nowUtc
        );

        if (exception.IsFailure)
            return Result.Failure(exception.Error);

        _exceptions.Add(exception.Value);

        AddDomainEvent(
            new AppointmentOccurrenceOverriddenDomainEvent(
                Id,
                TenantId,
                originalStartUtc,
                newStartUtc,
                actingUserId,
                nowUtc
            )
        );

        return Result.Success();
    }

    /// <summary>
    /// Edita la serie entera. Las excepciones existentes <b>se conservan</b>: siguen apuntando a las
    /// mismas ocurrencias por su <c>OriginalStartUtc</c>, que no cambia.
    /// </summary>
    public Result EditEntireSeries(EventTiming timing, RecurrenceRule recurrence, Guid actingUserId, DateTime nowUtc)
    {
        var allowed = EnsureOrganizer(actingUserId);
        if (allowed.IsFailure)
            return allowed;

        if (!IsRecurring)
            return Result.Failure(RecurrenceErrors.NotASeries);

        var previousStartUtc = Timing.StartUtc;
        Timing = timing;
        Recurrence = recurrence;

        foreach (var attendee in _attendees)
            attendee.ResetResponse();

        AddDomainEvent(
            new AppointmentRescheduledDomainEvent(Id, TenantId, previousStartUtc, timing.StartUtc, actingUserId, nowUtc)
        );

        return Result.Success();
    }

    /// <summary>
    /// «Esta y las siguientes»: parte la serie en vez de editarla. Editarla correria tambien enero y
    /// febrero, y eso falsea el historial — las citas pasadas ocurrieron a la hora vieja.
    ///
    /// <para>
    /// Devuelve la serie nueva. No parte sobre la primera ocurrencia: dejaria esta sin ninguna, y para
    /// eso esta <see cref="EditEntireSeries"/>.
    /// </para>
    /// </summary>
    public Result<Appointment> SplitForFollowing(
        DateTime cutOriginalStartUtc,
        EventTiming newTiming,
        RecurrenceRule newRecurrence,
        Guid actingUserId,
        DateTime nowUtc
    )
    {
        var allowed = EnsureOrganizer(actingUserId);
        if (allowed.IsFailure)
            return Result.Failure<Appointment>(allowed.Error);

        if (!IsRecurring)
            return Result.Failure<Appointment>(RecurrenceErrors.NotASeries);

        if (!OccurrenceExpander.IsOccurrence(this, cutOriginalStartUtc))
            return Result.Failure<Appointment>(RecurrenceErrors.NotAnOccurrence);

        if (IsFirstOccurrence(cutOriginalStartUtc))
            return Result.Failure<Appointment>(RecurrenceErrors.SplitOnFirstOccurrence);

        // UNTIL de la vieja: un instante antes del corte, en UTC como exige el RFC. Un segundo
        // alcanza y no deja fuera ninguna ocurrencia legitima.
        var limited = Recurrence!.EndingAt(cutOriginalStartUtc.AddSeconds(-1));
        if (limited.IsFailure)
            return Result.Failure<Appointment>(limited.Error);

        Recurrence = limited.Value;

        // Las excepciones desde el corte ya no le pertenecen a esta serie: heredarlas reviviria
        // cancelaciones de fechas que ya no produce.
        DiscardExceptionsFrom(cutOriginalStartUtc);

        var follower = new Appointment
        {
            Id = Guid.NewGuid(),
            Title = Title,
            Description = Description,
            Location = Location,
            Status = AppointmentStatus.Confirmed,
            AppointmentTypeId = AppointmentTypeId,
            OrganizerUserId = OrganizerUserId,
            // Copia: la misma instancia en dos agregados deja uno sin timing en la BD.
            // Copia: la misma instancia en dos agregados deja uno sin timing en la BD.
            Timing = newTiming.Copy(),
            Recurrence = newRecurrence,
            SplitFromSeriesId = Id,
            CustomerId = CustomerId,
            TaxYear = TaxYear,
            IsVirtual = IsVirtual,
            // No hereda MeetingId ni ShortCode: es otra serie y necesita su propia sala.
            CreatedAtUtc = nowUtc,
        };
        follower.SetTenant(TenantId);

        foreach (var attendee in _attendees)
        {
            follower._attendees.Add(
                AppointmentAttendee.Invite(
                    follower.Id,
                    attendee.Kind,
                    attendee.UserId,
                    attendee.CustomerId,
                    attendee.Snapshot.Copy(),
                    attendee.IsRequired
                )
            );
        }

        follower.AddDomainEvent(
            new AppointmentSeriesSplitDomainEvent(Id, follower.Id, TenantId, cutOriginalStartUtc, actingUserId, nowUtc)
        );

        return Result.Success(follower);
    }

    public bool IsFirstOccurrence(DateTime originalStartUtc) => OccurrenceExpander.FirstStart(this) == originalStartUtc;

    private Result EnsureCanEditOccurrence(DateTime originalStartUtc, Guid actingUserId)
    {
        var allowed = EnsureOrganizer(actingUserId);
        if (allowed.IsFailure)
            return allowed;

        // Sin serie no hay ocurrencias que excepcionar.
        if (!IsRecurring)
            return Result.Failure(RecurrenceErrors.NotRecurring);

        // Antes de comprobar que sea ocurrencia: «duplicada» explica mejor que «no es ocurrencia».
        if (FindException(originalStartUtc) is not null)
            return Result.Failure(RecurrenceErrors.DuplicateException);

        return OccurrenceExpander.IsOccurrence(this, originalStartUtc)
            ? Result.Success()
            : Result.Failure(RecurrenceErrors.NotAnOccurrence);
    }

    private void DiscardExceptionsFrom(DateTime cutUtc)
    {
        var kept = new List<AppointmentException>();

        foreach (var exception in _exceptions)
        {
            if (exception.OriginalStartUtc < cutUtc)
                kept.Add(exception);
        }

        _exceptions.Clear();
        _exceptions.AddRange(kept);
    }

    private AppointmentException? FindException(DateTime originalStartUtc)
    {
        foreach (var exception in _exceptions)
        {
            if (exception.OriginalStartUtc == originalStartUtc)
                return exception;
        }

        return null;
    }

    private Result EnsureOrganizer(Guid actingUserId)
    {
        if (Status == AppointmentStatus.Cancelled)
            return Result.Failure(AppointmentErrors.CancelledIsFinal);

        return actingUserId == OrganizerUserId ? Result.Success() : Result.Failure(AppointmentErrors.NotTheOrganizer);
    }

    private AppointmentAttendee? Find(Guid? userId, Guid? customerId, string? email)
    {
        foreach (var attendee in _attendees)
        {
            if (attendee.Matches(userId, customerId, email))
                return attendee;
        }

        return null;
    }

    private AppointmentAttendee? FindById(Guid attendeeId)
    {
        foreach (var attendee in _attendees)
        {
            if (attendee.Id == attendeeId)
                return attendee;
        }

        return null;
    }
}
