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

    /// <summary>El límite de la columna: pasarlo era un 500 en vez de un 400.</summary>
    public const int MaxDescriptionLength = 4000;

    public AppointmentTitle Title { get; private set; } = default!;

    public string? Description { get; private set; }

    public Location? Location { get; private set; }

    public AppointmentStatus Status { get; private set; }

    public Guid AppointmentTypeId { get; private set; }

    /// <summary>Organizador. Unico que mueve o cancela.</summary>
    [UserReference(UserReferenceKind.ActiveWork, HandledBy = "CalendarUserOffboardedConsumer")]
    public Guid OrganizerUserId { get; private set; }

    public EventTiming Timing { get; private set; } = default!;

    /// <summary>Null = cita puntual. No lleva zona: la de la serie es la de <see cref="Timing"/>.</summary>
    public RecurrenceRule? Recurrence { get; private set; }

    public bool IsRecurring => Recurrence is not null;

    /// <summary>Cuándo ocurre, con sus excepciones. Cambia al reprogramar; un RSVP no la mueve.</summary>
    public string ScheduleStamp
    {
        get
        {
            var exceptions = new List<string>(_exceptions.Count);
            foreach (var e in _exceptions)
                exceptions.Add($"{e.OriginalStartUtc:O}={e.Kind}:{e.NewStartUtc:O}:{e.NewEndUtc:O}");

            exceptions.Sort(StringComparer.Ordinal);

            return string.Join(
                '|',
                Timing.Kind,
                Timing.TimeZone.Id,
                $"{Timing.StartUtc:O}",
                $"{Timing.EndUtc:O}",
                $"{Timing.StartDate:O}",
                $"{Timing.EndDate:O}",
                $"{Timing.SeriesStartDate:O}",
                $"{Timing.LocalStartTime:O}",
                Timing.Duration,
                Recurrence?.Value,
                string.Join(',', exceptions)
            );
        }
    }

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

    /// <summary>Tope del aviso: una semana antes.</summary>
    public const int MaxReminderLeadMinutes = 7 * 24 * 60;

    /// <summary>
    /// Entra en la clave de los pedidos a Reminder. Sube cuando ningún aviso pedido sigue vigente,
    /// para que volver a pedir un objetivo ya cerrado no choque con su clave vieja.
    /// </summary>
    public int ReminderGeneration { get; private set; }

    public string? CancellationReason { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime? CancelledAtUtc { get; private set; }

    public byte[] RowVersion { get; private set; } = default!;

    /// <summary>
    /// Sube con cada cambio en asistentes o excepciones. Esos cambios no tocan la fila de la cita, y sin
    /// <c>UPDATE</c> en la raíz <see cref="RowVersion"/> no detecta dos ediciones a la vez.
    /// </summary>
    public int ChildrenRevision { get; private set; }

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
        bool isVirtual = false,
        IReadOnlyList<InitialAttendee>? attendees = null,
        string? bookedByCustomerName = null,
        bool awaitingApproval = false
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
            // Pedida en el portal con aprobación: espera a la oficina, pero ya aparta el horario.
            Status = awaitingApproval ? AppointmentStatus.Requested : AppointmentStatus.Confirmed,
            AppointmentTypeId = appointmentTypeId,
            OrganizerUserId = organizerUserId,
            Timing = timing,
            CustomerId = customerId,
            TaxYear = taxYear,
            IsVirtual = isVirtual,
            CreatedAtUtc = nowUtc,
        };
        appointment.SetTenant(tenantId);

        // Llegan con el alta: el aviso de "cita nueva" ya los incluye, así que no se anuncian aparte.
        var rsvpTokens = new List<AttendeeRsvpToken>();
        foreach (var initial in attendees ?? [])
        {
            if (appointment._attendees.Count >= MaxAttendees)
                return Result.Failure<Appointment>(AppointmentErrors.TooManyAttendees);
            if (appointment.Find(initial.UserId, initial.CustomerId, initial.Snapshot.Email) is not null)
                continue;
            var attendee = AppointmentAttendee.Invite(
                appointment.Id,
                initial.Kind,
                initial.UserId,
                initial.CustomerId,
                initial.Snapshot,
                initial.IsRequired
            );
            if (initial.Accepted)
                attendee.Respond(AttendeeResponse.Accepted, nowUtc);
            rsvpTokens.Add(attendee.IssueRsvpToken());
            appointment._attendees.Add(attendee);
        }

        // Una solicitud no se anuncia como cita: sin sala, avisos ni recordatorios hasta que se apruebe.
        if (awaitingApproval)
        {
            appointment.AddDomainEvent(
                new AppointmentBookingRequestedDomainEvent(
                    appointment.Id,
                    tenantId,
                    organizerUserId,
                    string.IsNullOrWhiteSpace(bookedByCustomerName) ? null : bookedByCustomerName.Trim(),
                    nowUtc
                )
            );
            return Result.Success(appointment);
        }

        appointment.AddDomainEvent(
            new AppointmentScheduledDomainEvent(
                appointment.Id,
                tenantId,
                organizerUserId,
                appointmentTypeId,
                isVirtual,
                nowUtc,
                [.. rsvpTokens],
                // La reservó el cliente en el portal: el aviso al preparador lo dice.
                string.IsNullOrWhiteSpace(bookedByCustomerName)
                    ? null
                    : bookedByCustomerName.Trim()
            )
        );

        return Result.Success(appointment);
    }

    public Result Reschedule(EventTiming timing, Guid actingUserId, bool canManageAll, DateTime nowUtc)
    {
        var allowed = EnsureOrganizer(actingUserId, canManageAll);
        if (allowed.IsFailure)
            return allowed;

        ApplyReschedule(timing, actingUserId, nowUtc, keepGoingCustomerId: null);
        return Result.Success();
    }

    /// <summary>
    /// La oficina aprueba la solicitud del cliente: queda confirmada y recién ahí se anuncia como una cita
    /// nueva (sala, avisos, recordatorios). Con <paramref name="assignee"/> se la da a otro empleado, que
    /// pasa a organizarla y la acepta.
    /// </summary>
    public Result ApproveRequest(Guid actingUserId, bool canManageAll, AppointmentAssignee? assignee, DateTime nowUtc)
    {
        if (Status != AppointmentStatus.Requested)
            return Result.Failure(AppointmentErrors.NotARequest);
        if (!canManageAll && actingUserId != OrganizerUserId)
            return Result.Failure(AppointmentErrors.NotTheOrganizer);

        if (assignee is not null && assignee.UserId != OrganizerUserId)
            HandTo(assignee, nowUtc);

        Status = AppointmentStatus.Confirmed;
        var rsvpTokens = new List<AttendeeRsvpToken>();
        foreach (var attendee in _attendees)
            rsvpTokens.Add(attendee.IssueRsvpToken());

        AddDomainEvent(
            new AppointmentScheduledDomainEvent(
                Id,
                TenantId,
                OrganizerUserId,
                AppointmentTypeId,
                IsVirtual,
                nowUtc,
                [.. rsvpTokens],
                ApprovedRequest: true
            )
        );
        return Result.Success();
    }

    /// <summary>La oficina rechaza la solicitud: se libera el horario y el cliente se entera del motivo.</summary>
    public Result DeclineRequest(Guid actingUserId, bool canManageAll, string? reason, DateTime nowUtc)
    {
        if (Status != AppointmentStatus.Requested)
            return Result.Failure(AppointmentErrors.NotARequest);
        if (!canManageAll && actingUserId != OrganizerUserId)
            return Result.Failure(AppointmentErrors.NotTheOrganizer);

        Status = AppointmentStatus.Cancelled;
        CancellationReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        CancelledAtUtc = nowUtc;
        AddDomainEvent(
            new AppointmentBookingDeclinedDomainEvent(Id, TenantId, CancellationReason, actingUserId, nowUtc)
        );
        return Result.Success();
    }

    /// <summary>El empleado nuevo organiza y acepta; el anterior deja de figurar como asistente.</summary>
    private void HandTo(AppointmentAssignee assignee, DateTime nowUtc)
    {
        var previous = OrganizerUserId;
        _attendees.RemoveAll(a => a.UserId == previous);
        OrganizerUserId = assignee.UserId;

        var existing = Find(assignee.UserId, null, null);
        if (existing is null)
        {
            existing = AppointmentAttendee.Invite(
                Id,
                AttendeeKind.InternalUser,
                assignee.UserId,
                null,
                assignee.Snapshot,
                true
            );
            _attendees.Add(existing);
        }

        existing.Respond(AttendeeResponse.Accepted, nowUtc);
        ChildrenRevision++;
    }

    /// <summary>
    /// El cliente de la cita la cancela desde el portal. Solo su propia cita y nunca una serie: una serie
    /// se cambia con la oficina. El plazo lo pone la política de la oficina, que es otro agregado.
    /// </summary>
    public Result CancelByCustomer(Guid customerId, Guid portalUserId, string? reason, DateTime nowUtc)
    {
        var allowed = EnsureCustomerCanChange(customerId);
        if (allowed.IsFailure)
            return allowed;

        ApplyCancel(portalUserId, reason, nowUtc, null, null);
        return Result.Success();
    }

    /// <summary>
    /// El cliente mueve su cita. Las respuestas de los demás vuelven a cero, pero él eligió la hora nueva:
    /// sigue yendo, sin un "respondió" aparte. Un solo hecho: se movió.
    /// </summary>
    public Result RescheduleByCustomer(EventTiming timing, Guid customerId, Guid portalUserId, DateTime nowUtc)
    {
        var allowed = EnsureCustomerCanChange(customerId);
        if (allowed.IsFailure)
            return allowed;

        ApplyReschedule(timing, portalUserId, nowUtc, keepGoingCustomerId: customerId);
        return Result.Success();
    }

    /// <summary>Es del cliente: por su <see cref="CustomerId"/> o porque está invitado como cliente.</summary>
    public bool BelongsToCustomer(Guid customerId)
    {
        if (CustomerId == customerId)
            return true;
        foreach (var attendee in _attendees)
        {
            if (attendee.CustomerId == customerId)
                return true;
        }

        return false;
    }

    private Result EnsureCustomerCanChange(Guid customerId)
    {
        if (Status == AppointmentStatus.Cancelled)
            return Result.Failure(AppointmentErrors.CancelledIsFinal);
        if (!BelongsToCustomer(customerId))
            return Result.Failure(AppointmentErrors.NotTheCustomer);
        return IsRecurring ? Result.Failure(AppointmentErrors.SeriesChangedByOffice) : Result.Success();
    }

    private void ApplyReschedule(EventTiming timing, Guid actingUserId, DateTime nowUtc, Guid? keepGoingCustomerId)
    {
        var previousStartUtc = Timing.StartUtc;
        Timing = timing;

        // Quien acepto el martes a las 9 no acepto el jueves a las 4.
        var rsvpTokens = ResetResponses();
        if (keepGoingCustomerId is { } customerId)
        {
            foreach (var attendee in _attendees)
            {
                if (attendee.CustomerId == customerId)
                    attendee.Respond(AttendeeResponse.Accepted, nowUtc);
            }
        }

        AddDomainEvent(
            new AppointmentRescheduledDomainEvent(
                Id,
                TenantId,
                previousStartUtc,
                timing.StartUtc,
                timing.EndUtc,
                timing.TimeZone.Id,
                actingUserId,
                nowUtc,
                rsvpTokens
            )
        );
    }

    /// <param name="scope">Alcance que se anuncia; null es la cita entera.</param>
    /// <param name="originalStartUtc">Ocurrencia que venía en el pedido, si la hubo.</param>
    public Result Cancel(
        Guid actingUserId,
        bool canManageAll,
        string? reason,
        DateTime nowUtc,
        EditScope? scope = null,
        DateTime? originalStartUtc = null
    )
    {
        if (Status == AppointmentStatus.Cancelled)
            return Result.Failure(AppointmentErrors.AlreadyCancelled);

        if (!canManageAll && actingUserId != OrganizerUserId)
            return Result.Failure(AppointmentErrors.NotTheOrganizer);

        ApplyCancel(actingUserId, reason, nowUtc, scope, originalStartUtc);
        return Result.Success();
    }

    private void ApplyCancel(
        Guid actingUserId,
        string? reason,
        DateTime nowUtc,
        EditScope? scope,
        DateTime? originalStartUtc
    )
    {
        Status = AppointmentStatus.Cancelled;
        CancellationReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        CancelledAtUtc = nowUtc;

        AddDomainEvent(
            new AppointmentCancelledDomainEvent(
                Id,
                TenantId,
                actingUserId,
                CancellationReason,
                nowUtc,
                scope,
                originalStartUtc
            )
        );
    }

    /// <summary>
    /// Reasigna el organizador al RETIRAR (offboard) al actual — acción de SISTEMA, no del organizador,
    /// por eso NO pasa por <see cref="EnsureOrganizer"/> (que exigiría que el actor fuera el propio
    /// organizador que ya se fue). Solo sobre citas no canceladas. No resetea respuestas de asistentes:
    /// no cambia el cuándo, solo quién la administra ahora. Idempotente.
    /// </summary>
    public Result ReassignOrganizer(Guid newOrganizerUserId)
    {
        if (newOrganizerUserId == Guid.Empty)
            return Result.Failure(AppointmentErrors.OrganizerRequired);

        if (Status == AppointmentStatus.Cancelled)
            return Result.Failure(AppointmentErrors.CancelledIsFinal);

        if (newOrganizerUserId == OrganizerUserId)
            return Result.Success();

        OrganizerUserId = newOrganizerUserId;
        return Result.Success();
    }

    /// <summary>
    /// Saca a un usuario interno de los asistentes al RETIRARLO (offboard). Acción de sistema: no pasa
    /// por <see cref="EnsureOrganizer"/>. Las canceladas no se tocan (historial) y el organizador no se
    /// saca (antes hay que reasignarlo). Sin él en la lista devuelve <c>AttendeeNotFound</c>: así un
    /// reintento es un no-op.
    /// </summary>
    public Result RemoveDepartedStaffAttendee(Guid userId)
    {
        if (Status == AppointmentStatus.Cancelled)
            return Result.Failure(AppointmentErrors.CancelledIsFinal);

        if (userId == OrganizerUserId)
            return Result.Failure(AppointmentErrors.OrganizerCannotBeRemoved);

        if (_attendees.RemoveAll(a => a.UserId == userId) == 0)
            return Result.Failure(AppointmentErrors.AttendeeNotFound);

        ChildrenRevision++;
        return Result.Success();
    }

    public Result ChangeTitle(AppointmentTitle title, Guid actingUserId, bool canManageAll)
    {
        var allowed = EnsureOrganizer(actingUserId, canManageAll);
        if (allowed.IsFailure)
            return allowed;

        Title = title;
        return Result.Success();
    }

    public Result ChangeLocation(Location? location, Guid actingUserId, bool canManageAll)
    {
        var allowed = EnsureOrganizer(actingUserId, canManageAll);
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
        DateTime nowUtc,
        bool canManageAll
    )
    {
        var allowed = EnsureOrganizer(actingUserId, canManageAll);
        if (allowed.IsFailure)
            return Result.Failure<AppointmentAttendee>(allowed.Error);

        if (_attendees.Count >= MaxAttendees)
            return Result.Failure<AppointmentAttendee>(AppointmentErrors.TooManyAttendees);

        if (Find(userId, customerId, snapshot.Email) is not null)
            return Result.Failure<AppointmentAttendee>(AppointmentErrors.AttendeeAlreadyAdded);

        var attendee = AppointmentAttendee.Invite(Id, kind, userId, customerId, snapshot, isRequired);
        var rsvp = attendee.IssueRsvpToken();
        _attendees.Add(attendee);
        ChildrenRevision++;

        AddDomainEvent(
            new AppointmentAttendeeInvitedDomainEvent(
                Id,
                TenantId,
                attendee.Id,
                kind,
                nowUtc,
                userId,
                customerId,
                snapshot.Email,
                rsvp.Token
            )
        );

        return Result.Success(attendee);
    }

    public Result RemoveAttendee(Guid attendeeId, Guid actingUserId, bool canManageAll)
    {
        var allowed = EnsureOrganizer(actingUserId, canManageAll);
        if (allowed.IsFailure)
            return allowed;

        var attendee = FindById(attendeeId);
        if (attendee is null)
            return Result.Failure(AppointmentErrors.AttendeeNotFound);

        if (attendee.UserId == OrganizerUserId)
            return Result.Failure(AppointmentErrors.OrganizerCannotBeRemoved);

        _attendees.Remove(attendee);
        ChildrenRevision++;
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
        ChildrenRevision++;

        AddDomainEvent(
            new AppointmentAttendeeRespondedDomainEvent(
                Id,
                TenantId,
                attendee.Id,
                response,
                nowUtc,
                OrganizerUserId,
                userId
            )
        );

        return Result.Success();
    }

    /// <summary>
    /// Título, descripción y lugar. La hora va por <see cref="Reschedule"/> y las respuestas no se tocan:
    /// quien aceptó la cita de las 10 sigue yendo aunque cambie el nombre. Sin cambios no anuncia nada.
    /// </summary>
    public Result EditDetails(
        AppointmentTitle title,
        string? description,
        Location? location,
        Guid actingUserId,
        bool canManageAll,
        DateTime nowUtc
    )
    {
        var allowed = EnsureOrganizer(actingUserId, canManageAll);
        if (allowed.IsFailure)
            return allowed;

        var cleanDescription = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (cleanDescription is { Length: > MaxDescriptionLength })
            return Result.Failure(AppointmentErrors.DescriptionTooLong);

        var changed = new List<string>(3);
        if (title.Value != Title.Value)
            changed.Add("Title");
        if (cleanDescription != Description)
            changed.Add("Description");
        if (location?.Value != Location?.Value)
            changed.Add("Location");
        if (changed.Count == 0)
            return Result.Success();

        var previousTitle = Title.Value;
        Title = title;
        Description = cleanDescription;
        Location = location;

        AddDomainEvent(
            new AppointmentDetailsChangedDomainEvent(Id, TenantId, previousTitle, [.. changed], actingUserId, nowUtc)
        );
        return Result.Success();
    }

    /// <summary>
    /// La respuesta desde el enlace del correo: el token dice quién responde. Solo una respuesta real;
    /// volver a "sin responder" no es algo que pida nadie.
    /// </summary>
    public Result RespondByRsvp(string token, AttendeeResponse response, DateTime nowUtc)
    {
        if (Status == AppointmentStatus.Cancelled)
            return Result.Failure(AppointmentErrors.CancelledIsFinal);

        if (response == AttendeeResponse.NeedsAction)
            return Result.Failure(AppointmentErrors.InvalidResponse);

        var attendee = FindByRsvpToken(token);
        if (attendee is null)
            return Result.Failure(AppointmentErrors.AttendeeNotFound);

        // Pasada la cita el enlace ya no responde: un correo viejo no reescribe la historia.
        if (Timing.EndUtc is { } endUtc && nowUtc > endUtc)
            return Result.Failure(AppointmentErrors.RsvpClosed);

        attendee.Respond(response, nowUtc);
        ChildrenRevision++;

        AddDomainEvent(
            new AppointmentAttendeeRespondedDomainEvent(
                Id,
                TenantId,
                attendee.Id,
                response,
                nowUtc,
                OrganizerUserId,
                attendee.UserId
            )
        );

        return Result.Success();
    }

    /// <summary>Cero o null quita el aviso.</summary>
    public Result RequestReminder(int? leadMinutes)
    {
        if (leadMinutes > MaxReminderLeadMinutes)
            return Result.Failure(AppointmentErrors.ReminderLeadTooLong);

        ReminderLeadMinutes = leadMinutes is > 0 ? leadMinutes : null;
        return Result.Success();
    }

    public void StartNewReminderGeneration() => ReminderGeneration++;

    /// <summary>Estos objetivos ya no valen. Se calculan antes del cambio: después la cita no los produce.</summary>
    public void CloseReminderTargets(IReadOnlyList<Guid> targetIds, string? reason, DateTime nowUtc)
    {
        if (targetIds.Count > 0)
            AddDomainEvent(new AppointmentReminderTargetsClosedDomainEvent(Id, TenantId, targetIds, reason, nowUtc));
    }

    /// <summary>Hay que volver a pedir los avisos, salvo los de <paramref name="keptTargetIds"/>, que siguen vivos.</summary>
    public void RefreshReminders(IReadOnlyList<Guid> keptTargetIds, bool leadChanged, DateTime nowUtc) =>
        AddDomainEvent(new AppointmentRemindersRefreshedDomainEvent(Id, TenantId, keptTargetIds, leadChanged, nowUtc));

    /// <summary>La sala llega por evento desde Communication; si divergen, manda la cita.</summary>
    public void LinkMeeting(Guid meetingId, string shortCode)
    {
        MeetingId = meetingId;
        MeetingShortCode = shortCode;
    }

    // ── Serie ────────────────────────────────────────────────────────────────────────────────────

    public Result MakeRecurring(RecurrenceRule recurrence, EventTiming timing, Guid actingUserId, bool canManageAll)
    {
        var allowed = EnsureOrganizer(actingUserId, canManageAll);
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
    public Result CancelOccurrence(
        DateTime originalStartUtc,
        Guid actingUserId,
        bool canManageAll,
        DateTime nowUtc,
        string? reason = null
    )
    {
        var allowed = EnsureCanEditOccurrence(originalStartUtc, actingUserId, canManageAll);
        if (allowed.IsFailure)
            return allowed;

        _exceptions.Add(AppointmentException.Cancel(Id, TenantId, originalStartUtc, actingUserId, nowUtc));
        ChildrenRevision++;

        AddDomainEvent(
            new AppointmentOccurrenceCancelledDomainEvent(Id, TenantId, originalStartUtc, actingUserId, nowUtc, reason)
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
        bool canManageAll,
        DateTime nowUtc
    )
    {
        var allowed = EnsureCanEditOccurrence(originalStartUtc, actingUserId, canManageAll);
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
        ChildrenRevision++;

        AddDomainEvent(
            new AppointmentOccurrenceOverriddenDomainEvent(
                Id,
                TenantId,
                originalStartUtc,
                newStartUtc,
                actingUserId,
                nowUtc,
                newEndUtc
            )
        );

        return Result.Success();
    }

    /// <summary>
    /// Edita la serie entera. Las excepciones existentes <b>se conservan</b>: cada una sigue a su
    /// ocurrencia aunque cambie de instante. Las que la regla nueva ya no alcanza se van.
    /// </summary>
    public Result EditEntireSeries(
        EventTiming timing,
        RecurrenceRule recurrence,
        Guid actingUserId,
        bool canManageAll,
        DateTime nowUtc
    )
    {
        var allowed = EnsureOrganizer(actingUserId, canManageAll);
        if (allowed.IsFailure)
            return allowed;

        if (!IsRecurring)
            return Result.Failure(RecurrenceErrors.NotASeries);

        // Una serie no tiene StartUtc: lo que se anuncia es su primera ocurrencia.
        var previousFirstUtc = OccurrenceExpander.FirstStart(this);
        // Antes de cambiar la regla: después ya no se sabe a qué ocurrencia apuntaba cada excepción.
        var positions = OccurrenceExpander.ExceptionPositions(this);
        Timing = timing;
        Recurrence = recurrence;
        ReanchorExceptions(positions);

        var rsvpTokens = ResetResponses();

        var newFirstUtc = OccurrenceExpander.FirstStart(this) ?? nowUtc;
        AddDomainEvent(
            new AppointmentRescheduledDomainEvent(
                Id,
                TenantId,
                previousFirstUtc,
                newFirstUtc,
                newFirstUtc.Add(timing.Duration ?? TimeSpan.FromHours(1)),
                timing.TimeZone.Id,
                actingUserId,
                nowUtc,
                rsvpTokens
            )
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
        DateTime nowUtc,
        bool canManageAll
    )
    {
        var allowed = EnsureOrganizer(actingUserId, canManageAll);
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
            ReminderLeadMinutes = ReminderLeadMinutes,
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

    private Result EnsureCanEditOccurrence(DateTime originalStartUtc, Guid actingUserId, bool canManageAll)
    {
        var allowed = EnsureOrganizer(actingUserId, canManageAll);
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

    /// <summary>
    /// La ocurrencia N de la regla vieja es la N de la nueva. Sin esto, mover la serie deja la excepción en un
    /// instante que ya no existe: la ocurrencia cancelada revive y la que ahora cae ahí queda cancelada.
    /// </summary>
    private void ReanchorExceptions(Dictionary<DateTime, int> positions)
    {
        if (positions.Count == 0)
            return;

        var lastPosition = 0;
        foreach (var position in positions.Values)
            lastPosition = Math.Max(lastPosition, position);

        var starts = OccurrenceExpander.StartsThrough(this, lastPosition);
        var targets = new HashSet<DateTime>();
        foreach (var position in positions.Values)
        {
            if (position < starts.Count)
                targets.Add(starts[position]);
        }

        var changed =
            _exceptions.RemoveAll(e =>
                positions.TryGetValue(e.OriginalStartUtc, out var position)
                    ? position >= starts.Count
                    // Huérfana de una edición anterior: no puede ocupar el lugar de una viva, el índice único lo rechaza.
                    : targets.Contains(e.OriginalStartUtc)
            ) > 0;

        foreach (var exception in _exceptions)
        {
            if (positions.TryGetValue(exception.OriginalStartUtc, out var position))
                changed |= exception.FollowOccurrenceTo(starts[position]);
        }

        if (changed)
            ChildrenRevision++;
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

    /// <summary>
    /// El organizador manda sobre su cita. <paramref name="canManageAll"/> es el override de la oficina
    /// (permiso <c>calendar.manage_all</c>): sin él, una cita queda congelada cuando su organizador se
    /// enferma o se va, y nadie puede cancelarla ni moverla — ni el dueño de la oficina.
    /// </summary>
    private Result EnsureOrganizer(Guid actingUserId, bool canManageAll)
    {
        if (Status == AppointmentStatus.Cancelled)
            return Result.Failure(AppointmentErrors.CancelledIsFinal);

        return canManageAll || actingUserId == OrganizerUserId
            ? Result.Success()
            : Result.Failure(AppointmentErrors.NotTheOrganizer);
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

    /// <summary>El invitado dueño del enlace; null si el enlace no es de esta cita o ya se reemplazó.</summary>
    public AppointmentAttendee? FindByRsvpToken(string token)
    {
        var hash = RsvpToken.HashOf(token);
        foreach (var attendee in _attendees)
        {
            if (attendee.HasRsvpToken(hash))
                return attendee;
        }

        return null;
    }

    /// <summary>Respuestas a cero y enlaces nuevos: los del aviso anterior dejan de responder.</summary>
    private AttendeeRsvpToken[] ResetResponses()
    {
        var tokens = new AttendeeRsvpToken[_attendees.Count];
        for (var i = 0; i < _attendees.Count; i++)
        {
            _attendees[i].ResetResponse();
            tokens[i] = _attendees[i].IssueRsvpToken();
        }

        return tokens;
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
