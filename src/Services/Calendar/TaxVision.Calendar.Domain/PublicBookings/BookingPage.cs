using System.Text.Json;
using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Calendar.Domain.Booking;
using TaxVision.Calendar.Domain.Types;
using TaxVision.Calendar.Domain.ValueObjects;

namespace TaxVision.Calendar.Domain.PublicBookings;

/// <summary>
/// Página pública de reservas de la plataforma (p. ej. "Book a live demo"). Define el tipo de cita, las reglas
/// de horario, los anfitriones que atienden, las respuestas de "How did you hear about us?" y el consentimiento.
/// Nace apagada: solo se activa con al menos un anfitrión y una respuesta activos y el consentimiento cargado.
/// </summary>
public sealed class BookingPage : AggregateRoot
{
    public const int MaxTitleLength = 120;
    public const int MaxDescriptionLength = 1000;
    public const int MaxConsentTextLength = 1000;
    public const int MaxHosts = 20;
    public const int MaxOptions = 30;
    public const int MaxAlarms = 3;
    public const int MaxConsentVersionLength = 40;
    public const string DefaultConsentVersion = "2026-11-demo-v1";

    private const int MaxNoticeMinutes = 14 * 1440;
    private const int MaxBufferMinutes = 240;
    private const int MaxReminderHours = 168;
    private const int MaxAlarmMinutes = 7 * 1440;

    private readonly List<BookingPageHost> _hosts = [];
    private readonly List<BookingPageHeardAboutOption> _heardAboutOptions = [];

    private BookingPage() { }

    public BookingPageSlug Slug { get; private set; } = null!;
    public LocalizedText Title { get; private set; } = null!;
    public LocalizedText Description { get; private set; } = null!;
    public Guid AppointmentTypeId { get; private set; }
    public bool IsActive { get; private set; }

    public int MinNoticeMinutes { get; private set; } = 240;
    public int MaxDaysAhead { get; private set; } = 30;
    public int BufferMinutes { get; private set; } = 15;
    public int SlotStepMinutes { get; private set; } = 30;
    public bool RequireEmailVerification { get; private set; } = true;
    public int? ReminderHoursBefore { get; private set; } = 24;
    public string IcsAlarmMinutesJson { get; private set; } = "[1440,60]";

    public BookingFieldMode PhoneField { get; private set; } = BookingFieldMode.Optional;
    public BookingFieldMode OfficeSizeField { get; private set; } = BookingFieldMode.Optional;
    public BookingFieldMode InterestField { get; private set; } = BookingFieldMode.Optional;

    /// <summary>Aviso junto al botón de reservar. Se muestra tal cual y su versión queda en cada reserva.</summary>
    public LocalizedText? PrivacyNotice { get; private set; }

    /// <summary>Texto de la casilla opcional de marketing.</summary>
    public LocalizedText? MarketingConsent { get; private set; }

    public string ConsentVersion { get; private set; } = DefaultConsentVersion;

    [UserReference(UserReferenceKind.Provenance)]
    public Guid CreatedByUserId { get; private set; }

