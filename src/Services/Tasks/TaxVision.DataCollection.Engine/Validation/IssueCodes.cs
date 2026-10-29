namespace TaxVision.DataCollection.Engine.Validation;

public static class IssueCodes
{
    public const string DefinitionTooLarge = "definition.too_large";
    public const string DefinitionInvalidJson = "definition.invalid_json";
    public const string RepeaterNested = "repeater.nested";
    public const string AnswersTooLarge = "answers.too_large";
    public const string AnswersInvalidJson = "answers.invalid_json";
    public const string MappingInvalidTarget = "mapping.invalid_target";
    public const string MappingUnknownTarget = "mapping.unknown_target";
    public const string MappingDeprecatedTarget = "mapping.deprecated_target";
    public const string MappingTypeMismatch = "mapping.type_mismatch";
    public const string MappingLengthExceedsTarget = "mapping.length_exceeds_target";
    public const string MappingOptionMapIncomplete = "mapping.option_map_incomplete";
    public const string MappingSensitivityMismatch = "mapping.sensitivity_mismatch";
    public const string MappingDuplicateTarget = "mapping.duplicate_target";
    public const string MappingRequiredTargetMissing = "mapping.required_target_missing";
    public const string MappingPermissionDenied = "mapping.permission_denied";
    public const string MappingSourceNotRequired = "mapping.source_not_required";
}
