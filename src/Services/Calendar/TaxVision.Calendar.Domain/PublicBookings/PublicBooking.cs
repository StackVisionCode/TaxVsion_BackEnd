using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Calendar.Domain.PublicBookings.Events;

namespace TaxVision.Calendar.Domain.PublicBookings;

/// <summary>
/// Reserva de un visitante sin cuenta en una página pública. Primero retiene el horario unos minutos mientras
/// confirma su email con un código; al confirmarse nace la cita del anfitrión y el visitante recibe un enlace
/// de gestión. Solo se guardan hashes: los secretos en claro salen una vez, en el valor que devuelve cada
/// método, y nunca en un evento de dominio (esos se guardan en la cola durable).
/// </summary>
public sealed class PublicBooking : AggregateRoot
{
    public const int MaxCodeAttempts = 5;
    public const int MaxCodeSends = 3;
    public const int MaxIdempotencyKeyLength = 100;
    public const int MaxReasonLength = 500;
    public const string AnonymizedName = "Anonymized";

    private PublicBooking() { }

    public Guid BookingPageId { get; private set; }
    public PublicBookingStatus Status { get; private set; }

    public string Name { get; private set; } = string.Empty;
    public string? Email { get; private set; }

    /// <summary>SHA-256 del email normalizado: limita reservas activas por persona sin indexar el email.</summary>
    public string? RequesterKey { get; private set; }

    public string Company { get; private set; } = string.Empty;
    public OfficeSize? OfficeSize { get; private set; }
    public string? Phone { get; private set; }
    public string? Interest { get; private set; }
    public string HeardAboutCode { get; private set; } = string.Empty;
    public string? HeardAboutDetail { get; private set; }
    public BookingEntrySource EntrySource { get; private set; }
    public string? UtmSource { get; private set; }
    public string? UtmMedium { get; private set; }
    public string? UtmCampaign { get; private set; }
    public string? ReferrerDomain { get; private set; }
    public BookingLanguage Language { get; private set; }
    public string VisitorTimeZone { get; private set; } = string.Empty;
    public bool MarketingOptIn { get; private set; }
    public string ConsentTextVersion { get; private set; } = string.Empty;

    public DateTime SlotStartUtc { get; private set; }
    public int DurationMinutes { get; private set; }
    public DateTime SlotEndUtc => SlotStartUtc.AddMinutes(DurationMinutes);

    [UserReference(
        UserReferenceKind.ActiveWork,
        HandledBy = "PublicBookingHostOffboardedConsumer",
        Note = "Se reasigna o se cancela."
    )]
    public Guid HostUserId { get; private set; }

    public Guid? AppointmentId { get; private set; }

    public string IdempotencyKey { get; private set; } = string.Empty;
    public byte[] RequestHandleHash { get; private set; } = [];
    public bool RequiresVerification { get; private set; }
    public byte[]? CodeHash { get; private set; }
    public int CodeAttempts { get; private set; }
    public int CodeSends { get; private set; }
    public DateTime? EmailVerifiedAtUtc { get; private set; }
    public DateTime HoldExpiresAtUtc { get; private set; }

    public byte[]? ManagementTokenHash { get; private set; }
    public int IcsSequence { get; private set; }

    /// <summary>Sube con cada token de gestión nuevo; ata el token cifrado del evento a su emisión.</summary>
    public int ManagementTokenVersion { get; private set; }

    public DateTime? ConfirmedAtUtc { get; private set; }
    public DateTime? ReminderSentAtUtc { get; private set; }

    /// <summary>Último aviso de "ya tienes una demo"; limita la rotación del enlace a una por hora.</summary>
    public DateTime? RepeatNoticeSentAtUtc { get; private set; }

    public static readonly TimeSpan RepeatNoticeInterval = TimeSpan.FromHours(1);
    public DateTime? CancelledAtUtc { get; private set; }
    public PublicBookingActor? CancelledBy { get; private set; }

    [UserReference(UserReferenceKind.Provenance)]
    public Guid? CancelledByUserId { get; private set; }

    public string? CancelReason { get; private set; }
    public DateTime? ExpiredAtUtc { get; private set; }
    public DateTime? OutcomeAtUtc { get; private set; }

