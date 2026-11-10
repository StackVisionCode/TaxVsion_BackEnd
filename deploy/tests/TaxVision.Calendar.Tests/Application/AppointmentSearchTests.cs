using Microsoft.Extensions.Options;
using TaxVision.Calendar.Application.Appointments.Abstractions;
using TaxVision.Calendar.Application.Appointments.Search;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Types;
using TaxVision.Calendar.Domain.ValueObjects;
using Xunit;

namespace TaxVision.Calendar.Tests.Application;

/// <summary>
/// La búsqueda del calendario: ordena lo próximo primero y lo pasado después, una serie aparece una vez
/// (su próxima ocurrencia), los filtros de estado tienen sentido y se dice qué persona coincidió.
/// </summary>
public sealed class AppointmentSearchTests
{
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _olga = Guid.NewGuid();
    private readonly AppointmentType _type;

    public AppointmentSearchTests() =>
        _type = AppointmentType
            .Create(_tenant, "Consulta inicial", TimeSpan.FromMinutes(30), "#336699", DateTime.UtcNow)
            .Value;

    private Appointment At(string title, DateTime start, string? attendee = null)
    {
        var appointment = Appointment
            .Schedule(
                _tenant,
                AppointmentTitle.Create(title).Value,
                EventTiming.PointInTimeOf(start, start.AddMinutes(30), "America/New_York").Value,
                _type.Id,
                _olga,
                DateTime.UtcNow.AddYears(-1)
            )
            .Value;
        if (attendee is not null)
            appointment.AddAttendee(
                AttendeeKind.External,
                null,
                null,
                AttendeeSnapshot.Create(attendee, "guest@client.test").Value,
                isRequired: true,
                _olga,
                DateTime.UtcNow.AddYears(-1),
                canManageAll: false
            );
        return appointment;
    }

    private Task<BuildingBlocks.Results.Result<AppointmentSearchResult>> Search(
        AppointmentSearchStatus status,
        string? text,
        params Appointment[] found
    ) =>
        SearchAppointmentsHandler.Handle(
            new SearchAppointmentsQuery(_tenant, _olga, CanViewAll: true, text, status, null, null, null, 30),
            new FakeSearch(found),
            new SingleTypeRepository(_type),
            Options.Create(new CalendarVisibilityOptions { Enabled = true }),
            CancellationToken.None
        );

    [Fact]
    public async Task Upcoming_comes_first_soonest_first_then_the_past_most_recent_first()
    {
        var nextWeek = At("Revision 1040", DateTime.UtcNow.AddDays(7));
        var tomorrow = At("Revision firma", DateTime.UtcNow.AddDays(1));
        var lastMonth = At("Revision vieja", DateTime.UtcNow.AddDays(-30));
        var yesterday = At("Revision ayer", DateTime.UtcNow.AddDays(-1));

        var result = await Search(AppointmentSearchStatus.All, "revision", nextWeek, tomorrow, lastMonth, yesterday);

        Assert.Equal([tomorrow.Id, nextWeek.Id, yesterday.Id, lastMonth.Id], result.Value.Items.Select(i => i.Id));
        Assert.Equal("Consulta inicial", result.Value.Items[0].TypeName);
    }

    [Fact]
    public async Task Status_filters_mean_what_they_say()
    {
        var upcoming = At("A", DateTime.UtcNow.AddDays(2));
        var past = At("B", DateTime.UtcNow.AddDays(-2));
        var cancelled = At("C", DateTime.UtcNow.AddDays(3));
        cancelled.Cancel(_olga, false, null, DateTime.UtcNow);

        var onlyUpcoming = await Search(AppointmentSearchStatus.Upcoming, null, upcoming, past, cancelled);
        var onlyPast = await Search(AppointmentSearchStatus.Past, null, upcoming, past, cancelled);
        var onlyCancelled = await Search(AppointmentSearchStatus.Cancelled, null, upcoming, past, cancelled);
        var all = await Search(AppointmentSearchStatus.All, null, upcoming, past, cancelled);

        Assert.Equal([upcoming.Id], onlyUpcoming.Value.Items.Select(i => i.Id));
        Assert.Equal([past.Id], onlyPast.Value.Items.Select(i => i.Id));
        Assert.Equal([cancelled.Id], onlyCancelled.Value.Items.Select(i => i.Id));
        // "All" no mezcla las canceladas: para eso está su filtro.
        Assert.DoesNotContain(all.Value.Items, i => i.Id == cancelled.Id);
    }

    [Fact]
    public async Task It_says_which_person_matched_when_the_title_did_not()
    {
        var withAndres = At("Consulta", DateTime.UtcNow.AddDays(2), attendee: "Andres Hernandez");

        var result = await Search(AppointmentSearchStatus.All, "andres", withAndres);

        Assert.Equal("Andres Hernandez", Assert.Single(result.Value.Items).MatchedPerson);
    }

    [Fact]
    public async Task Too_short_a_text_with_no_filter_returns_nothing_instead_of_everything()
    {
        var result = await Search(AppointmentSearchStatus.All, "a", At("Algo", DateTime.UtcNow.AddDays(1)));

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Items);
    }

    /// <summary>La base hace el filtro de texto y visibilidad; aquí se prueba lo que hace el handler con lo que vuelve.</summary>
    private sealed class FakeSearch(params Appointment[] found) : IAppointmentSearch
    {
        public Task<IReadOnlyList<Appointment>> SearchAsync(
            AppointmentSearchCriteria criteria,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<Appointment>>(found);
    }
}
