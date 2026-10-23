using System.Text;
using System.Text.Json;
using BuildingBlocks.Results;
using TaxVision.DataCollection.Engine.Answers;
using TaxVision.DataCollection.Engine.Components;
using TaxVision.DataCollection.Engine.Definitions;
using TaxVision.DataCollection.Engine.Progress;
using TaxVision.DataCollection.Engine.Serialization;
using TaxVision.DataCollection.Engine.Validation;
using TaxVision.DataCollection.Engine.Visibility;

namespace TaxVision.DataCollection.Engine;

public sealed class FormEngine : IFormEngine
{
    private readonly DataCollectionLimits _limits;
    private readonly StructureValidator _structure;
    private readonly VisibilityEvaluator _visibility;
    private readonly AnswersValidator _answers;
    private readonly ProgressCalculator _progress;

    public FormEngine(DataCollectionLimits limits)
    {
        _limits = limits;
        var registry = new ComponentRegistry();
        _structure = new StructureValidator(limits, registry);
        _visibility = new VisibilityEvaluator(limits, registry);
        _answers = new AnswersValidator(limits, registry);
        _progress = new ProgressCalculator(registry);
    }

    public DataCollectionLimits Limits => _limits;

    public Result<FormDefinition> ParseDefinition(string json) =>
        Parse<FormDefinition>(
            json,
            _limits.MaxDefinitionBytes,
            IssueCodes.DefinitionTooLarge,
            IssueCodes.DefinitionInvalidJson
        );

    public ValidationReport ValidateForDraft(FormDefinition definition) =>
        _structure.Validate(definition, publish: false);

    public ValidationReport ValidateForPublish(FormDefinition definition) =>
        _structure.Validate(definition, publish: true);

    public Result<FormAnswers> ParseAnswers(string json) =>
        Parse<FormAnswers>(json, _limits.MaxAnswersBytes, IssueCodes.AnswersTooLarge, IssueCodes.AnswersInvalidJson);

    public VisibilityMap EvaluateVisibility(FormDefinition definition, FormAnswers answers) =>
        _visibility.Evaluate(definition, answers);

    public AnswersValidationReport ValidateAnswers(
        FormDefinition definition,
        FormAnswers answers,
        AnswersValidationMode mode
    )
    {
        var visibility = EvaluateVisibility(definition, answers);
        return _answers.Validate(definition, answers, visibility, mode);
    }

    public FormAnswers RemoveHiddenAnswers(FormDefinition definition, FormAnswers answers, VisibilityMap visibility) =>
        AnswersSanitizer.RemoveHidden(definition, answers, visibility);

    public FormProgress CalculateProgress(FormDefinition definition, FormAnswers answers, VisibilityMap visibility) =>
        _progress.Calculate(definition, answers, visibility);

    public CanonicalDefinition Canonicalize(FormDefinition definition) => CanonicalJson.From(definition);

    private static Result<T> Parse<T>(string json, int maxBytes, string tooLargeCode, string invalidJsonCode)
    {
        if (json is null || Encoding.UTF8.GetByteCount(json) > maxBytes)
            return Result.Failure<T>(new Error(tooLargeCode, "JSON payload exceeds the configured limit."));
        try
        {
            var value = JsonSerializer.Deserialize<T>(json, StrictJson.Options);
            return value is null
                ? Result.Failure<T>(new Error(invalidJsonCode, "JSON payload is empty."))
                : Result.Success(value);
        }
        catch (JsonException)
        {
            return Result.Failure<T>(new Error(invalidJsonCode, "JSON payload is invalid."));
        }
    }
}
