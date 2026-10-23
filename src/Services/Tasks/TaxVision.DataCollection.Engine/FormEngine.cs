using System.Text;
using System.Text.Json;
using BuildingBlocks.Results;
using TaxVision.DataCollection.Engine.Answers;
using TaxVision.DataCollection.Engine.Components;
using TaxVision.DataCollection.Engine.Conditions;
using TaxVision.DataCollection.Engine.Definitions;
using TaxVision.DataCollection.Engine.Mapping;
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
    private readonly MappingValidator _mappings;
    private readonly ComponentRegistry _registry;

    public FormEngine(DataCollectionLimits limits)
    {
        _limits = limits;
        var registry = new ComponentRegistry();
        _registry = registry;
        _structure = new StructureValidator(limits, registry);
        _visibility = new VisibilityEvaluator(limits, registry);
        _answers = new AnswersValidator(limits, registry);
        _progress = new ProgressCalculator(registry);
        _mappings = new MappingValidator(registry);
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

    public CanonicalRuleSets CanonicalizeRuleSets(FormDefinition definition) =>
        new(
            CanonicalJson.Serialize<IReadOnlyList<FormMapping>>(definition.Mappings ?? []),
            CanonicalJson.Serialize<IReadOnlyList<FormAction>>(definition.Actions ?? [])
        );

    public ValidationReport ValidateMappings(
        FormDefinition definition,
        MappingCatalogSnapshot catalogSnapshot,
        PublisherCapabilities publisherCapabilities
    ) => _mappings.Validate(definition, catalogSnapshot, publisherCapabilities);

    public Result<FormRuleSets> ParseRuleSets(string mappingSetJson, string actionSetJson)
    {
        var mappings = ParseRuleSet<FormMapping>(mappingSetJson);
        if (mappings.IsFailure)
            return Result.Failure<FormRuleSets>(mappings.Error);
        var actions = ParseRuleSet<FormAction>(actionSetJson);
        return actions.IsFailure
            ? Result.Failure<FormRuleSets>(actions.Error)
            : Result.Success(new FormRuleSets(mappings.Value, actions.Value));
    }

    // Misma semántica que la visibilidad: un campo oculto cuenta como no respondido.
    public bool EvaluateCondition(FormDefinition definition, FormAnswers answers, Condition? condition)
    {
        if (condition is null)
            return true;
        var visibility = EvaluateVisibility(definition, answers);
        return ConditionEvaluator.Evaluate(
            condition,
            answers,
            DefinitionIndex.Create(definition, _registry),
            visibility
        );
    }

    private Result<List<T>> ParseRuleSet<T>(string json) =>
        Parse<List<T>>(
            json,
            _limits.MaxDefinitionBytes,
            IssueCodes.DefinitionTooLarge,
            IssueCodes.DefinitionInvalidJson
        );

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