    [UserReference(UserReferenceKind.Provenance)]
    public Guid UpdatedByUserId { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyList<BookingPageHost> Hosts => _hosts;
    public IReadOnlyList<BookingPageHeardAboutOption> HeardAboutOptions => _heardAboutOptions;

    public IReadOnlyList<int> IcsAlarmMinutes => JsonSerializer.Deserialize<List<int>>(IcsAlarmMinutesJson) ?? [];

    /// <summary>Anfitriones activos en orden estable: primero SortOrder, luego el id.</summary>
    public IReadOnlyList<BookingPageHost> ActiveHosts
    {
        get
        {
            var active = _hosts.FindAll(host => host.IsActive);
            active.Sort(
                (a, b) => a.SortOrder != b.SortOrder ? a.SortOrder.CompareTo(b.SortOrder) : a.UserId.CompareTo(b.UserId)
            );
            return active;
        }
    }

    public IReadOnlyList<BookingPageHeardAboutOption> ActiveHeardAboutOptions
    {
        get
        {
            var active = _heardAboutOptions.FindAll(option => option.IsActive);
            active.Sort(
                (a, b) =>
                    a.SortOrder != b.SortOrder
                        ? a.SortOrder.CompareTo(b.SortOrder)
                        : string.CompareOrdinal(a.Code, b.Code)
            );
            return active;
        }
    }

    public static Result<BookingPage> Create(
        Guid tenantId,
        BookingPageSlug slug,
        LocalizedText title,
        LocalizedText description,
        AppointmentType appointmentType,
        Guid actingUserId,
        DateTime nowUtc
    )
    {
        if (!title.Fits(MaxTitleLength) || !description.Fits(MaxDescriptionLength))
            return Result.Failure<BookingPage>(BookingPageErrors.TextTooLong);
        if (!IsUsableType(appointmentType) || appointmentType.TenantId != tenantId)
            return Result.Failure<BookingPage>(BookingPageErrors.TypeNotUsable);

        var page = new BookingPage
        {
            Slug = slug,
            Title = title,
            Description = description,
            AppointmentTypeId = appointmentType.Id,
            CreatedByUserId = actingUserId,
            CreatedAtUtc = nowUtc,
        };
        page.SetTenant(tenantId);
        page.Touch(actingUserId, nowUtc);
        return Result.Success(page);
    }

    public Result ChangeTexts(
        BookingPageSlug slug,
        LocalizedText title,
        LocalizedText description,
        Guid actingUserId,
        DateTime nowUtc
    )
    {
        if (!title.Fits(MaxTitleLength) || !description.Fits(MaxDescriptionLength))
            return Result.Failure(BookingPageErrors.TextTooLong);

        Slug = slug;
        Title = title;
        Description = description;
        Touch(actingUserId, nowUtc);
        return Result.Success();
    }

    /// <summary>
    /// La única regla de "tipo usable" para una demo pública: activo, presencial (sin sala de Communication) y que
    /// bloquee choques. La usan la página, el planificador y la plataforma.
    /// </summary>
    public static bool IsUsableType(AppointmentType? type) =>
        type is { IsActive: true, IsVirtual: false, BlocksOnConflict: true };

    public Result ChangeAppointmentType(AppointmentType appointmentType, Guid actingUserId, DateTime nowUtc)
    {
        if (!IsUsableType(appointmentType) || appointmentType.TenantId != TenantId)
            return Result.Failure(BookingPageErrors.TypeNotUsable);

        AppointmentTypeId = appointmentType.Id;
        Touch(actingUserId, nowUtc);
        return Result.Success();
    }

    public Result ChangeRules(
        int minNoticeMinutes,
        int maxDaysAhead,
        int bufferMinutes,
        int slotStepMinutes,
        bool requireEmailVerification,
        int? reminderHoursBefore,
        IReadOnlyList<int> icsAlarmMinutes,
        Guid actingUserId,
        DateTime nowUtc
    )
    {
        var alarms = new List<int>(icsAlarmMinutes);
        alarms.Sort((a, b) => b.CompareTo(a));
        var valid =
            minNoticeMinutes is >= 0 and <= MaxNoticeMinutes
            && maxDaysAhead is >= 1 and <= 365
            && bufferMinutes is >= 0 and <= MaxBufferMinutes
            && IsAllowedSlotStep(slotStepMinutes)
            && reminderHoursBefore is null or (>= 1 and <= MaxReminderHours)
            && AreValidAlarms(alarms);
        if (!valid)
            return Result.Failure(BookingPageErrors.RulesOutOfRange);

        MinNoticeMinutes = minNoticeMinutes;
        MaxDaysAhead = maxDaysAhead;
        BufferMinutes = bufferMinutes;
        SlotStepMinutes = slotStepMinutes;
        RequireEmailVerification = requireEmailVerification;
        ReminderHoursBefore = reminderHoursBefore;
        IcsAlarmMinutesJson = JsonSerializer.Serialize(alarms);
        Touch(actingUserId, nowUtc);
        return Result.Success();
    }

    public Result ConfigureFields(
        BookingFieldMode phone,
        BookingFieldMode officeSize,
        BookingFieldMode interest,
        Guid actingUserId,
        DateTime nowUtc
    )
    {
        if (!Enum.IsDefined(phone) || !Enum.IsDefined(officeSize) || !Enum.IsDefined(interest))
            return Result.Failure(BookingPageErrors.RulesOutOfRange);

        PhoneField = phone;
        OfficeSizeField = officeSize;
        InterestField = interest;
        Touch(actingUserId, nowUtc);
        return Result.Success();
    }

    /// <summary>Cambia los textos de consentimiento. Cada cambio de texto lleva versión nueva: así cada reserva prueba qué aceptó.</summary>
    public Result ChangeConsent(
        LocalizedText privacyNotice,
        LocalizedText marketingConsent,
        string? version,
        Guid actingUserId,
        DateTime nowUtc
    )
    {
        var trimmed = version?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > MaxConsentVersionLength)
            return Result.Failure(BookingPageErrors.ConsentVersionInvalid);
        if (!privacyNotice.Fits(MaxConsentTextLength) || !marketingConsent.Fits(MaxConsentTextLength))
            return Result.Failure(BookingPageErrors.TextTooLong);

        PrivacyNotice = privacyNotice;
        MarketingConsent = marketingConsent;
        ConsentVersion = trimmed;
        Touch(actingUserId, nowUtc);
        return Result.Success();
    }