    [UserReference(UserReferenceKind.Provenance)]
    public Guid? OutcomeByUserId { get; private set; }

    public DateTime? AnonymizedAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public bool IsClosed =>
        Status
            is PublicBookingStatus.Cancelled
                or PublicBookingStatus.Expired
                or PublicBookingStatus.Completed
                or PublicBookingStatus.NoShow;

    public static Result<PublicBookingIssued> Request(
        Guid tenantId,
        Guid bookingPageId,
        PublicBookingRequestData data,
        DateTime slotStartUtc,
        int durationMinutes,
        Guid hostUserId,
        string? idempotencyKey,
        bool requiresVerification,
        int holdMinutes,
        DateTime nowUtc
    )
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > MaxIdempotencyKeyLength)
            return Result.Failure<PublicBookingIssued>(PublicBookingErrors.IdempotencyKeyInvalid);
        if (slotStartUtc <= nowUtc)
            return Result.Failure<PublicBookingIssued>(PublicBookingErrors.SlotInPast);
        var consent = data.ConsentTextVersion?.Trim() ?? string.Empty;
        if (
            bookingPageId == Guid.Empty
            || hostUserId == Guid.Empty
            || durationMinutes is < 5 or > 480
            || holdMinutes is < 1 or > 60
            || consent.Length is 0 or > BookingPage.MaxConsentVersionLength
            || !IsRequesterKey(data.RequesterKey)
        )
            return Result.Failure<PublicBookingIssued>(PublicBookingErrors.RequestInvalid);

        var (handle, handleHash) = OpaqueToken.Create();
        var booking = new PublicBooking
        {
            BookingPageId = bookingPageId,
            Status = PublicBookingStatus.PendingVerification,
            Name = data.Name.Value,
            Email = data.Email.Value,
            RequesterKey = data.RequesterKey,
            Company = data.Company.Value,
            OfficeSize = data.OfficeSize,
            Phone = data.Phone?.Value,
            Interest = data.Interest?.Value,
            HeardAboutCode = data.HeardAbout.Code,
            HeardAboutDetail = data.HeardAbout.Detail,
            EntrySource = data.Attribution.EntrySource,
            UtmSource = data.Attribution.UtmSource,
            UtmMedium = data.Attribution.UtmMedium,
            UtmCampaign = data.Attribution.UtmCampaign,
            ReferrerDomain = data.Attribution.ReferrerDomain,
            Language = data.Language,
            VisitorTimeZone = data.VisitorTimeZone.Id,
            MarketingOptIn = data.MarketingOptIn,
            ConsentTextVersion = consent,
            SlotStartUtc = slotStartUtc,
            DurationMinutes = durationMinutes,
            HostUserId = hostUserId,
            IdempotencyKey = idempotencyKey,
            RequestHandleHash = handleHash,
            RequiresVerification = requiresVerification,
            HoldExpiresAtUtc = nowUtc.AddMinutes(holdMinutes),
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        };
        booking.SetTenant(tenantId);

        var code = requiresVerification ? booking.IssueCode(nowUtc) : null;
        return Result.Success(new PublicBookingIssued(booking, handle, code));
    }

    /// <summary>Comprueba el código. Cinco fallos cierran la solicitud y liberan el horario.</summary>
    public Result VerifyCode(string? code, DateTime nowUtc)
    {
        var pending = EnsurePendingAndHeld(nowUtc);
        if (pending.IsFailure)
            return pending;
        if (!RequiresVerification || EmailVerifiedAtUtc is not null)
            return Result.Success();

        if (VerificationCode.Matches(Id, code, CodeHash))
        {
            EmailVerifiedAtUtc = nowUtc;
            CodeHash = null;
            UpdatedAtUtc = nowUtc;
            return Result.Success();
        }

        CodeAttempts++;
        UpdatedAtUtc = nowUtc;
        if (CodeAttempts < MaxCodeAttempts)
            return Result.Failure(PublicBookingErrors.InvalidCode);

        CloseAsExpired(nowUtc);
        return Result.Failure(PublicBookingErrors.TooManyAttempts);
    }

    /// <summary>Otro código (hasta tres envíos). Devuelve el código en claro para cifrarlo y mandarlo.</summary>
    public Result<string> ResendCode(DateTime nowUtc)
    {
        var pending = EnsurePendingAndHeld(nowUtc);
        if (pending.IsFailure)
            return Result.Failure<string>(pending.Error);
        if (!RequiresVerification || EmailVerifiedAtUtc is not null)
            return Result.Failure<string>(PublicBookingErrors.RequestInvalid);
        if (CodeSends >= MaxCodeSends)
            return Result.Failure<string>(PublicBookingErrors.TooManyCodes);

        return Result.Success(IssueCode(nowUtc));
    }

    /// <summary>
    /// El mismo envío llegó otra vez (reintento del navegador): el manejo anterior pudo perderse, así que se
    /// emite uno nuevo. No manda otro código.
    /// </summary>
    public Result<string> ReissueRequestHandle(DateTime nowUtc)
    {
        var pending = EnsurePendingAndHeld(nowUtc);
        if (pending.IsFailure)
            return Result.Failure<string>(pending.Error);

        var (handle, hash) = OpaqueToken.Create();
        RequestHandleHash = hash;
        UpdatedAtUtc = nowUtc;
        return Result.Success(handle);
    }

    /// <summary>El visitante elige otro horario mientras dura la retención (el anterior se ocupó).</summary>
    public Result ChooseSlot(DateTime slotStartUtc, Guid hostUserId, DateTime nowUtc)
    {
        var allowed = CanChooseSlot(nowUtc);
        if (allowed.IsFailure)
            return allowed;
        if (slotStartUtc <= nowUtc)
            return Result.Failure(PublicBookingErrors.SlotInPast);
        if (hostUserId == Guid.Empty)
            return Result.Failure(PublicBookingErrors.RequestInvalid);

        SlotStartUtc = slotStartUtc;
        HostUserId = hostUserId;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    /// <summary>
    /// Puede elegir otro horario: sigue pendiente y retenida, y ya probó su email. Lo último hace que un manejo
    /// inventado y uno real sin verificar respondan igual.
    /// </summary>
    public Result CanChooseSlot(DateTime nowUtc)
    {
        var pending = EnsurePendingAndHeld(nowUtc);
        if (pending.IsFailure)
            return pending;
        return RequiresVerification && EmailVerifiedAtUtc is null
            ? Result.Failure(PublicBookingErrors.NotVerified)
            : Result.Success();
    }

    /// <summary>Confirma la reserva ya creada la cita. Devuelve el token de gestión en claro.</summary>
    public Result<string> Confirm(Guid hostUserId, Guid appointmentId, DateTime nowUtc)
    {
        var pending = EnsurePendingAndHeld(nowUtc);
        if (pending.IsFailure)
            return Result.Failure<string>(pending.Error);
        if (RequiresVerification && EmailVerifiedAtUtc is null)
            return Result.Failure<string>(PublicBookingErrors.NotVerified);
        if (hostUserId == Guid.Empty || appointmentId == Guid.Empty)
            return Result.Failure<string>(PublicBookingErrors.RequestInvalid);

        Status = PublicBookingStatus.Confirmed;
        HostUserId = hostUserId;
        AppointmentId = appointmentId;
        CodeHash = null;
        ConfirmedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
        return Result.Success(RotateManagementToken());
    }

    public Result<string> RescheduleByVisitor(DateTime slotStartUtc, Guid hostUserId, DateTime nowUtc) =>
        Reschedule(slotStartUtc, hostUserId, null, nowUtc);

    public Result<string> RescheduleByStaff(DateTime slotStartUtc, Guid hostUserId, string? reason, DateTime nowUtc) =>
        Reschedule(slotStartUtc, hostUserId, reason, nowUtc);

    public Result CancelByVisitor(DateTime nowUtc, string? reason = null) =>
        Cancel(PublicBookingActor.Visitor, null, reason, nowUtc);

    public Result CancelByStaff(Guid actingUserId, string? reason, DateTime nowUtc) =>
        Cancel(PublicBookingActor.Staff, actingUserId, reason, nowUtc);

    /// <summary>Anota si la demo se hizo o el visitante no llegó. Se puede corregir después.</summary>
    public Result MarkOutcome(PublicBookingOutcome outcome, Guid actingUserId, DateTime nowUtc)
    {
        if (
            Status is not (PublicBookingStatus.Confirmed or PublicBookingStatus.Completed or PublicBookingStatus.NoShow)
        )
            return Result.Failure(PublicBookingErrors.NotConfirmed);
        if (nowUtc < SlotEndUtc)
            return Result.Failure(PublicBookingErrors.NotFinishedYet);
        if (!Enum.IsDefined(outcome))
            return Result.Failure(PublicBookingErrors.RequestInvalid);

        Status = outcome == PublicBookingOutcome.Completed ? PublicBookingStatus.Completed : PublicBookingStatus.NoShow;
        OutcomeAtUtc = nowUtc;
        OutcomeByUserId = actingUserId;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    /// <summary>El job cierra la solicitud que no se confirmó a tiempo y libera el horario.</summary>
    public Result Expire(DateTime nowUtc)
    {
        if (Status != PublicBookingStatus.PendingVerification)
            return Result.Failure(PublicBookingErrors.NotPending);
        if (nowUtc < HoldExpiresAtUtc)
            return Result.Failure(PublicBookingErrors.RequestInvalid);

        CloseAsExpired(nowUtc);
        return Result.Success();
    }

    /// <summary>El mismo visitante pidió de nuevo: la solicitud pendiente se cierra y libera su horario.</summary>
    public Result ExpireReplaced(DateTime nowUtc)
    {
        if (Status != PublicBookingStatus.PendingVerification)
            return Result.Failure(PublicBookingErrors.NotPending);

        CloseAsExpired(nowUtc);
        return Result.Success();
    }

    /// <summary>
    /// Marca el recordatorio, que sale <paramref name="hoursBefore"/> horas antes de la demo. Rota el token y lo
    /// devuelve: el enlace del recordatorio pasa a ser el vigente. Si la demo se confirmó o se movió ya dentro de
    /// esa ventana no hace falta: el visitante acaba de recibir un correo con todo.
    /// </summary>
    /// <summary>
    /// El mismo visitante pidió otra demo teniendo una confirmada: se le avisa por correo con un enlace de gestión
    /// nuevo. Una vez por hora como mucho, para que nadie invalide su enlace en cadena.
    /// </summary>
    public Result<string> NoteRepeatRequest(DateTime nowUtc)
    {
        var changeable = CanBeChanged(nowUtc);
        if (changeable.IsFailure)
            return Result.Failure<string>(changeable.Error);
        if (RepeatNoticeSentAtUtc is { } last && nowUtc - last < RepeatNoticeInterval)
            return Result.Failure<string>(PublicBookingErrors.RepeatNoticeTooSoon);

        RepeatNoticeSentAtUtc = nowUtc;
        return Result.Success(RotateManagementToken());
    }

    public Result<string> MarkReminderSent(int hoursBefore, DateTime nowUtc)
    {
        var changeable = CanBeChanged(nowUtc);
        if (changeable.IsFailure)
            return Result.Failure<string>(changeable.Error);
        if (ReminderSentAtUtc is not null)
            return Result.Failure<string>(PublicBookingErrors.ReminderAlreadySent);
        var windowOpensUtc = SlotStartUtc.AddHours(-hoursBefore);
        if (nowUtc < windowOpensUtc)
            return Result.Failure<string>(PublicBookingErrors.ReminderNotDue);
        if (UpdatedAtUtc >= windowOpensUtc)
            return Result.Failure<string>(PublicBookingErrors.ReminderNotNeeded);

        ReminderSentAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
        return Result.Success(RotateManagementToken());
    }

    /// <summary>Se puede mover o cancelar: está confirmada y todavía no empezó.</summary>
    public Result CanBeChanged(DateTime nowUtc) => EnsureConfirmedAndAhead(nowUtc);

    /// <summary>Borra los datos personales de una reserva cerrada; quedan solo los datos para reportes.</summary>
    public Result Anonymize(DateTime nowUtc)
    {
        if (!IsClosed)
            return Result.Failure(PublicBookingErrors.NotClosed);
        if (AnonymizedAtUtc is not null)
            return Result.Success();

        Name = AnonymizedName;
        Company = AnonymizedName;
        Email = null;
        RequesterKey = null;
        Phone = null;
        Interest = null;
        HeardAboutDetail = null;
        CancelReason = null;
        ManagementTokenHash = null;
        CodeHash = null;
        AnonymizedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    private Result<string> Reschedule(DateTime slotStartUtc, Guid hostUserId, string? reason, DateTime nowUtc)
    {
        var changeable = EnsureConfirmedAndAhead(nowUtc);
        if (changeable.IsFailure)
            return Result.Failure<string>(changeable.Error);
        if (slotStartUtc <= nowUtc)
            return Result.Failure<string>(PublicBookingErrors.SlotInPast);
        if (hostUserId == Guid.Empty)
            return Result.Failure<string>(PublicBookingErrors.RequestInvalid);
        var cleanReason = CleanReason(reason);
        if (cleanReason.IsFailure)
            return Result.Failure<string>(cleanReason.Error);

        SlotStartUtc = slotStartUtc;
        HostUserId = hostUserId;
        ReminderSentAtUtc = null;
        IcsSequence++;
        UpdatedAtUtc = nowUtc;
        return Result.Success(RotateManagementToken());
    }

    private Result Cancel(PublicBookingActor actor, Guid? actingUserId, string? reason, DateTime nowUtc)
    {
        var changeable = EnsureConfirmedAndAhead(nowUtc);
        if (changeable.IsFailure)
            return changeable;
        var cleanReason = CleanReason(reason);
        if (cleanReason.IsFailure)
            return cleanReason;

        Status = PublicBookingStatus.Cancelled;
        CancelledAtUtc = nowUtc;
        CancelledBy = actor;
        CancelledByUserId = actingUserId;
        CancelReason = cleanReason.Value;
        ManagementTokenHash = null;
        IcsSequence++;
        UpdatedAtUtc = nowUtc;
        AddDomainEvent(
            new PublicBookingCancelledDomainEvent(
                Id,
                TenantId,
                SlotStartUtc,
                HostUserId,
                actor,
                cleanReason.Value,
                IcsSequence,
                nowUtc
            )
        );
        return Result.Success();
    }

    private string IssueCode(DateTime nowUtc)
    {
        var code = VerificationCode.Generate();
        CodeHash = VerificationCode.HashOf(Id, code);
        CodeSends++;
        UpdatedAtUtc = nowUtc;
        return code;
    }

    private string RotateManagementToken()
    {
        var (token, hash) = OpaqueToken.Create();
        ManagementTokenHash = hash;
        ManagementTokenVersion++;
        return token;
    }

    // HMAC-SHA256 en hex: 64 caracteres.
    private static bool IsRequesterKey(string? key)
    {
        if (key is not { Length: 64 })
            return false;
        foreach (var c in key)
        {
            if (!char.IsAsciiHexDigitLower(c) && !char.IsAsciiDigit(c))
                return false;
        }
        return true;
    }

    private void CloseAsExpired(DateTime nowUtc)
    {
        Status = PublicBookingStatus.Expired;
        ExpiredAtUtc = nowUtc;
        CodeHash = null;
        UpdatedAtUtc = nowUtc;
    }

    private Result EnsurePendingAndHeld(DateTime nowUtc)
    {
        if (Status != PublicBookingStatus.PendingVerification)
            return Result.Failure(PublicBookingErrors.NotPending);
        return nowUtc >= HoldExpiresAtUtc ? Result.Failure(PublicBookingErrors.HoldExpired) : Result.Success();
    }

    private Result EnsureConfirmedAndAhead(DateTime nowUtc)
    {
        if (Status != PublicBookingStatus.Confirmed)
            return Result.Failure(PublicBookingErrors.NotConfirmed);
        return nowUtc >= SlotStartUtc ? Result.Failure(PublicBookingErrors.AlreadyHappened) : Result.Success();
    }

    private static Result<string?> CleanReason(string? reason)
    {
        var trimmed = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        return trimmed?.Length > MaxReasonLength
            ? Result.Failure<string?>(PublicBookingErrors.ReasonTooLong)
            : Result.Success(trimmed);
    }
}
