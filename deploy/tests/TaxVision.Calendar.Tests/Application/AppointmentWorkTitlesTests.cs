using BuildingBlocks.Contracts.Work;
using Microsoft.Extensions.Options;
using TaxVision.Calendar.Application.Appointments.Abstractions;
using TaxVision.Calendar.Application.Appointments.Work;
using Xunit;

namespace TaxVision.Calendar.Tests.Application;

/// <summary>Títulos en vivo de citas: la visibilidad del que mira llega al lector, se recortan y lo vacío se omite.</summary>
public sealed class AppointmentWorkTitlesTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Viewer = Guid.NewGuid();

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    public async Task Titles_use_the_viewers_assignment_visibility(bool enabled, bool canViewAll, bool restricted)
    {
        var reader = new RecordingTitleReader();
        var id = Guid.NewGuid();

        await HandleAsync(reader, [id], canViewAll, enabled);

        Assert.Equal((Tenant, restricted ? Viewer : (Guid?)null), (reader.TenantId, reader.AssignedTo));
        Assert.Equal([id], reader.Ids);
    }

    [Fact]
    public async Task Titles_are_clipped_and_blank_ones_are_left_out()
    {
        Guid kept = Guid.NewGuid(),
            blank = Guid.NewGuid(),
            longOne = Guid.NewGuid();
        var reader = new RecordingTitleReader
        {
            Titles =
            [
                new WorkTitle(WorkResourceTypes.Appointment, kept, " Kickoff\t call "),
                new WorkTitle(WorkResourceTypes.Appointment, blank, ""),
                new WorkTitle(WorkResourceTypes.Appointment, longOne, new string('x', 300)),
            ],
        };

        var titles = await HandleAsync(reader, [kept, blank, longOne], canViewAll: true, enabled: true);

        Assert.Equal([kept, longOne], titles.Select(t => t.Id));
        Assert.Equal("Kickoff call", titles[0].Title);
        Assert.Equal(WorkLimits.TitleMaxLength, titles[1].Title.Length);
    }

    [Fact]
    public async Task Nothing_asked_reads_nothing()
    {
        var reader = new RecordingTitleReader();

        Assert.Empty(await HandleAsync(reader, [], canViewAll: true, enabled: true));
        Assert.Null(reader.Ids);
    }

    [Fact]
    public void Title_refs_are_capped_and_need_a_list()
    {
        var max = Enumerable
            .Range(0, WorkLimits.TitleBatch)
            .Select(_ => new WorkTitleRef(WorkResourceTypes.Appointment, Guid.NewGuid()))
            .ToList();

        Assert.True(WorkTitleRefs.Validate(new WorkTitlesRequest(max)).IsSuccess);
        Assert.True(WorkTitleRefs.Validate(new WorkTitlesRequest([.. max, max[0]])).IsFailure);
        Assert.True(WorkTitleRefs.Validate(null).IsFailure);
        Assert.True(WorkTitleRefs.Validate(new WorkTitlesRequest(null!)).IsFailure);
    }

    private static Task<IReadOnlyList<WorkTitle>> HandleAsync(
        RecordingTitleReader reader,
        IReadOnlyList<Guid> ids,
        bool canViewAll,
        bool enabled
    ) =>
        GetAppointmentWorkTitlesHandler.Handle(
            new GetAppointmentWorkTitlesQuery(Tenant, Viewer, canViewAll, ids),
            reader,
            Options.Create(new CalendarVisibilityOptions { Enabled = enabled }),
            default
        );

    private sealed class RecordingTitleReader : IAppointmentTitleReader
    {
        public IReadOnlyList<WorkTitle> Titles { get; init; } = [];
        public Guid TenantId { get; private set; }
        public IReadOnlyCollection<Guid>? Ids { get; private set; }
        public Guid? AssignedTo { get; private set; }

        public Task<IReadOnlyList<WorkTitle>> ListTitlesAsync(
            Guid tenantId,
            IReadOnlyCollection<Guid> appointmentIds,
            Guid? assignedToUserId,
            CancellationToken ct = default
        )
        {
            TenantId = tenantId;
            Ids = appointmentIds;
            AssignedTo = assignedToUserId;
            return Task.FromResult(Titles);
        }
    }
}
