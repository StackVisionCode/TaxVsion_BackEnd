using BuildingBlocks.Results;
using TaxVision.DataCollection.Engine.Answers;
using TaxVision.DataCollection.Engine.Definitions;
using TaxVision.DataCollection.Engine.Progress;
using TaxVision.DataCollection.Engine.Serialization;
using TaxVision.DataCollection.Engine.Validation;
using TaxVision.DataCollection.Engine.Visibility;

namespace TaxVision.DataCollection.Engine;

public interface IFormEngine
{
    DataCollectionLimits Limits { get; }
    Result<FormDefinition> ParseDefinition(string json);
    ValidationReport ValidateForDraft(FormDefinition definition);
    ValidationReport ValidateForPublish(FormDefinition definition);
    Result<FormAnswers> ParseAnswers(string json);
    VisibilityMap EvaluateVisibility(FormDefinition definition, FormAnswers answers);
    AnswersValidationReport ValidateAnswers(FormDefinition definition, FormAnswers answers, AnswersValidationMode mode);
    FormAnswers RemoveHiddenAnswers(FormDefinition definition, FormAnswers answers, VisibilityMap visibility);
    FormProgress CalculateProgress(FormDefinition definition, FormAnswers answers, VisibilityMap visibility);
    CanonicalDefinition Canonicalize(FormDefinition definition);
}
