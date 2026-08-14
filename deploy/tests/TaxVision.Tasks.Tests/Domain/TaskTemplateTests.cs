using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.Templates;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Tests.Domain;

public sealed class TaskTemplateTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Ana = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 1, 10, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_template_without_steps_is_rejected()
    {
        var template = NewTemplate();

        Assert.Equal(TaskErrors.Template.StepsRequired, template.ReplaceSteps([], Now).Error);
    }

    /// <summary>Checkpoint 9.4: el ciclo se rechaza al guardar la plantilla, no al aplicarla.</summary>
    [Fact]
    public void A_cycle_between_steps_is_rejected_when_saving_the_template()
    {
        var template = NewTemplate();
        var steps = new[] { Step(1, dependsOn: 3), Step(2, dependsOn: 1), Step(3, dependsOn: 2) };

        Assert.Equal(TaskErrors.Template.StepCycle, template.ReplaceSteps(steps, Now).Error);
        Assert.Empty(template.Steps);
    }

    /// <summary>Un padre que desciende de su propio hijo es el mismo error por otro camino.</summary>
    [Fact]
    public void A_cycle_in_the_subtask_hierarchy_is_rejected()
    {
        var template = NewTemplate();
        var steps = new[] { Step(1, parent: 2), Step(2, parent: 1) };

        Assert.Equal(TaskErrors.Template.ParentCycle, template.ReplaceSteps(steps, Now).Error);
    }

    [Fact]
    public void A_step_that_depends_on_itself_is_rejected_at_construction()
    {
        var step = TaskTemplateStep.Create(
            2,
            TaskTitle.Create("Revisar").Value,
            null,
            TaskPriority.Normal,
            null,
            -10,
            false,
            dependsOnStepOrder: 2,
            parentStepOrder: null,
            suggestedRoleName: null
        );

        Assert.Equal(TaskErrors.Template.StepSelfReference, step.Error);
    }

    [Fact]
    public void A_step_pointing_at_an_order_that_is_not_in_the_template_is_rejected()
    {
        var template = NewTemplate();
        var steps = new[] { Step(1), Step(2, dependsOn: 9) };

        Assert.Equal(TaskErrors.Template.StepReferenceMissing, template.ReplaceSteps(steps, Now).Error);
    }

    [Fact]
    public void Two_steps_cannot_share_the_same_order()
    {
        var template = NewTemplate();

        Assert.Equal(TaskErrors.Template.DuplicateStepOrder, template.ReplaceSteps([Step(1), Step(1)], Now).Error);
    }

    /// <summary>La cadena del 1040: seis pasos, cinco aristas, sin ciclo.</summary>
    [Fact]
    public void A_linear_chain_is_accepted_and_stored_in_order()
    {
        var template = NewTemplate();
        var steps = new[]
        {
            Step(3, dependsOn: 2),
            Step(1),
            Step(2, dependsOn: 1),
            Step(4, dependsOn: 3),
            Step(5, dependsOn: 4),
            Step(6, dependsOn: 5),
        };

        Assert.True(template.ReplaceSteps(steps, Now).IsSuccess);
        Assert.Equal([1, 2, 3, 4, 5, 6], template.Steps.Select(s => s.Order));
        Assert.All(template.Steps, s => Assert.Equal(template.Id, s.TaskTemplateId));
    }

    /// <summary>Guardar de nuevo reemplaza el guion entero: no quedan pasos del anterior.</summary>
    [Fact]
    public void Replacing_the_steps_leaves_no_trace_of_the_previous_script()
    {
        var template = NewTemplate();
        template.ReplaceSteps([Step(1), Step(2, dependsOn: 1)], Now);

        template.ReplaceSteps([Step(1)], Now.AddDays(1));

        Assert.Single(template.Steps);
        Assert.Equal(Now.AddDays(1), template.UpdatedAtUtc);
    }

    [Fact]
    public void A_retired_template_keeps_its_steps_and_can_come_back()
    {
        var template = NewTemplate();
        template.ReplaceSteps([Step(1)], Now);

        template.Retire(Now);
        Assert.False(template.IsActive);
        Assert.Single(template.Steps);

        template.Reactivate(Now);
        Assert.True(template.IsActive);
    }

    private static TaskTemplate NewTemplate() =>
        TaskTemplate.Create(TenantId, Ana, "1040 individual", "El encargo estándar", Now).Value;

    private static TaskTemplateStep Step(int order, int? dependsOn = null, int? parent = null) =>
        TaskTemplateStep
            .Create(
                order,
                TaskTitle.Create($"Paso {order}").Value,
                null,
                TaskPriority.Normal,
                null,
                dueOffsetDays: -45 + (order * 5),
                isStatutory: false,
                dependsOnStepOrder: dependsOn,
                parentStepOrder: parent,
                suggestedRoleName: null
            )
            .Value;
}
