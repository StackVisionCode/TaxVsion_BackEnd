using TaxVision.DataCollection.Engine;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.ClientForms.Application.Reporting;

/// <summary>
/// Puebla el índice de reporting con la revisión completada. Lo llama el handler de Complete y agrega
/// las filas al mismo DbContext, así se confirman en su transacción. No publica nada: es persistencia.
/// </summary>
public sealed class FormAnswerIndexer(
    IFormTemplateVersionRepository versions,
    IFormEngine engine,
    IAnswersProtector protector,
    IFormAnswerIndexRepository index
)
{
    public async Task IndexCompletedAsync(FormRequest request, DateTime completedAtUtc, CancellationToken ct)
    {
        var revision = request.LatestRevision;
        if (revision is null)
            return;
        var version = await versions.GetByIdAsync(request.TenantId, request.FormTemplateVersionId, ct);
        if (version.IsFailure)
            return;
        var definition = engine.ParseDefinition(version.Value.Definition.Json);
        if (definition.IsFailure)
            return;
        var answers = engine.ParseAnswers(protector.Unprotect(revision.Answers));
        if (answers.IsFailure)
            return;
        var context = new FormAnswerIndexContext(
            request.TenantId,
            request.Id,
            request.FormTemplateId,
            request.VersionNumber,
            request.CustomerId,
            request.ParticipantId,
            revision.RevisionNumber,
            completedAtUtc
        );
        foreach (var entry in FormAnswerIndexBuilder.Build(definition.Value, answers.Value, context))
            index.Add(entry);
    }
}
