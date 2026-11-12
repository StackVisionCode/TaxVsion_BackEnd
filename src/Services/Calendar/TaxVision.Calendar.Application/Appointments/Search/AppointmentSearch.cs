using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Calendar.Application.Appointments.Abstractions;
using TaxVision.Calendar.Application.Types.Abstractions;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Scheduling;
using TaxVision.Calendar.Domain.Types;

namespace TaxVision.Calendar.Application.Appointments.Search;

public enum AppointmentSearchStatus
{
    All,
    Upcoming,
    Past,
    Requests,
    Cancelled,
}

/// <summary>
/// Lo que la base filtra. <c>PortalCustomerId</c> limita a las citas de ese cliente y el texto solo
/// mira título y lugar; sin él, el texto también mira notas y asistentes.
/// </summary>
public sealed record AppointmentSearchCriteria(
    Guid TenantId,
    string? Text,
    DateTime FromUtc,
    DateTime ToUtc,
    Guid? AssignedToUserId,
    Guid? PortalCustomerId,
    Guid? TypeId,
    Guid? PersonUserId,
    Guid? CustomerId,
    int Take,
    AppointmentSearchStatus Status = AppointmentSearchStatus.All,
    DateTime? NowUtc = null
);

public interface IAppointmentSearch
{
    Task<IReadOnlyList<Appointment>> SearchAsync(AppointmentSearchCriteria criteria, CancellationToken ct = default);
}

/// <summary>Una cita encontrada; en una serie, su próxima ocurrencia (o la última, si ya pasó).</summary>
public sealed record AppointmentSearchItem(
    Guid Id,
    DateTime StartUtc,
    DateTime EndUtc,
    string Title,
    string? Location,
    Guid AppointmentTypeId,
    string? TypeName,
    string? ColorHex,
    string Status,
    bool IsUpcoming,
    bool IsVirtual,
    bool IsRecurring,
    Guid OrganizerUserId,
    Guid? CustomerId,
    string? MatchedPerson
);

public sealed record AppointmentSearchResult(IReadOnlyList<AppointmentSearchItem> Items, bool HasMore)
{
    public static AppointmentSearchResult Empty { get; } = new([], false);
}

public sealed record SearchAppointmentsQuery(
    Guid TenantId,
    Guid ActorUserId,
    bool CanViewAll,
    string? Text,
    AppointmentSearchStatus Status,
    Guid? TypeId,
    Guid? PersonUserId,
    Guid? CustomerId,
    int Take
);

public sealed record SearchPortalAppointmentsQuery(
    Guid TenantId,
    Guid CustomerId,
    string? Text,
    AppointmentSearchStatus Status,
    int Take
);

/// <summary>Búsqueda del equipo: misma visibilidad por asignación que el calendario.</summary>
public static class SearchAppointmentsHandler
{
    public static async Task<Result<AppointmentSearchResult>> Handle(
        SearchAppointmentsQuery query,
        IAppointmentSearch search,
        IAppointmentTypeRepository types,
        IOptions<CalendarVisibilityOptions> visibility,
        CancellationToken ct
    )
    {
        var text = AppointmentSearchRules.Normalize(query.Text);
        var hasFilter =
            query.Status != AppointmentSearchStatus.All
            || query.TypeId is not null
            || query.PersonUserId is not null
            || query.CustomerId is not null;
        if (text is null && !hasFilter)
            return Result.Success(AppointmentSearchResult.Empty);

        var assignedTo = visibility.Value.Enabled && !query.CanViewAll ? query.ActorUserId : (Guid?)null;
        var now = DateTime.UtcNow;
        var found = await search.SearchAsync(
            AppointmentSearchRules.Criteria(query.TenantId, text, now, query.Status) with
            {
                AssignedToUserId = assignedTo,
                TypeId = query.TypeId,
                PersonUserId = query.PersonUserId,
                CustomerId = query.CustomerId,
            },
            ct
        );

        var typesById = (await types.ListAsync(query.TenantId, onlyActive: false, ct)).ToDictionary(t => t.Id);
        var matches = found.Where(a =>
            (query.TypeId is not { } type || a.AppointmentTypeId == type)
            && (query.PersonUserId is not { } person || Participates(a, person))
            && (query.CustomerId is not { } customer || a.BelongsToCustomer(customer))
        );
        return Result.Success(
            AppointmentSearchRules.Shape(matches, typesById, text, query.Status, now, query.Take, staff: true)
        );
    }

    private static bool Participates(Appointment appointment, Guid userId) =>
        appointment.OrganizerUserId == userId || appointment.Attendees.Any(a => a.UserId == userId);
}

