using BuildingBlocks.Results;
using TaxVision.DataCollection.Engine;
using TaxVision.DataCollection.Engine.Answers;
using TaxVision.DataCollection.Engine.Conditions;
using TaxVision.DataCollection.Engine.Definitions;
using TaxVision.DataCollection.Engine.Mapping;
using TaxVision.DataCollection.Engine.Progress;
using TaxVision.DataCollection.Engine.Serialization;
using TaxVision.DataCollection.Engine.Validation;
using TaxVision.DataCollection.Engine.Visibility;

namespace TaxVision.Tasks.ClientForms.Application.Common;

/// <summary>El mismo engine, pero validando las respuestas con el "hoy" de la oficina (P13).</summary>
internal sealed class OfficeDayFormEngine(IFormEngine inner, TimeZoneInfo officeZone) : IFormEngine
{
    public DataCollectionLimits Limits => inner.Limits;

    public Result<FormDefinition> ParseDefinition(string json) => inner.ParseDefinition(json);

    public ValidationReport ValidateForDraft(FormDefinition definition) => inner.ValidateForDraft(definition);

    public ValidationReport ValidateForPublish(FormDefinition definition) => inner.ValidateForPublish(definition);

    public Result<FormAnswers> ParseAnswers(string json) => inner.ParseAnswers(json);

    public VisibilityMap EvaluateVisibility(FormDefinition definition, FormAnswers answers) =>
        inner.EvaluateVisibility(definition, answers);

    public AnswersValidationReport ValidateAnswers(
        FormDefinition definition,
        FormAnswers answers,
        AnswersValidationMode mode
    ) => inner.ValidateAnswers(definition, answers, mode, officeZone);

    public AnswersValidationReport ValidateAnswers(
        FormDefinition definition,
        FormAnswers answers,
        AnswersValidationMode mode,
        TimeZoneInfo? zone
    ) => inner.ValidateAnswers(definition, answers, mode, zone ?? officeZone);

    public FormAnswers RemoveHiddenAnswers(FormDefinition definition, FormAnswers answers, VisibilityMap visibility) =>
        inner.RemoveHiddenAnswers(definition, answers, visibility);

    public FormProgress CalculateProgress(FormDefinition definition, FormAnswers answers, VisibilityMap visibility) =>
        inner.CalculateProgress(definition, answers, visibility);

    public CanonicalDefinition Canonicalize(FormDefinition definition) => inner.Canonicalize(definition);

    public CanonicalRuleSets CanonicalizeRuleSets(FormDefinition definition) => inner.CanonicalizeRuleSets(definition);

    public ValidationReport ValidateMappings(
        FormDefinition definition,
        MappingCatalogSnapshot catalogSnapshot,
        PublisherCapabilities publisherCapabilities
    ) => inner.ValidateMappings(definition, catalogSnapshot, publisherCapabilities);

    public Result<FormRuleSets> ParseRuleSets(string mappingSetJson, string actionSetJson) =>
        inner.ParseRuleSets(mappingSetJson, actionSetJson);

    public bool EvaluateCondition(FormDefinition definition, FormAnswers answers, Condition? condition) =>
        inner.EvaluateCondition(definition, answers, condition);
}
