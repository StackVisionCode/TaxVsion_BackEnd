using TaxVision.Tasks.Application.Templates;
using TaxVision.Tasks.Application.Templates.Abstractions;
using TaxVision.Tasks.Application.Templates.Commands;
using TaxVision.Tasks.Application.Templates.Seed;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.Templates;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Tests.Templates;

public sealed class TaskTemplateAttachmentTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime DueAtUtc = new(2026, 4, 15, 16, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// El checklist del guion llega a la instancia con el mismo <c>fileId</c>: el byte se guarda una
    /// vez en CloudStorage por muchas veces que se aplique la plantilla.
    /// </summary>
    [Fact]
    public void The_reference_file_reaches_the_instance_with_the_same_file_id()
    {
        var fileId = Guid.NewGuid();
        var template = TemplateWith(fileId, stepOrder: null);

        var result = Instantiator().Instantiate(template, Application());

        var withFile = result.Value.Tasks.Where(t => t.Attachments.Any(a => a.FileId == fileId)).ToList();

        Assert.Single(withFile);
        Assert.Equal(AttachmentOrigin.FromTemplate, withFile[0].Attachments[0].Origin);
        Assert.Equal(AttachmentStatus.Available, withFile[0].Attachments[0].Status);
    }

    /// <summary>Sin paso indicado cuelga del primero, que es por donde se empieza a mirar.</summary>
    [Fact]
    public void A_file_without_a_step_hangs_off_the_first_one()
    {
        var template = TemplateWith(Guid.NewGuid(), stepOrder: null);

        var result = Instantiator().Instantiate(template, Application());

        var carrier = result.Value.Tasks.Single(t => t.Attachments.Count > 0);
        Assert.Equal("Solicitar documentos al cliente", carrier.Title.Value);
    }

    [Fact]
    public void A_file_pinned_to_a_step_travels_to_that_step()
    {
        var template = TemplateWith(Guid.NewGuid(), stepOrder: 6);

        var result = Instantiator().Instantiate(template, Application());

        var carrier = result.Value.Tasks.Single(t => t.Attachments.Count > 0);
        Assert.StartsWith("Transmitir", carrier.Title.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_pinned_to_a_step_that_does_not_exist_is_rejected_when_saving()
    {
        var template = NewTemplate();

        var applied = template.ReplaceAttachments(
            [TaskTemplateAttachment.Create(Guid.NewGuid(), "checklist.pdf", "application/pdf", 10, 99)],
            DateTime.UtcNow
        );

        Assert.Equal(TaskErrors.Template.StepReferenceMissing, applied.Error);
    }

    [Fact]
    public void The_same_file_cannot_be_a_reference_twice()
    {
        var template = NewTemplate();
        var fileId = Guid.NewGuid();

        var applied = template.ReplaceAttachments(
            [
                TaskTemplateAttachment.Create(fileId, "checklist.pdf", "application/pdf", 10, null),
                TaskTemplateAttachment.Create(fileId, "checklist-copia.pdf", "application/pdf", 10, null),
            ],
            DateTime.UtcNow
        );

        Assert.Equal(TaskErrors.Template.DuplicateAttachment, applied.Error);
    }

    /// <summary>
    /// ADR-T-13: el 941 del Q2 no lleva los documentos del Q1. La instancia nueva nace limpia; lo
    /// único que se repite son los archivos del guion.
    /// </summary>
    [Fact]
    public void A_fresh_occurrence_inherits_no_attachments_from_the_previous_one()
    {
        var previous = TaskItem
            .Create(
                TenantId,
                UserId,
                TaskTitle.Create("941 Q1").Value,
                null,
                TaskPriority.Normal,
                TaskReference.None,
                null,
                null,
                UserId,
                DueAtUtc
            )
            .Value;
        previous.LinkExistingFile(Guid.NewGuid(), "planilla-q1.pdf", null, 10, UserId, DueAtUtc);

        var next = TaskItem
            .Create(
                TenantId,
                UserId,
                TaskTitle.Create("941 Q2").Value,
                null,
                TaskPriority.Normal,
                TaskReference.None,
                null,
                null,
                UserId,
                DueAtUtc
            )
            .Value;

        Assert.Single(previous.Attachments);
        Assert.Empty(next.Attachments);
    }

    private static TaskTemplateInstantiator Instantiator() =>
        new(new InMemoryTaskRepository(), new InMemoryTaskDependencyRepository());

    private static TemplateApplication Application() =>
        new(UserId, UserId, Guid.NewGuid(), 2025, DueAtUtc, "America/New_York", DateTime.UtcNow);

    private static TaskTemplate NewTemplate()
    {
        var standard = StandardTaxTemplates.All.Single(t => t.RecurrenceRule is null);
        var template = TaskTemplate.Create(TenantId, UserId, standard.Name, standard.Description, DateTime.UtcNow);
        TaskTemplateStepFactory.ApplyTo(template.Value, standard.Steps);

        return template.Value;
    }

    private static TaskTemplate TemplateWith(Guid fileId, int? stepOrder)
    {
        var template = NewTemplate();
        var applied = template.ReplaceAttachments(
            [TaskTemplateAttachment.Create(fileId, "checklist.pdf", "application/pdf", 2048, stepOrder)],
            DateTime.UtcNow
        );

        Assert.True(applied.IsSuccess, applied.IsFailure ? applied.Error.Code : "");

        return template;
    }
}
