using BuildingBlocks.Results;
using TaxVision.DataCollection.Engine.Answers;
using TaxVision.DataCollection.Engine.Definitions;
using TaxVision.DataCollection.Engine.Visibility;
using TaxVision.Tasks.ClientForms.Application.Requests;
using TaxVision.Tasks.ClientForms.Domain.Consents;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.ClientForms.Application.Consents.Abstractions;

public interface IFormConsentGate
{
    Task<Result> EnsureBeforeCollectionAsync(
        ParticipantContext context,
        FormRequest request,
        FormDefinition definition,
        FormAnswers? answers,
        bool alwaysProtected,
        CancellationToken ct
    );

    Task<Result<IReadOnlyList<FormConsentRecord>>> EnsureBeforeSubmitAsync(
        ParticipantContext context,
        FormRequest request,
        FormDefinition definition,
        VisibilityMap visibility,
        CancellationToken ct
    );
}