    /// <summary>Reemplaza la lista de anfitriones; los que faltan se quitan.</summary>
    public Result SetHosts(IReadOnlyList<BookingPageHostInput> hosts, Guid actingUserId, DateTime nowUtc)
    {
        if (hosts.Count > MaxHosts)
            return Result.Failure(BookingPageErrors.TooManyHosts);

        var seen = new HashSet<Guid>();
        var anyActive = false;
        var validated = new List<(BookingPageHostInput Input, string Name, string Email, string Url)>();
        foreach (var host in hosts)
        {
            if (host.UserId == Guid.Empty || !seen.Add(host.UserId))
                return Result.Failure(BookingPageErrors.DuplicatedHost);
            anyActive |= host.IsActive;
            var name = host.DisplayName?.Trim() ?? string.Empty;
            if (name.Length is 0 or > BookingPageHost.MaxDisplayNameLength)
                return Result.Failure(BookingPageErrors.HostNameInvalid);
            var email = WorkEmail.Create(host.NotificationEmail);
            if (email.IsFailure)
                return Result.Failure(BookingPageErrors.EmailInvalid);
            var url = ValueObjects.MeetingUrl.Create(host.MeetingUrl);
            if (url.IsFailure)
                return Result.Failure(BookingPageErrors.MeetingUrlInvalid);
            validated.Add((host, name, email.Value.Value, url.Value.Value));
        }

        if (IsActive && !anyActive)
            return Result.Failure(BookingPageErrors.NoActiveHost);

        _hosts.RemoveAll(existing => !seen.Contains(existing.UserId));
        foreach (var (input, name, email, url) in validated)
        {
            var host = _hosts.Find(existing => existing.UserId == input.UserId);
            if (host is null)
            {
                host = BookingPageHost.Create(Id, input.UserId);
                _hosts.Add(host);
            }
            host.Apply(name, email, url, input.SortOrder, input.IsActive);
        }

        Touch(actingUserId, nowUtc);
        return Result.Success();
    }

