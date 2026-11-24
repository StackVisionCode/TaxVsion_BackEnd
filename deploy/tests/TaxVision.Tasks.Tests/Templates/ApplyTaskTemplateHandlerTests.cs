using BuildingBlocks.Messaging.ReminderIntegrationEvents;
using BuildingBlocks.Messaging.TasksIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Reminders;
using TaxVision.Tasks.Application.Series;
using TaxVision.Tasks.Application.Templates;
using TaxVision.Tasks.Application.Templates.Commands;
using TaxVision.Tasks.Application.Templates.Seed;
using TaxVision.Tasks.Domain.Templates;
using TaxVision.Tasks.Tests.Dependencies;

namespace TaxVision.Tasks.Tests.Templates;

// Las tareas que nacen de una plantilla piden su recordatorio igual que las creadas a mano.
public sealed class ApplyTaskTemplateHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Manager = Guid.NewGuid();
    private static readonly Guid Preparer = Guid.NewGuid();
    private static readonly DateTime DueAtUtc = DateTime.UtcNow.AddDays(90);

    [Fact]
    public async Task AppliedTasksWithDueAndAssignee_RequestReminderEach()
    {
        var fixture = new Fixture(Template(recurring: false));

        var result = await fixture.ApplyAsync(Preparer);

        Assert.True(result.IsSuccess);
        var created = fixture.Tasks.Stored;
        var reminders = fixture.Bus.Published.OfType<ReminderRequestedIntegrationEvent>().ToList();
        Assert.Equal(created.Count, reminders.Count);
        Assert.All(
            created,
            task =>
            {
                var reminder = Assert.Single(reminders, r => r.TargetId == task.Id);
                Assert.Equal(TaskReminderContracts.RequestKey(task.Id, Preparer, 0), reminder.RequestKey);
                Assert.Equal(task.Due!.DueAtUtc, reminder.AnchorAtUtc);
            }
        );
        Assert.Equal(created.Count, fixture.Bus.Published.OfType<TaskCreatedIntegrationEvent>().Count());
    }

    [Fact]
    public async Task AppliedTasksWithoutAssignee_RequestNoReminder()
    {
        var fixture = new Fixture(Template(recurring: false));

        await fixture.ApplyAsync(assignee: null);

        Assert.Empty(fixture.Bus.Published.OfType<ReminderRequestedIntegrationEvent>());
    }

    [Fact]
    public async Task RecurringTemplate_FirstInstance_RequestsReminder()
    {
        var fixture = new Fixture(Template(recurring: true));

        var result = await fixture.ApplyAsync(Preparer);

        Assert.True(result.IsSuccess);
        var reminder = Assert.Single(fixture.Bus.Published.OfType<ReminderRequestedIntegrationEvent>());
        Assert.Equal(result.Value.FirstTaskId, reminder.TargetId);
        Assert.Equal(Preparer, reminder.UserId);
    }

    /// <summary>P4/P24: con la fecha civil, el encargo vence al final de ese día en la zona de la oficina.</summary>
    [Fact]
    public async Task A_civil_due_date_ends_at_the_end_of_the_office_day()
    {
        var fixture = new Fixture(Template(recurring: false));

        var result = await fixture.ApplyAsync(Preparer, dueOn: new DateOnly(2027, 4, 15));

        Assert.True(result.IsSuccess);
        var last = fixture.Tasks.Stored.Single(t => t.Due!.IsStatutory);
        Assert.Equal(new DateTime(2027, 4, 16, 4, 59, 59, DateTimeKind.Utc), last.Due!.DueAtUtc); // 23:59:59 CDT
        Assert.Equal("America/Chicago", last.Due.TimeZoneId);
    }

    [Fact]
    public async Task Every_occurrence_of_a_recurring_template_keeps_the_template_and_its_reference_files()
    {
        var template = Template(recurring: true);
        var fileId = Guid.NewGuid();
        template.ReplaceAttachments(
            [TaskTemplateAttachment.Create(fileId, "Checklist.pdf", "application/pdf", 1024, null)],
            DateTime.UtcNow
        );
        var fixture = new Fixture(template);

        var result = await fixture.ApplyAsync(Preparer);
        var first = fixture.Tasks.Stored.Single(t => t.Id == result.Value.FirstTaskId);
        var series = Assert.Single(fixture.Series.Stored);
        series.RegisterInstanceClosed(first.Id);
        var next = await fixture.Materializer.MaterializeNextAsync(series, first.Due!.DueAtUtc, null);

        Assert.Equal(template.Id, series.TemplateId);
        foreach (var task in new[] { first, next.Value })
        {
            Assert.Equal(template.Id, task.TemplateId);
            Assert.Equal(fileId, Assert.Single(task.Attachments).FileId);
        }
    }

    private static TaskTemplate Template(bool recurring)
    {
        var standard = StandardTaxTemplates.All.First(t => (t.RecurrenceRule is not null) == recurring);
        var template = TaskTemplate
            .Create(TenantId, Manager, standard.Name, standard.Description, DateTime.UtcNow)
            .Value;
        Assert.True(
            TaskTemplateStepFactory
                .ApplyRecurrence(template, standard.RecurrenceRule, "America/New_York", standard.RecurrenceMode)
                .IsSuccess
        );
        Assert.True(TaskTemplateStepFactory.ApplyTo(template, standard.Steps).IsSuccess);
        return template;
    }

    private sealed class Fixture(TaskTemplate template)
    {
        public InMemoryTaskRepository Tasks { get; } = new();
        public FakeMessageBus Bus { get; } = new();
        public InMemoryTaskSeriesRepository Series { get; } = new();
        public InMemoryTaskTemplateRepository Templates { get; } = new(template);
        public TaskSeriesMaterializer Materializer => new(Tasks, Series, Templates);

        // Los anuncios salen del handler del evento de alta de cada tarea (el replay los deja en el bus).
        public async Task<Result<TemplateApplicationResponse>> ApplyAsync(Guid? assignee, DateOnly? dueOn = null)
        {
            var result = await ApplyTaskTemplateHandler.Handle(
                new ApplyTaskTemplateCommand(
                    TenantId,
                    Manager,
                    template.Id,
                    assignee,
                    null,
                    2025,
                    DueAtUtc,
                    "America/New_York",
                    false,
                    dueOn
                ),
                Templates,
                new TaskTemplateInstantiator(Tasks, new InMemoryTaskDependencyRepository()),
                Series,
                Materializer,
                new RecordingUnitOfWork(),
                new FixedOfficeZone("America/Chicago"),
                CancellationToken.None
            );
            await DomainEventReplay.DispatchIntoAsync(Bus, Tasks);
            return result;
        }
    }
}
