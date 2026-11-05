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

    private static TaskTemplate Template(bool recurring)
    {
        var standard = StandardTaxTemplates.All.First(t => (t.RecurrenceRule is not null) == recurring);
        var template = TaskTemplate.Create(TenantId, Manager, standard.Name, standard.Description, DateTime.UtcNow).Value;
        Assert.True(
            TaskTemplateStepFactory
                .ApplyRecurrence(template, standard.RecurrenceRule, StandardTaxTemplates.TimeZoneId, standard.RecurrenceMode)
                .IsSuccess
        );
        Assert.True(TaskTemplateStepFactory.ApplyTo(template, standard.Steps).IsSuccess);
        return template;
    }

    private sealed class Fixture(TaskTemplate template)
    {
        public InMemoryTaskRepository Tasks { get; } = new();
        public FakeMessageBus Bus { get; } = new();

        public Task<Result<TemplateApplicationResponse>> ApplyAsync(Guid? assignee)
        {
            var series = new InMemoryTaskSeriesRepository();
            return ApplyTaskTemplateHandler.Handle(
                new ApplyTaskTemplateCommand(
                    TenantId,
                    Manager,
                    template.Id,
                    assignee,
                    null,
                    2025,
                    DueAtUtc,
                    "America/New_York",
                    false
                ),
                new InMemoryTaskTemplateRepository(template),
                new TaskTemplateInstantiator(Tasks, new InMemoryTaskDependencyRepository()),
                series,
                new TaskSeriesMaterializer(Tasks, series),
                new RecordingUnitOfWork(),
                Bus,
                new NoOpCorrelationContext(),
                CancellationToken.None
            );
        }
    }
}