    /// <summary>Reemplaza las respuestas de "How did you hear about us?". Para retirar una sin perder reportes, desactivarla.</summary>
    public Result SetHeardAboutOptions(IReadOnlyList<HeardAboutOptionInput> options, Guid actingUserId, DateTime nowUtc)
    {
        if (options.Count > MaxOptions)
            return Result.Failure(BookingPageErrors.TooManyOptions);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var anyActive = false;
        var validated = new List<(HeardAboutOptionInput Input, string En, string? Es)>();
        foreach (var option in options)
        {
            if (!HeardAboutAnswer.IsValidCode(option.Code))
                return Result.Failure(BookingPageErrors.OptionCodeInvalid);
            if (!seen.Add(option.Code!))
                return Result.Failure(BookingPageErrors.DuplicatedOption);
            anyActive |= option.IsActive;
            var en = option.LabelEn?.Trim() ?? string.Empty;
            var es = string.IsNullOrWhiteSpace(option.LabelEs) ? null : option.LabelEs.Trim();
            if (
                en.Length is 0 or > BookingPageHeardAboutOption.MaxLabelLength
                || es?.Length > BookingPageHeardAboutOption.MaxLabelLength
            )
                return Result.Failure(BookingPageErrors.OptionLabelInvalid);
            validated.Add((option, en, es));
        }

        if (IsActive && !anyActive)
            return Result.Failure(BookingPageErrors.NoHeardAboutOption);

        _heardAboutOptions.RemoveAll(existing => !seen.Contains(existing.Code));
        foreach (var (input, en, es) in validated)
        {
            var option = _heardAboutOptions.Find(existing => existing.Code == input.Code);
            if (option is null)
            {
                option = BookingPageHeardAboutOption.Create(Id, input.Code!);
                _heardAboutOptions.Add(option);
            }
            option.Apply(en, es, input.SortOrder, input.IsActive, input.AllowsDetail);
        }

        Touch(actingUserId, nowUtc);
        return Result.Success();
    }

    public Result Activate(Guid actingUserId, DateTime nowUtc)
    {
        if (!_hosts.Exists(host => host.IsActive))
            return Result.Failure(BookingPageErrors.NoActiveHost);
        if (!_heardAboutOptions.Exists(option => option.IsActive))
            return Result.Failure(BookingPageErrors.NoHeardAboutOption);
        if (PrivacyNotice is null || MarketingConsent is null)
            return Result.Failure(BookingPageErrors.NoConsent);

        IsActive = true;
        Touch(actingUserId, nowUtc);
        return Result.Success();
    }

    public void Deactivate(Guid actingUserId, DateTime nowUtc)
    {
        IsActive = false;
        Touch(actingUserId, nowUtc);
    }

    /// <summary>
    /// Retiro del empleado: deja de atender. Si era el último anfitrión activo, la página se apaga para no
    /// ofrecer horarios que nadie va a cubrir.
    /// </summary>
    public bool RemoveHostOnOffboarding(Guid userId, DateTime nowUtc)
    {
        var host = FindActiveHost(userId);
        if (host is null)
            return false;

        host.Deactivate();
        if (!_hosts.Exists(existing => existing.IsActive))
            IsActive = false;
        UpdatedAtUtc = nowUtc;
        return true;
    }

    public BookingPageHost? FindActiveHost(Guid userId) => _hosts.Find(host => host.UserId == userId && host.IsActive);

    public BookingPageHeardAboutOption? FindActiveOption(string? code) =>
        _heardAboutOptions.Find(option => option.IsActive && option.Code == code);

    private static bool IsAllowedSlotStep(int minutes)
    {
        foreach (var step in BookingPolicy.AllowedSlotSteps)
        {
            if (step == minutes)
                return true;
        }
        return false;
    }

    private static bool AreValidAlarms(List<int> alarms)
    {
        if (alarms.Count > MaxAlarms)
            return false;
        for (var i = 0; i < alarms.Count; i++)
        {
            if (alarms[i] is < 1 or > MaxAlarmMinutes || (i > 0 && alarms[i] == alarms[i - 1]))
                return false;
        }
        return true;
    }

    private void Touch(Guid actingUserId, DateTime nowUtc)
    {
        UpdatedByUserId = actingUserId;
        UpdatedAtUtc = nowUtc;
    }
}