/// <summary>Búsqueda del cliente en su portal: solo sus citas y sin datos de otros invitados.</summary>
public static class SearchPortalAppointmentsHandler
{
    public static async Task<Result<AppointmentSearchResult>> Handle(
        SearchPortalAppointmentsQuery query,
        IAppointmentSearch search,
        IAppointmentTypeRepository types,
        CancellationToken ct
    )
    {
        var text = AppointmentSearchRules.Normalize(query.Text);
        if (text is null && query.Status == AppointmentSearchStatus.All)
            return Result.Success(AppointmentSearchResult.Empty);

        var now = DateTime.UtcNow;
        var found = await search.SearchAsync(
            AppointmentSearchRules.Criteria(query.TenantId, text, now, query.Status) with
            {
                PortalCustomerId = query.CustomerId,
            },
            ct
        );

        var typesById = (await types.ListAsync(query.TenantId, onlyActive: false, ct)).ToDictionary(t => t.Id);
        var mine = found.Where(a => a.BelongsToCustomer(query.CustomerId));
        return Result.Success(
            AppointmentSearchRules.Shape(mine, typesById, text, query.Status, now, query.Take, staff: false)
        );
    }
}

internal static class AppointmentSearchRules
{
    public const int MinTextLength = 2;
    public const int MaxTextLength = 100;
    public const int MaxTake = 50;

    // Lo que la base devuelve como mucho; si se llena, se caen primero las más viejas.
    private const int MaxCandidates = 300;

    // Un año para cada lado: lo que alguien busca de verdad en una agenda.
    private static readonly TimeSpan Window = TimeSpan.FromDays(365);

    public static string? Normalize(string? text)
    {
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length < MinTextLength)
            return null;
        return trimmed.Length > MaxTextLength ? trimmed[..MaxTextLength] : trimmed;
    }

    public static AppointmentSearchCriteria Criteria(
        Guid tenantId,
        string? text,
        DateTime now,
        AppointmentSearchStatus status
    ) => new(tenantId, text, now - Window, now + Window, null, null, null, null, null, MaxCandidates, status, now);

    public static AppointmentSearchResult Shape(
        IEnumerable<Appointment> appointments,
        IReadOnlyDictionary<Guid, AppointmentType> typesById,
        string? text,
        AppointmentSearchStatus status,
        DateTime now,
        int take,
        bool staff
    )
    {
        var items = new List<AppointmentSearchItem>();
        foreach (var appointment in appointments)
        {
            if (!Admits(appointment.Status, status))
                continue;

            if (Pick(appointment, now, status) is not { } occurrence)
                continue;

            typesById.TryGetValue(appointment.AppointmentTypeId, out var type);
            items.Add(
                new AppointmentSearchItem(
                    appointment.Id,
                    occurrence.StartUtc,
                    occurrence.EndUtc,
                    occurrence.Title,
                    occurrence.Location,
                    appointment.AppointmentTypeId,
                    type?.Name,
                    type?.ColorHex,
                    appointment.Status.ToString(),
                    occurrence.EndUtc > now,
                    appointment.IsVirtual,
                    appointment.IsRecurring,
                    appointment.OrganizerUserId,
                    appointment.CustomerId,
                    staff ? MatchedPerson(appointment, text) : null
                )
            );
        }

        // Lo que viene primero, lo más cercano arriba; después lo pasado, lo más reciente arriba.
        var ordered = items
            .Where(i => i.IsUpcoming)
            .OrderBy(i => i.StartUtc)
            .Concat(items.Where(i => !i.IsUpcoming).OrderByDescending(i => i.StartUtc))
            .ToList();
        var limit = Math.Clamp(take, 1, MaxTake);
        return new AppointmentSearchResult([.. ordered.Take(limit)], ordered.Count > limit);
    }

    // "All" deja fuera las canceladas: tienen su propio filtro.
    private static bool Admits(AppointmentStatus appointment, AppointmentSearchStatus status) =>
        status switch
        {
            AppointmentSearchStatus.Cancelled => appointment == AppointmentStatus.Cancelled,
            AppointmentSearchStatus.Requests => appointment == AppointmentStatus.Requested,
            _ => appointment != AppointmentStatus.Cancelled,
        };

    private static Occurrence? Pick(Appointment appointment, DateTime now, AppointmentSearchStatus status)
    {
        var expanded = OccurrenceExpander.Expand(appointment, now - Window, now + Window);
        if (expanded.IsFailure || expanded.Value.Count == 0)
            return null;

        Occurrence? next = null;
        Occurrence? last = null;
        foreach (var occurrence in expanded.Value)
        {
            if (occurrence.EndUtc > now)
            {
                next ??= occurrence;
            }
            else
            {
                last = occurrence;
            }
        }

        return status switch
        {
            AppointmentSearchStatus.Upcoming => next,
            AppointmentSearchStatus.Past => last,
            _ => next ?? last,
        };
    }

    // Si el texto no está en la cita misma, dice qué persona coincidió.
    private static string? MatchedPerson(Appointment appointment, string? text)
    {
        if (
            text is null
            || Contains(appointment.Title.Value, text)
            || Contains(appointment.Location?.Value, text)
            || Contains(appointment.Description, text)
        )
            return null;

        return appointment
            .Attendees.FirstOrDefault(a => Contains(a.Snapshot.DisplayName, text) || Contains(a.Snapshot.Email, text))
            ?.Snapshot.DisplayName;
    }

    private static bool Contains(string? value, string text) =>
        value is not null && value.Contains(text, StringComparison.OrdinalIgnoreCase);
}
